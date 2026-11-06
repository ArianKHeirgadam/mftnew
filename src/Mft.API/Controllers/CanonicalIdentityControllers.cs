using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Application.Abstractions;
using Mft.Application.Contracts;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController, Route("api/auth"), Authorize]
public sealed class CanonicalAuthController(IAuthService auth, CanonicalIdentityDbContext db) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "نام کاربری و رمز عبور الزامی است." });

        var result = await auth.LoginAsync(request, ct);
        return result is null
            ? Unauthorized(new { message = "نام کاربری یا رمز عبور نادرست است یا برای حساب نقشی تعریف نشده است." })
            : Ok(result);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var id = User.CanonicalUserId();
        if (id is null) return Unauthorized();

        var user = await db.Users.AsNoTracking()
            .Include(item => item.Department)
            .Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(item => item.Id == id.Value, ct);

        if (user is null) return NotFound();

        var roles = user.UserRoles.Select(item =>
            CanonicalRoleNames.ToPolicyRole(item.Role.Name)).Distinct().ToArray();

        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            role = CanonicalIdentityRoleSelection.Primary(roles),
            roles,
            email = user.Email,
            mobile = user.PhoneNumber,
            departmentId = user.DepartmentId,
            department = user.Department?.Name
        });
    }
}

[ApiController, Route("api/users"), Authorize]
public sealed class CanonicalUsersController(CanonicalIdentityDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> List([FromQuery] string? role, [FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking()
            .Include(item => item.Department)
            .Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .AsQueryable();

        var currentDepartmentId = User.CanonicalDepartmentId();
        if (User.IsInRole("Department") || User.IsInRole("Teacher"))
        {
            if (!currentDepartmentId.HasValue) return Forbid();
            query = query.Where(item => item.DepartmentId == currentDepartmentId.Value);
        }

        if (User.IsInRole("Teacher"))
            query = query.Where(item => item.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student));

        if (!string.IsNullOrWhiteSpace(role))
        {
            var databaseRole = CanonicalIdentityRoleSelection.ToDatabaseRole(role);
            if (databaseRole is null) return BadRequest(new { message = "نقش نامعتبر است." });
            query = query.Where(item => item.UserRoles.Any(ur => ur.Role.Name == databaseRole));
        }

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(item => item.FullName.Contains(search) || item.Username.Contains(search));

        var rows = await query.OrderBy(item => item.FullName).Select(item => new
        {
            id = item.Id,
            username = item.Username,
            fullName = item.FullName,
            roles = item.UserRoles.Select(ur => ur.Role.Name),
            email = item.Email,
            mobile = item.PhoneNumber,
            departmentId = item.DepartmentId,
            department = item.Department == null ? null : item.Department.Name,
            isActive = item.IsActive,
            createdAt = item.CreatedAt
        }).ToListAsync(ct);

        return Ok(rows.Select(item => new
        {
            item.id,
            item.username,
            item.fullName,
            role = CanonicalIdentityRoleSelection.Primary(item.roles.Select(CanonicalRoleNames.ToPolicyRole)),
            roles = item.roles.Select(CanonicalRoleNames.ToPolicyRole),
            item.email,
            item.mobile,
            item.departmentId,
            item.department,
            item.isActive,
            item.createdAt
        }));
    }

    [HttpGet("{id:long}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(item => item.Department)
            .Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(item => item.Id == id, ct);

        if (user is null) return NotFound();
        if (!CanManage(user.DepartmentId, user.UserRoles.Select(item => item.Role.Name))) return Forbid();

        var roles = user.UserRoles.Select(item => CanonicalRoleNames.ToPolicyRole(item.Role.Name)).ToArray();
        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            role = CanonicalIdentityRoleSelection.Primary(roles),
            roles,
            email = user.Email,
            mobile = user.PhoneNumber,
            departmentId = user.DepartmentId,
            department = user.Department?.Name,
            isActive = user.IsActive,
            createdAt = user.CreatedAt
        });
    }

    [HttpPost, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        var databaseRole = CanonicalIdentityRoleSelection.ToDatabaseRole(request.Role);
        if (databaseRole is null) return BadRequest(new { message = "نقش نامعتبر است." });
        if (databaseRole == CanonicalRoleNames.SuperAdmin && !User.IsInRole("SuperAdmin")) return Forbid();
        if (User.IsInRole("Teacher") && databaseRole != CanonicalRoleNames.Student) return Forbid();
        if (User.IsInRole("Department") && databaseRole is not (CanonicalRoleNames.Student or CanonicalRoleNames.Teacher)) return Forbid();

        var username = request.Username?.Trim();
        var fullName = request.FullName?.Trim();
        var password = request.Password;
        var phoneNumber = request.Mobile?.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(fullName))
            return BadRequest(new { message = "نام کاربری و نام کامل الزامی است." });
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return BadRequest(new { message = "رمز عبور باید حداقل ۸ کاراکتر باشد." });
        if (phoneNumber?.Length > 40 || (request.Email?.Length ?? 0) > 512)
            return BadRequest(new { message = "طول شماره تماس یا ایمیل از محدودیت دیتابیس بیشتر است." });
        if (username.Length > 200 || fullName.Length > 400)
            return BadRequest(new { message = "طول نام کاربری یا نام کامل از محدودیت دیتابیس بیشتر است." });

        var departmentId = User.IsInRole("Department") || User.IsInRole("Teacher")
            ? User.CanonicalDepartmentId()
            : request.DepartmentId;

        if ((databaseRole == CanonicalRoleNames.Student || databaseRole == CanonicalRoleNames.Teacher) && !departmentId.HasValue)
            return BadRequest(new { message = "برای ساخت دانشجو یا استاد، انتخاب دپارتمان الزامی است." });

        if (departmentId.HasValue && !await db.Departments.AnyAsync(item => item.Id == departmentId.Value, ct))
            return BadRequest(new { message = "دپارتمان انتخاب‌شده معتبر نیست." });

        if (databaseRole == CanonicalRoleNames.DepartmentAdmin && departmentId.HasValue &&
            await db.Users.AnyAsync(item => item.DepartmentId == departmentId.Value &&
                item.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.DepartmentAdmin), ct))
            return Conflict(new { message = "این دپارتمان از قبل مدیر دارد." });

        if (await db.Users.AnyAsync(item => item.Username == username, ct))
            return Conflict(new { message = "این نام کاربری قبلاً ثبت شده است." });
        if (phoneNumber is not null &&
            await db.Users.AnyAsync(item => item.PhoneNumber == phoneNumber, ct))
            return Conflict(new { message = "این شماره تماس قبلاً ثبت شده است." });

        var role = await db.Roles.SingleOrDefaultAsync(item => item.Name == databaseRole, ct);
        if (role is null) return StatusCode(500, new { message = "نقش موردنظر در جدول Roles تعریف نشده است." });

        var user = new CanonicalUser
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            FullName = fullName,
            PhoneNumber = phoneNumber,
            Email = request.Email?.Trim(),
            DepartmentId = departmentId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.UserRoles.Add(new CanonicalUserRole { RoleId = role.Id, Role = role });
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = user.Id }, new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            role = CanonicalRoleNames.ToPolicyRole(databaseRole),
            departmentId = user.DepartmentId
        });
    }

    [HttpPut("{id:long}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Update(long id, UpdateUserRequest request, CancellationToken ct)
    {
        var user = await db.Users.Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (user is null) return NotFound();
        if (!CanManage(user.DepartmentId, user.UserRoles.Select(item => item.Role.Name))) return Forbid();
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "نام کامل الزامی است." });

        if (User.IsInRole("SuperAdmin") && request.DepartmentId.HasValue &&
            !await db.Departments.AnyAsync(item => item.Id == request.DepartmentId.Value, ct))
            return BadRequest(new { message = "دپارتمان انتخاب‌شده معتبر نیست." });

        var phoneNumber = request.Mobile?.Trim();
        if (request.FullName.Trim().Length > 400 || phoneNumber?.Length > 40 ||
            (request.Email?.Length ?? 0) > 512)
            return BadRequest(new { message = "طول یکی از فیلدها از محدودیت دیتابیس بیشتر است." });
        if (phoneNumber is not null &&
            await db.Users.AnyAsync(item => item.Id != id && item.PhoneNumber == phoneNumber, ct))
            return Conflict(new { message = "این شماره تماس قبلاً ثبت شده است." });

        user.FullName = request.FullName.Trim();
        user.Email = request.Email?.Trim();
        user.PhoneNumber = phoneNumber;
        user.IsActive = request.IsActive;
        if (User.IsInRole("SuperAdmin")) user.DepartmentId = request.DepartmentId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:long}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var user = await db.Users.Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (user is null) return NotFound();
        if (User.CanonicalUserId() == id || !CanManage(user.DepartmentId, user.UserRoles.Select(item => item.Role.Name)))
            return Forbid();

        db.Users.Remove(user);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "این حساب در سوابق سامانه استفاده شده و قابل حذف فیزیکی نیست؛ آن را غیرفعال کنید." });
        }
        return NoContent();
    }

    [HttpGet("me/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var id = User.CanonicalUserId();
        if (id is null) return Unauthorized();
        var user = await db.Users.AsNoTracking().Include(item => item.Department)
            .Include(item => item.UserRoles).ThenInclude(item => item.Role)
            .SingleOrDefaultAsync(item => item.Id == id.Value, ct);
        if (user is null) return NotFound();

        var roles = user.UserRoles.Select(item => CanonicalRoleNames.ToPolicyRole(item.Role.Name)).ToArray();
        return Ok(new
        {
            id = user.Id,
            username = user.Username,
            fullName = user.FullName,
            email = user.Email,
            mobile = user.PhoneNumber,
            role = CanonicalIdentityRoleSelection.Primary(roles),
            department = user.Department?.Name
        });
    }

    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest request, CancellationToken ct)
    {
        var id = User.CanonicalUserId();
        if (id is null) return Unauthorized();
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == id.Value, ct);
        if (user is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "نام کامل الزامی است." });

        var phoneNumber = request.Mobile?.Trim();
        if (request.FullName.Trim().Length > 400 || phoneNumber?.Length > 40 ||
            (request.Email?.Length ?? 0) > 512)
            return BadRequest(new { message = "طول یکی از فیلدها از محدودیت دیتابیس بیشتر است." });
        if (phoneNumber is not null &&
            await db.Users.AnyAsync(item => item.Id != id.Value && item.PhoneNumber == phoneNumber, ct))
            return Conflict(new { message = "این شماره تماس قبلاً ثبت شده است." });

        user.FullName = request.FullName.Trim();
        user.Email = request.Email?.Trim();
        user.PhoneNumber = phoneNumber;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private bool CanManage(long? targetDepartmentId, IEnumerable<string> databaseRoles)
    {
        var roles = databaseRoles.ToArray();
        if (User.IsInRole("SuperAdmin")) return true;
        if (!User.CanonicalDepartmentId().HasValue || targetDepartmentId != User.CanonicalDepartmentId())
            return false;
        if (User.IsInRole("Teacher"))
            return roles.Contains(CanonicalRoleNames.Student, StringComparer.Ordinal);
        if (User.IsInRole("Department"))
            return roles.Any(role => role is CanonicalRoleNames.Student or CanonicalRoleNames.Teacher);
        return false;
    }
}

[ApiController, Route("api/departments"), Authorize]
public sealed class CanonicalDepartmentsController(CanonicalIdentityDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> List([FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Departments.AsNoTracking().AsQueryable();
        if (User.IsInRole("Department") || User.IsInRole("Teacher"))
        {
            var departmentId = User.CanonicalDepartmentId();
            if (!departmentId.HasValue) return Forbid();
            query = query.Where(item => item.Id == departmentId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(item => item.Name.Contains(search));

        var rows = await query.OrderBy(item => item.Name).Select(department => new
        {
            id = department.Id,
            name = department.Name,
            isActive = department.IsActive,
            createdAt = department.CreatedAt,
            manager = department.Users
                .Where(user => user.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.DepartmentAdmin))
                .OrderBy(user => user.Id)
                .Select(user => new { id = user.Id, fullName = user.FullName })
                .FirstOrDefault(),
            students = department.Users.Count(user =>
                user.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student)),
            teachers = department.Users.Count(user =>
                user.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Teacher))
        }).ToListAsync(ct);

        return Ok(rows.Select(item => new
        {
            item.id,
            item.name,
            managerUserId = item.manager == null ? (long?)null : item.manager.id,
            manager = item.manager?.fullName,
            item.students,
            item.teachers,
            item.isActive,
            item.createdAt
        }));
    }

    [HttpGet("{id:long}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        if (!User.IsInRole("SuperAdmin") && User.CanonicalDepartmentId() != id) return Forbid();
        var department = await db.Departments.AsNoTracking()
            .Include(item => item.Users).ThenInclude(user => user.UserRoles).ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(item => item.Id == id, ct);
        if (department is null) return NotFound();

        var manager = department.Users.FirstOrDefault(user =>
            user.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.DepartmentAdmin));
        return Ok(new
        {
            id = department.Id,
            name = department.Name,
            managerUserId = manager?.Id,
            isActive = department.IsActive,
            createdAt = department.CreatedAt
        });
    }

    [HttpPost, Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create(DepartmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "نام دپارتمان الزامی است." });
        var name = request.Name.Trim();
        if (await db.Departments.AnyAsync(item => item.Name == name, ct))
            return Conflict(new { message = "نام دپارتمان تکراری است." });

        CanonicalUser? manager = null;
        if (request.ManagerUserId.HasValue)
        {
            manager = await db.Users.Include(item => item.UserRoles).ThenInclude(item => item.Role)
                .SingleOrDefaultAsync(item => item.Id == request.ManagerUserId.Value, ct);
            if (manager is null || !manager.UserRoles.Any(item => item.Role.Name == CanonicalRoleNames.DepartmentAdmin))
                return BadRequest(new { message = "مدیر انتخاب‌شده باید حسابی با نقش DepartmentAdmin داشته باشد." });
            if (manager.DepartmentId.HasValue)
                return BadRequest(new { message = "این مدیر قبلاً به یک دپارتمان متصل شده است." });
        }

        var department = new CanonicalDepartment
        {
            Name = name,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Departments.Add(department);
        await db.SaveChangesAsync(ct);

        if (manager is not null)
        {
            manager.DepartmentId = department.Id;
            await db.SaveChangesAsync(ct);
        }

        return CreatedAtAction(nameof(Get), new { id = department.Id }, new
        {
            id = department.Id,
            name = department.Name,
            managerUserId = manager?.Id,
            isActive = department.IsActive
        });
    }

    [HttpPut("{id:long}"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(long id, DepartmentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "نام دپارتمان الزامی است." });

        var department = await db.Departments.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (department is null) return NotFound();
        var name = request.Name.Trim();
        if (await db.Departments.AnyAsync(item => item.Id != id && item.Name == name, ct))
            return Conflict(new { message = "نام دپارتمان تکراری است." });

        CanonicalUser? newManager = null;
        if (request.ManagerUserId.HasValue)
        {
            newManager = await db.Users.Include(item => item.UserRoles).ThenInclude(item => item.Role)
                .SingleOrDefaultAsync(item => item.Id == request.ManagerUserId.Value, ct);
            if (newManager is null || !newManager.UserRoles.Any(item => item.Role.Name == CanonicalRoleNames.DepartmentAdmin))
                return BadRequest(new { message = "مدیر انتخاب‌شده باید حسابی با نقش DepartmentAdmin داشته باشد." });
            if (newManager.DepartmentId.HasValue && newManager.DepartmentId.Value != id)
                return BadRequest(new { message = "این مدیر قبلاً به دپارتمان دیگری متصل شده است." });
        }

        var previousManagers = await db.Users
            .Where(user => user.DepartmentId == id &&
                user.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.DepartmentAdmin))
            .ToListAsync(ct);

        foreach (var previous in previousManagers)
            if (newManager is null || previous.Id != newManager.Id)
                previous.DepartmentId = null;

        department.Name = name;
        department.IsActive = request.IsActive;
        if (newManager is not null) newManager.DepartmentId = id;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:long}"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var department = await db.Departments.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (department is null) return NotFound();
        if (await db.Users.AnyAsync(user => user.DepartmentId == id, ct))
            return Conflict(new { message = "ابتدا حساب‌های متصل به این دپارتمان را منتقل یا غیرفعال کنید." });

        db.Departments.Remove(department);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

internal static class CanonicalIdentityRoleSelection
{
    public static string? ToDatabaseRole(string? role) => role?.Trim() switch
    {
        "Department" or "DepartmentAdmin" => CanonicalRoleNames.DepartmentAdmin,
        CanonicalRoleNames.SuperAdmin => CanonicalRoleNames.SuperAdmin,
        CanonicalRoleNames.Teacher => CanonicalRoleNames.Teacher,
        CanonicalRoleNames.Student => CanonicalRoleNames.Student,
        _ => null
    };

    public static string? Primary(IEnumerable<string> roles)
    {
        var values = roles.ToArray();
        return new[] { "SuperAdmin", "Department", "Teacher", "Student" }
            .FirstOrDefault(role => values.Contains(role, StringComparer.Ordinal));
    }
}

internal static class CanonicalClaimsExtensions
{
    public static long? CanonicalUserId(this ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static long? CanonicalDepartmentId(this ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirst("departmentId")?.Value, out var id) ? id : null;
}
