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
            x.Id, x.Name, x.DepartmentId,
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

    [HttpPost]
    public async Task<IActionResult> Create(CanonicalClassRequest request, CancellationToken ct)
    {
        var name = request.Name?.Trim();
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
            CreatedAt = DateTime.UtcNow
        };
        academic.Classes.Add(entity);
        await academic.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Members), new { id = entity.Id },
            new { id = entity.Id, name = entity.Name, departmentId = entity.DepartmentId, memberCount = 0 });
    }

    [HttpPut("{id:long}/members")]
    public async Task<IActionResult> UpdateMembers(long id, CanonicalClassMembersRequest request, CancellationToken ct)
    {
        var entity = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!CanManageDepartment(entity.DepartmentId)) return Forbid();

        var studentIds = request.StudentIds ?? new List<long>();
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
}

public sealed class CanonicalClassMembersRequest
{
    public List<long> StudentIds { get; set; } = new();
}
