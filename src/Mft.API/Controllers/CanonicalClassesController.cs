using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/classes")]
[Authorize(Policy = "TeacherOrAbove")]
public sealed class CanonicalClassesController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? CurrentDepartmentId => User.CanonicalDepartmentId();

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var query = academic.Classes.AsNoTracking().AsQueryable();
        if (!User.IsInRole("SuperAdmin"))
        {
            if (!CurrentDepartmentId.HasValue) return Forbid();
            query = query.Where(x => x.DepartmentId == CurrentDepartmentId.Value);
        }

        var rows = await query.OrderBy(x => x.Name).Select(x => new
        {
            x.Id, x.Name, x.DepartmentId, x.Capacity, x.Schedule,
            MemberCount = academic.ClassMembers.Count(m => m.ClassId == x.Id)
        }).ToListAsync(ct);

        var departmentIds = rows.Select(x => x.DepartmentId).Distinct().ToArray();
        var departments = await identity.Departments.AsNoTracking()
            .Where(x => departmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        return Ok(rows.Select(x => new
        {
            id = x.Id, name = x.Name, departmentId = x.DepartmentId,
            department = departments.GetValueOrDefault(x.DepartmentId),
            capacity = x.Capacity,
            schedule = x.Schedule,
            memberCount = x.MemberCount
        }));
    }

    [HttpGet("{id:long}/members")]
    public async Task<IActionResult> Members(long id, CancellationToken ct)
    {
        var classInfo = await academic.Classes.AsNoTracking()
            .Where(x => x.Id == id).Select(x => new { x.Id, x.Name, x.DepartmentId })
            .FirstOrDefaultAsync(ct);
        if (classInfo is null) return NotFound();
        if (!CanManageDepartment(classInfo.DepartmentId)) return Forbid();

        var studentIds = await academic.ClassMembers.AsNoTracking()
            .Where(x => x.ClassId == id).Select(x => x.StudentUserId).ToListAsync(ct);
        var members = await identity.Users.AsNoTracking()
            .Where(x => studentIds.Contains(x.Id) && x.DepartmentId == classInfo.DepartmentId && x.IsActive &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student))
            .OrderBy(x => x.FullName)
            .Select(x => new { id = x.Id, fullName = x.FullName, username = x.Username })
            .ToListAsync(ct);
        return Ok(new { classInfo.Id, classInfo.Name, classInfo.DepartmentId, members });
    }

    [HttpGet("{id:long}/available-members")]
    public async Task<IActionResult> AvailableMembers(long id, CancellationToken ct)
    {
        var classInfo = await academic.Classes.AsNoTracking()
            .Where(x => x.Id == id).Select(x => new { x.Id, x.DepartmentId }).FirstOrDefaultAsync(ct);
        if (classInfo is null) return NotFound();
        if (!CanManageDepartment(classInfo.DepartmentId)) return Forbid();

        var users = identity.Users.AsNoTracking().Include(x => x.Department)
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Where(x => x.IsActive && x.UserRoles.Any(ur =>
                ur.Role.Name == CanonicalRoleNames.Student || ur.Role.Name == CanonicalRoleNames.Teacher));
        if (!User.IsInRole("SuperAdmin"))
            users = users.Where(x => x.DepartmentId == classInfo.DepartmentId);

        var rows = await users.OrderBy(x => x.FullName).Select(x => new
        {
            id = x.Id, username = x.Username, fullName = x.FullName,
            departmentId = x.DepartmentId, department = x.Department == null ? null : x.Department.Name,
            isStudent = x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student),
            isTeacher = x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Teacher)
        }).ToListAsync(ct);

        return Ok(new
        {
            students = rows.Where(x => x.isStudent).Select(x => new { x.id, x.username, x.fullName, x.departmentId, x.department, isActive = true }),
            teachers = rows.Where(x => x.isTeacher).Select(x => new { x.id, x.username, x.fullName, x.departmentId, x.department, isActive = true })
        });
    }

    [HttpGet("{id:long}/teachers")]
    public async Task<IActionResult> Teachers(long id, CancellationToken ct)
    {
        var classInfo = await academic.Classes.AsNoTracking()
            .Where(x => x.Id == id).Select(x => new { x.Id, x.DepartmentId }).FirstOrDefaultAsync(ct);
        if (classInfo is null) return NotFound();
        if (!CanManageDepartment(classInfo.DepartmentId)) return Forbid();

        var teacherIds = await academic.ClassTeachers.AsNoTracking()
            .Where(x => x.ClassId == id).Select(x => x.TeacherUserId).ToListAsync(ct);
        var teachers = await identity.Users.AsNoTracking()
            .Where(x => teacherIds.Contains(x.Id) && x.DepartmentId == classInfo.DepartmentId && x.IsActive &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Teacher))
            .OrderBy(x => x.FullName)
            .Select(x => new { id = x.Id, fullName = x.FullName, username = x.Username })
            .ToListAsync(ct);
        return Ok(new { classId = id, teachers });
    }

    [HttpPut("{id:long}/teachers")]
    public async Task<IActionResult> UpdateTeachers(long id, CanonicalClassTeachersRequest request, CancellationToken ct)
    {
        var classInfo = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (classInfo is null) return NotFound();
        if (!CanManageDepartment(classInfo.DepartmentId)) return Forbid();

        var teacherIds = request.TeacherIds ?? new List<long>();
        if (teacherIds.Count > 500 || teacherIds.Any(x => x <= 0) ||
            teacherIds.Distinct().Count() != teacherIds.Count)
            return BadRequest(new { message = "فهرست استادان نامعتبر یا دارای شناسه تکراری است." });

        var validIds = await identity.Users
            .Where(x => teacherIds.Contains(x.Id) && x.DepartmentId == classInfo.DepartmentId && x.IsActive &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Teacher))
            .Select(x => x.Id).ToListAsync(ct);
        if (validIds.Count != teacherIds.Count)
            return BadRequest(new { message = "همه استادان باید استاد فعال همان دپارتمان باشند." });

        await using var transaction = await academic.Database.BeginTransactionAsync(ct);
        var current = await academic.ClassTeachers.Where(x => x.ClassId == id).ToListAsync(ct);
        var desired = validIds.ToHashSet();
        var existing = current.Select(x => x.TeacherUserId).ToHashSet();
        academic.ClassTeachers.RemoveRange(current.Where(x => !desired.Contains(x.TeacherUserId)));
        var addedAt = DateTime.UtcNow;
        academic.ClassTeachers.AddRange(desired.Except(existing).Select(teacherId =>
            new CanonicalClassTeacher { ClassId = id, TeacherUserId = teacherId, AddedAt = addedAt }));
        await academic.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { classId = id, teacherCount = validIds.Count, teacherIds = validIds });
    }

    [HttpPut("{id:long}/roster")]
    public async Task<IActionResult> UpdateRoster(long id, CanonicalClassRosterRequest request, CancellationToken ct)
    {
        var classInfo = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (classInfo is null) return NotFound();
        if (!CanManageDepartment(classInfo.DepartmentId)) return Forbid();

        var studentIds = request.StudentIds ?? new List<long>();
        var teacherIds = request.TeacherIds ?? new List<long>();
        if (classInfo.Capacity.HasValue && studentIds.Count > classInfo.Capacity.Value)
            return BadRequest(new { message = "تعداد دانشجویان انتخاب‌شده از ظرفیت تعریف‌شده کلاس بیشتر است." });
        if (studentIds.Count > 2000 || studentIds.Any(x => x <= 0) || studentIds.Distinct().Count() != studentIds.Count)
            return BadRequest(new { message = "فهرست دانشجویان نامعتبر یا دارای شناسه تکراری است." });
        if (teacherIds.Count > 500 || teacherIds.Any(x => x <= 0) || teacherIds.Distinct().Count() != teacherIds.Count)
            return BadRequest(new { message = "فهرست استادان نامعتبر یا دارای شناسه تکراری است." });

        var validStudentIds = await identity.Users.Where(x => studentIds.Contains(x.Id) &&
            x.DepartmentId == classInfo.DepartmentId && x.IsActive &&
            x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student)).Select(x => x.Id).ToListAsync(ct);
        if (validStudentIds.Count != studentIds.Count)
            return BadRequest(new { message = "همه دانشجویان باید دانشجوی فعال همان دپارتمان باشند." });

        var validTeacherIds = await identity.Users.Where(x => teacherIds.Contains(x.Id) &&
            x.DepartmentId == classInfo.DepartmentId && x.IsActive &&
            x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Teacher)).Select(x => x.Id).ToListAsync(ct);
        if (validTeacherIds.Count != teacherIds.Count)
            return BadRequest(new { message = "همه استادان باید استاد فعال همان دپارتمان باشند." });

        await using var transaction = await academic.Database.BeginTransactionAsync(ct);
        var currentStudents = await academic.ClassMembers.Where(x => x.ClassId == id).ToListAsync(ct);
        var desiredStudents = validStudentIds.ToHashSet();
        var existingStudents = currentStudents.Select(x => x.StudentUserId).ToHashSet();
        academic.ClassMembers.RemoveRange(currentStudents.Where(x => !desiredStudents.Contains(x.StudentUserId)));
        var addedAt = DateTime.UtcNow;
        academic.ClassMembers.AddRange(desiredStudents.Except(existingStudents).Select(studentId =>
            new CanonicalClassMember { ClassId = id, StudentUserId = studentId, AddedAt = addedAt }));

        var currentTeachers = await academic.ClassTeachers.Where(x => x.ClassId == id).ToListAsync(ct);
        var desiredTeachers = validTeacherIds.ToHashSet();
        var existingTeachers = currentTeachers.Select(x => x.TeacherUserId).ToHashSet();
        academic.ClassTeachers.RemoveRange(currentTeachers.Where(x => !desiredTeachers.Contains(x.TeacherUserId)));
        academic.ClassTeachers.AddRange(desiredTeachers.Except(existingTeachers).Select(teacherId =>
            new CanonicalClassTeacher { ClassId = id, TeacherUserId = teacherId, AddedAt = addedAt }));

        await academic.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { classId = id, studentCount = validStudentIds.Count, teacherCount = validTeacherIds.Count });
    }

    [HttpPost]
    public async Task<IActionResult> Create(CanonicalClassRequest request, CancellationToken ct)
    {
        if (User.IsInRole("Teacher")) return Forbid();
        var name = request.Name?.Trim();
        if (request.Capacity.HasValue && request.Capacity.Value <= 0)
            return BadRequest(new { message = "ظرفیت کلاس باید عددی بزرگ‌تر از صفر باشد." });
        if (request.Schedule?.Trim().Length > 200)
            return BadRequest(new { message = "زمان برگزاری حداکثر ۲۰۰ کاراکتر است." });
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return BadRequest(new { message = "نام کلاس الزامی و حداکثر ۲۰۰ کاراکتر است." });

        var departmentId = User.IsInRole("SuperAdmin") ? request.DepartmentId : CurrentDepartmentId;
        if (!departmentId.HasValue ||
            !await identity.Departments.AnyAsync(x => x.Id == departmentId.Value && x.IsActive, ct))
            return BadRequest(new { message = "دپارتمان فعال و معتبر برای کلاس الزامی است." });

        if (await academic.Classes.AnyAsync(x => x.DepartmentId == departmentId.Value && x.Name == name, ct))
            return Conflict(new { message = "کلاسی با این نام در دپارتمان انتخاب‌شده وجود دارد." });

        var entity = new CanonicalClass
        {
            Name = name,
            DepartmentId = departmentId.Value,
            Capacity = request.Capacity,
            Schedule = string.IsNullOrWhiteSpace(request.Schedule) ? null : request.Schedule.Trim(),
            CreatedAt = DateTime.UtcNow
        };
        academic.Classes.Add(entity);
        await academic.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Members), new { id = entity.Id },
            new { id = entity.Id, name = entity.Name, departmentId = entity.DepartmentId, capacity = entity.Capacity, schedule = entity.Schedule, memberCount = 0 });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, CanonicalClassUpdateRequest request, CancellationToken ct)
    {
        var entity = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManageDepartment(entity.DepartmentId)) return Forbid();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return BadRequest(new { message = "نام کلاس الزامی و حداکثر ۲۰۰ کاراکتر است." });
        var schedule = request.Schedule?.Trim();
        if (schedule?.Length > 200)
            return BadRequest(new { message = "زمان برگزاری حداکثر ۲۰۰ کاراکتر است." });
        if (await academic.Classes.AnyAsync(x => x.Id != id && x.DepartmentId == entity.DepartmentId && x.Name == name, ct))
            return Conflict(new { message = "کلاسی با این نام در این دپارتمان وجود دارد." });
        entity.Name = name;
        entity.Schedule = string.IsNullOrWhiteSpace(schedule) ? null : schedule;
        await academic.SaveChangesAsync(ct);
        return Ok(new { id = entity.Id, name = entity.Name, schedule = entity.Schedule });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var entity = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManageDepartment(entity.DepartmentId)) return Forbid();
        var hasStudents = await academic.ClassMembers.AnyAsync(x => x.ClassId == id, ct);
        var hasTeachers = await academic.ClassTeachers.AnyAsync(x => x.ClassId == id, ct);
        var hasExams = await academic.Exams.AnyAsync(x => x.ClassId == id, ct);
        if (hasStudents || hasTeachers || hasExams)
            return Conflict(new { message = "این کلاس عضو یا آزمون مرتبط دارد؛ برای جلوگیری از حذف سوابق، ابتدا ارتباط‌های آن را مدیریت کنید." });
        academic.Classes.Remove(entity);
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{id:long}/capacity")]
    public async Task<IActionResult> UpdateCapacity(long id, CanonicalClassCapacityRequest request, CancellationToken ct)
    {
        var entity = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManageDepartment(entity.DepartmentId)) return Forbid();
        if (request.Capacity <= 0 || request.Capacity > 100000)
            return BadRequest(new { message = "ظرفیت باید بین ۱ تا ۱۰۰۰۰۰ نفر باشد." });
        var currentMembers = await academic.ClassMembers.CountAsync(x => x.ClassId == id, ct);
        if (request.Capacity < currentMembers)
            return BadRequest(new { message = "ظرفیت نمی‌تواند از تعداد دانشجویان فعلی کلاس کمتر باشد." });
        entity.Capacity = request.Capacity;
        await academic.SaveChangesAsync(ct);
        return Ok(new { classId = id, capacity = entity.Capacity, memberCount = currentMembers });
    }

    [HttpPut("{id:long}/members")]
    public async Task<IActionResult> UpdateMembers(long id, CanonicalClassMembersRequest request, CancellationToken ct)
    {
        var entity = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManageDepartment(entity.DepartmentId)) return Forbid();

        var studentIds = request.StudentIds ?? new List<long>();
        if (entity.Capacity.HasValue && studentIds.Count > entity.Capacity.Value)
            return BadRequest(new { message = "تعداد دانشجویان انتخاب‌شده از ظرفیت تعریف‌شده کلاس بیشتر است." });
        if (studentIds.Count > 2000 || studentIds.Any(x => x <= 0) ||
            studentIds.Distinct().Count() != studentIds.Count)
            return BadRequest(new { message = "فهرست اعضای کلاس نامعتبر یا دارای شناسه تکراری است." });

        var validIds = await identity.Users
            .Where(x => studentIds.Contains(x.Id) && x.DepartmentId == entity.DepartmentId && x.IsActive &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student))
            .Select(x => x.Id).ToListAsync(ct);
        if (validIds.Count != studentIds.Count)
            return BadRequest(new { message = "همه اعضا باید دانشجوی فعال همان دپارتمان باشند." });

        await using var transaction = await academic.Database.BeginTransactionAsync(ct);
        var current = await academic.ClassMembers.Where(x => x.ClassId == id).ToListAsync(ct);
        var desiredIds = validIds.ToHashSet();
        var currentIds = current.Select(x => x.StudentUserId).ToHashSet();

        academic.ClassMembers.RemoveRange(current.Where(x => !desiredIds.Contains(x.StudentUserId)));
        var addedAt = DateTime.UtcNow;
        academic.ClassMembers.AddRange(desiredIds.Except(currentIds).Select(studentId =>
            new CanonicalClassMember { ClassId = id, StudentUserId = studentId, AddedAt = addedAt }));

        await academic.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return Ok(new { classId = id, memberCount = validIds.Count, memberIds = validIds });
    }

    private bool CanManageDepartment(long departmentId) =>
        User.IsInRole("SuperAdmin") || CurrentDepartmentId == departmentId;
}

public sealed class CanonicalClassRequest
{
    public string Name { get; set; } = "";
    public long? DepartmentId { get; set; }
    public int? Capacity { get; set; }
    public string? Schedule { get; set; }
}

public sealed class CanonicalClassUpdateRequest
{
    public string Name { get; set; } = "";
    public string? Schedule { get; set; }
}

public sealed class CanonicalClassCapacityRequest
{
    public int Capacity { get; set; }
}

public sealed class CanonicalClassMembersRequest
{
    public List<long> StudentIds { get; set; } = new();
}

public sealed class CanonicalClassTeachersRequest
{
    public List<long> TeacherIds { get; set; } = new();
}

public sealed class CanonicalClassRosterRequest
{
    public List<long> StudentIds { get; set; } = new();
    public List<long> TeacherIds { get; set; } = new();
}
