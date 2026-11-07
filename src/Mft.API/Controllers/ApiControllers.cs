using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Application.Abstractions;
using Mft.Application.Contracts;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;
namespace Mft.API.Controllers;
[ApiController, Route("api/classes"), Authorize]
public sealed class ClassesController(CanonicalAcademicDbContext academic, CanonicalIdentityDbContext identity) : ControllerBase
{
    [HttpGet, Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var classes = await academic.Classes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
        var departmentIds = classes.Select(x => x.DepartmentId).Distinct().ToArray();
        var departments = await identity.Departments.AsNoTracking().Where(x => departmentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var ids = classes.Select(x => x.Id).ToArray();
        var members = await academic.ClassMembers.AsNoTracking().Where(x => ids.Contains(x.ClassId))
            .GroupBy(x => x.ClassId).Select(g => new { ClassId = g.Key, Count = g.Count(), Ids = g.Select(x => x.StudentUserId).ToList() }).ToListAsync(ct);
        var memberMap = members.ToDictionary(x => x.ClassId);
        return Ok(classes.Select(x => new { id = x.Id, name = x.Name, departmentId = x.DepartmentId,
            department = departments.GetValueOrDefault(x.DepartmentId),
            memberCount = memberMap.GetValueOrDefault(x.Id)?.Count ?? 0,
            memberIds = memberMap.GetValueOrDefault(x.Id)?.Ids ?? new List<long>() }));
    }

    [HttpGet("{id:long}/members"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Members(long id, CancellationToken ct)
    {
        var item = await academic.Classes.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new { x.Id, x.DepartmentId, x.Name }).FirstOrDefaultAsync(ct);
        if (item is null) return NotFound();
        var members = await academic.ClassMembers.AsNoTracking().Where(x => x.ClassId == id)
            .Join(identity.Users.AsNoTracking(), m => m.StudentUserId, u => u.Id,
                (m, u) => new { id = u.Id, name = u.FullName, username = u.Username })
            .OrderBy(x => x.name).ToListAsync(ct);
        return Ok(new { item.Id, item.Name, item.DepartmentId, members });
    }

    [HttpPut("{id:long}/members"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateMembers(long id, [FromBody] JsonElement payload, CancellationToken ct)
    {
        var item = await academic.Classes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return NotFound();
        if (!payload.TryGetProperty("studentIds", out var values) || values.ValueKind != JsonValueKind.Array)
            return BadRequest(new { message = "فهرست اعضای کلاس معتبر نیست." });
        var studentIds = values.EnumerateArray().Where(x => x.TryGetInt64(out _))
            .Select(x => x.GetInt64()).Distinct().ToArray();
        if (studentIds.Length != values.GetArrayLength())
            return BadRequest(new { message = "شناسه اعضا تکراری یا نامعتبر است." });
        var valid = await identity.Users.Where(x => studentIds.Contains(x.Id) && x.IsActive &&
            x.DepartmentId == item.DepartmentId && x.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student))
            .Select(x => x.Id).ToListAsync(ct);
        if (valid.Count != studentIds.Length)
            return BadRequest(new { message = "همه اعضا باید دانشجوی فعال همان دپارتمان باشند." });
        var current = await academic.ClassMembers.Where(x => x.ClassId == id).ToListAsync(ct);
        academic.ClassMembers.RemoveRange(current);
        academic.ClassMembers.AddRange(valid.Select(studentId => new CanonicalClassMember { ClassId = id, StudentUserId = studentId }));
        await academic.SaveChangesAsync(ct);
        return Ok(new { classId = id, memberCount = valid.Count });
    }

    [HttpPost, Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] JsonElement payload, CancellationToken ct)
    {
        var name = payload.TryGetProperty("name", out var n) ? n.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return BadRequest(new { message = "نام کلاس الزامی و حداکثر ۲۰۰ کاراکتر است." });
        if (!payload.TryGetProperty("departmentId", out var raw) || !raw.TryGetInt64(out var departmentId))
            return BadRequest(new { message = "دپارتمان کلاس را انتخاب کنید." });
        if (!await identity.Departments.AnyAsync(x => x.Id == departmentId, ct))
            return BadRequest(new { message = "دپارتمان انتخاب‌شده معتبر نیست." });
        var studentIds = payload.TryGetProperty("studentIds", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(x => x.TryGetInt64(out _)).Select(x => x.GetInt64()).Distinct().ToArray()
            : Array.Empty<long>();
        if (values.ValueKind == JsonValueKind.Array && studentIds.Length != values.GetArrayLength())
            return BadRequest(new { message = "شناسه اعضا تکراری یا نامعتبر است." });
        var valid = await identity.Users.Where(x => studentIds.Contains(x.Id) && x.IsActive && x.DepartmentId == departmentId &&
            x.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student)).Select(x => x.Id).ToListAsync(ct);
        if (valid.Count != studentIds.Length)
            return BadRequest(new { message = "همه اعضا باید دانشجوی فعال همان دپارتمان باشند." });
        var entity = new CanonicalClass { Name = name, DepartmentId = departmentId, CreatedAt = DateTime.UtcNow };
        academic.Classes.Add(entity);
        await academic.SaveChangesAsync(ct);
        academic.ClassMembers.AddRange(valid.Select(id => new CanonicalClassMember { ClassId = entity.Id, StudentUserId = id, AddedAt = DateTime.UtcNow }));
        await academic.SaveChangesAsync(ct);
        return Ok(new { id = entity.Id, name = entity.Name, memberCount = valid.Count });
    }
}

[ApiController, Route("api/notifications"), Authorize]
public sealed class NotificationsController(CanonicalOperationsDbContext operations, CanonicalIdentityDbContext identity, CanonicalAcademicDbContext academic) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] bool? unread, CancellationToken ct)
    {
        var userId = User.CanonicalUserId();
        if (!userId.HasValue) return Unauthorized();
        var query = operations.Notifications.AsNoTracking().Where(x => x.UserId == userId.Value);
        if (unread == true) query = query.Where(x => !x.IsRead);
        if (unread == false) query = query.Where(x => x.IsRead);
        return Ok(await query.OrderByDescending(x => x.CreatedAt).ToListAsync(ct));
    }

    [HttpPost, Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create([FromBody] JsonElement payload, CancellationToken ct)
    {
        var title = payload.TryGetProperty("title", out var t) ? t.GetString()?.Trim() : null;
        var message = payload.TryGetProperty("message", out var m) ? m.GetString()?.Trim() : null;
        var audience = payload.TryGetProperty("audience", out var a) ? a.GetString() : null;
        if (string.IsNullOrWhiteSpace(title) || title.Length > 250 || string.IsNullOrWhiteSpace(message))
            return BadRequest(new { message = "عنوان و متن اعلان الزامی است." });
        IQueryable<long> recipients = identity.Users.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id);
        if (string.Equals(audience, "Department", StringComparison.OrdinalIgnoreCase))
        {
            if (!payload.TryGetProperty("departmentId", out var raw) || !long.TryParse(raw.ToString(), out var departmentId))
                return BadRequest(new { message = "دپارتمان مقصد را انتخاب کنید." });
            if (!await identity.Departments.AnyAsync(x => x.Id == departmentId, ct))
                return BadRequest(new { message = "دپارتمان انتخاب‌شده معتبر نیست." });
            recipients = recipients.Where(id => identity.Users.Any(u => u.Id == id && u.DepartmentId == departmentId &&
                !u.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.SuperAdmin)));
        }
        else if (string.Equals(audience, "Managers", StringComparison.OrdinalIgnoreCase))
            recipients = recipients.Where(id => identity.Users.Any(u => u.Id == id && u.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.DepartmentAdmin)));
        else if (string.Equals(audience, "Class", StringComparison.OrdinalIgnoreCase))
        {
            if (!payload.TryGetProperty("classId", out var raw) || !long.TryParse(raw.ToString(), out var classId))
                return BadRequest(new { message = "کلاس مقصد را انتخاب کنید." });
            if (!await academic.Classes.AnyAsync(x => x.Id == classId, ct))
                return BadRequest(new { message = "کلاس انتخاب‌شده معتبر نیست." });
            recipients = recipients.Where(id => academic.ClassMembers.Any(x => x.ClassId == classId && x.StudentUserId == id));
        }
        else if (string.Equals(audience, "User", StringComparison.OrdinalIgnoreCase))
        {
            if (!payload.TryGetProperty("userId", out var raw) || !long.TryParse(raw.ToString(), out var userId))
                return BadRequest(new { message = "کاربر مقصد را انتخاب کنید." });
            recipients = recipients.Where(id => id == userId);
        }
        else return BadRequest(new { message = "گروه گیرندگان نامعتبر است." });

        var ids = await recipients.Distinct().ToListAsync(ct);
        if (ids.Count == 0) return BadRequest(new { message = "برای این گروه گیرنده فعالی پیدا نشد." });
        var now = DateTime.UtcNow;
        operations.Notifications.AddRange(ids.Select(id => new CanonicalNotification { UserId = id, Title = title, Message = message, IsRead = false, CreatedAt = now }));
        await operations.SaveChangesAsync(ct);
        return Ok(new { sent = ids.Count, message = "اعلان برای گیرندگان انتخاب‌شده ثبت شد." });
    }

    [HttpPut("{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        var userId = User.CanonicalUserId();
        if (!userId.HasValue) return Unauthorized();
        var item = await operations.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, ct);
        if (item is null) return NotFound();
        item.IsRead = true;
        await operations.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        var userId = User.CanonicalUserId();
        if (!userId.HasValue) return Unauthorized();
        await operations.Notifications.Where(x => x.UserId == userId.Value && !x.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true), ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var userId = User.CanonicalUserId();
        if (!userId.HasValue) return Unauthorized();
        var item = await operations.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId.Value, ct);
        if (item is null) return NotFound();
        operations.Notifications.Remove(item);
        await operations.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/dashboard"), Authorize]
public sealed class DashboardController(CanonicalIdentityDbContext identity, CanonicalAcademicDbContext academic) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var users = identity.Users.AsNoTracking().AsQueryable();
        var departments = identity.Departments.AsNoTracking().AsQueryable();
        var questions = academic.Questions.AsNoTracking().AsQueryable();
        var exams = academic.Exams.AsNoTracking().AsQueryable();
        var attempts = academic.ExamAttempts.AsNoTracking().AsQueryable();
        var departmentId = User.CanonicalDepartmentId();
        var userId = User.CanonicalUserId();

        if (User.IsInRole("Department"))
        {
            if (!departmentId.HasValue) return Forbid();
            users = users.Where(x => x.DepartmentId == departmentId.Value);
            departments = departments.Where(x => x.Id == departmentId.Value);
            questions = questions.Where(x => identity.Users.Any(u => u.Id == x.CreatedByUserId && u.DepartmentId == departmentId.Value));
            exams = exams.Where(x => x.DepartmentId == departmentId.Value);
            attempts = attempts.Where(x => academic.Exams.Any(e => e.Id == x.ExamId && e.DepartmentId == departmentId.Value));
        }
        if (User.IsInRole("Teacher"))
        {
            if (!userId.HasValue) return Unauthorized();
            users = users.Where(u => u.DepartmentId == departmentId && u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student));
            departments = departments.Where(d => d.Id == departmentId);
            questions = questions.Where(q => q.CreatedByUserId == userId.Value);
            exams = exams.Where(e => e.CreatedByUserId == userId.Value);
            attempts = attempts.Where(a => academic.Exams.Any(e => e.Id == a.ExamId && e.CreatedByUserId == userId.Value));
        }
        if (User.IsInRole("Student"))
        {
            if (!userId.HasValue) return Unauthorized();
            attempts = attempts.Where(a => a.StudentUserId == userId.Value);
            var available = academic.Exams.Where(e => e.IsPublished && (e.DepartmentId == departmentId || departmentId == null));
            var total = await attempts.CountAsync(ct);
            var completed = await attempts.CountAsync(a => a.SubmittedAt != null, ct);
            var average = await attempts.Where(a => a.FinalScore != null).Select(a => (double?)a.FinalScore).AverageAsync(ct) ?? 0;
            return Ok(new { exams = await available.CountAsync(ct), attempts = total, completed, averageScore = average });
        }

        var studentCount = await users.CountAsync(u => u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student), ct);
        var teacherCount = await users.CountAsync(u => u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Teacher), ct);
        return Ok(new
        {
            users = await users.CountAsync(ct), students = studentCount, teachers = teacherCount,
            departments = await departments.CountAsync(ct), questions = await questions.CountAsync(ct),
            exams = await exams.CountAsync(ct), attempts = await attempts.CountAsync(ct)
        });
    }

    [HttpGet("activity")]
    public async Task<IActionResult> Activity(CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-7);
        var query = academic.ExamAttempts.AsNoTracking().Where(x => x.StartedAt >= since);
        if (User.IsInRole("Department"))
        {
            var departmentId = User.CanonicalDepartmentId();
            if (!departmentId.HasValue) return Forbid();
            query = query.Where(x => academic.Exams.Any(e => e.Id == x.ExamId && e.DepartmentId == departmentId.Value));
        }
        if (User.IsInRole("Teacher"))
        {
            var userId = User.CanonicalUserId();
            if (!userId.HasValue) return Unauthorized();
            query = query.Where(x => academic.Exams.Any(e => e.Id == x.ExamId && e.CreatedByUserId == userId.Value));
        }
        if (User.IsInRole("Student"))
        {
            var userId = User.CanonicalUserId();
            if (!userId.HasValue) return Unauthorized();
            query = query.Where(x => x.StudentUserId == userId.Value);
        }
        var data = await query.GroupBy(x => x.StartedAt.Date)
            .Select(g => new { date = g.Key, count = g.Count() }).ToListAsync(ct);
        return Ok(data);
    }
}
[ApiController, Route("api/settings"), Authorize(Policy = "SuperAdmin")]
public sealed class SettingsController(CanonicalOperationsDbContext operations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await operations.AppSettings.AsNoTracking().OrderBy(x => x.Key).ToListAsync(ct));

    [HttpPut]
    public async Task<IActionResult> Save(SaveSettingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Key) || request.Key.Length > 100 || request.Value is null)
            return BadRequest(new { message = "کلید و مقدار تنظیمات معتبر الزامی است." });
        var setting = await operations.AppSettings.SingleOrDefaultAsync(x => x.Key == request.Key, ct);
        if (setting is null)
            operations.AppSettings.Add(new CanonicalAppSetting { Key = request.Key, Value = request.Value, UpdatedAt = DateTime.UtcNow });
        else { setting.Value = request.Value; setting.UpdatedAt = DateTime.UtcNow; }
        await operations.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/reports"), Authorize(Policy = "DepartmentOrAbove")]
public sealed class ReportsController(CanonicalIdentityDbContext identity, CanonicalAcademicDbContext academic) : ControllerBase
{
    [HttpGet("departments")]
    public async Task<IActionResult> Departments(CancellationToken ct)
    {
        var query = identity.Departments.AsNoTracking().AsQueryable();
        if (User.IsInRole("Department"))
        {
            var current = User.CanonicalDepartmentId();
            if (!current.HasValue) return Forbid();
            query = query.Where(x => x.Id == current.Value);
        }
        var departments = await query.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var rows = new List<object>();
        foreach (var department in departments)
        {
            var studentIds = identity.Users.Where(u => u.DepartmentId == department.Id && u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student)).Select(u => u.Id);
            var teacherIds = identity.Users.Where(u => u.DepartmentId == department.Id && u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Teacher)).Select(u => u.Id);
            var examIds = academic.Exams.Where(e => e.DepartmentId == department.Id).Select(e => e.Id);
            var graded = academic.ExamAttempts.Where(a => examIds.Contains(a.ExamId) && a.FinalScore != null);
            var gradedCount = await graded.CountAsync(ct);
            var average = await graded.Select(a => (double?)a.FinalScore).AverageAsync(ct) ?? 0;
            var passed = await graded.CountAsync(a => a.FinalScore >= 50m, ct);
            rows.Add(new { departmentId = department.Id, department = department.Name,
                students = await studentIds.CountAsync(ct), teachers = await teacherIds.CountAsync(ct),
                exams = await examIds.CountAsync(ct), averageScore = average, gradedAttempts = gradedCount,
                passedAttempts = passed, passRate = gradedCount == 0 ? 0d : passed * 100d / gradedCount,
                participation = await academic.ExamAttempts.CountAsync(a => examIds.Contains(a.ExamId), ct) });
        }
        if (User.IsInRole("SuperAdmin"))
        {
            var unassignedStudents = await identity.Users.CountAsync(u => u.DepartmentId == null && u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Student), ct);
            var unassignedTeachers = await identity.Users.CountAsync(u => u.DepartmentId == null && u.UserRoles.Any(r => r.Role.Name == CanonicalRoleNames.Teacher), ct);
            if (unassignedStudents > 0 || unassignedTeachers > 0)
                rows.Add(new { departmentId = (long?)null, department = "بدون دپارتمان", students = unassignedStudents,
                    teachers = unassignedTeachers, exams = 0, averageScore = 0d, gradedAttempts = 0,
                    passedAttempts = 0, passRate = 0d, participation = 0 });
        }
        return Ok(rows);
    }

    [HttpGet("recent-attempts")]
    public async Task<IActionResult> RecentAttempts([FromQuery] long? departmentId, [FromQuery] string? search,
        [FromQuery] int take = 100, CancellationToken ct = default)
    {
        var query = from attempt in academic.ExamAttempts.AsNoTracking()
                    join exam in academic.Exams.AsNoTracking() on attempt.ExamId equals exam.Id
                    join student in identity.Users.AsNoTracking() on attempt.StudentUserId equals student.Id
                    join department in identity.Departments.AsNoTracking() on exam.DepartmentId equals department.Id
                    where attempt.SubmittedAt != null
                    select new { attempt, exam, student, department };
        if (User.IsInRole("Department"))
        {
            var current = User.CanonicalDepartmentId();
            if (!current.HasValue) return Forbid();
            query = query.Where(x => x.exam.DepartmentId == current.Value);
        }
        else if (departmentId.HasValue) query = query.Where(x => x.exam.DepartmentId == departmentId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.exam.Title.Contains(term) || x.student.FullName.Contains(term));
        }
        var rows = await query.OrderBy(x => x.department.Name).ThenByDescending(x => x.attempt.SubmittedAt)
            .Take(Math.Clamp(take, 1, 250))
            .Select(x => new { attemptId = x.attempt.Id, departmentId = x.exam.DepartmentId,
                department = x.department.Name, studentId = x.student.Id, student = x.student.FullName,
                exam = x.exam.Title, score = x.attempt.FinalScore, status = x.attempt.StatusId,
                startedAt = x.attempt.StartedAt, submittedAt = x.attempt.SubmittedAt }).ToListAsync(ct);
        return Ok(rows);
    }
}
static class ClaimsExtensions{
    public static Guid UserId(this ClaimsPrincipal u) => Guid.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id)?id:Guid.Empty;
    public static Guid? DepartmentId(this ClaimsPrincipal u) => Guid.TryParse(u.FindFirst("departmentId")?.Value, out var id)?id:null;
}


[ApiController, Route("api/attendance"), Authorize(Policy = "TeacherOrAbove")]
public sealed class AttendanceController(CanonicalIdentityDbContext identity, CanonicalOperationsDbContext operations) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? date, [FromQuery] long? departmentId, CancellationToken ct)
    {
        var effectiveDepartmentId = User.IsInRole("SuperAdmin") ? departmentId : User.CanonicalDepartmentId();
        if (!effectiveDepartmentId.HasValue) return BadRequest(new { message = "دپارتمان را انتخاب کنید." });
        if (!await identity.Departments.AnyAsync(x => x.Id == effectiveDepartmentId.Value, ct))
            return NotFound(new { message = "دپارتمان پیدا نشد." });
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var students = await identity.Users.AsNoTracking().Where(x => x.IsActive && x.DepartmentId == effectiveDepartmentId.Value &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student))
            .OrderBy(x => x.FullName).Select(x => new { id = x.Id, fullName = x.FullName, username = x.Username }).ToListAsync(ct);
        var ids = students.Select(x => x.id).ToArray();
        var marks = await operations.AttendanceRecords.AsNoTracking()
            .Where(x => x.DepartmentId == effectiveDepartmentId.Value && x.AttendanceDate == targetDate && ids.Contains(x.StudentUserId))
            .ToDictionaryAsync(x => x.StudentUserId, x => x.Status, ct);
        return Ok(new { date = targetDate, departmentId = effectiveDepartmentId,
            students = students.Select(x => new { x.id, x.fullName, x.username, status = marks.GetValueOrDefault(x.id) }) });
    }

    [HttpPut]
    public async Task<IActionResult> Save(CanonicalSaveAttendanceRequest request, CancellationToken ct)
    {
        var departmentId = User.IsInRole("SuperAdmin") ? request.DepartmentId : User.CanonicalDepartmentId();
        if (!departmentId.HasValue) return BadRequest(new { message = "دپارتمان را انتخاب کنید." });
        if (request.Records is null || request.Records.Count == 0) return BadRequest(new { message = "حداقل وضعیت یک دانشجو را مشخص کنید." });
        if (request.Records.Count > 1000) return BadRequest(new { message = "در هر بار حداکثر ۱۰۰۰ دانشجو قابل ثبت است." });
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Present", "Absent", "Deleted" };
        if (request.Records.Any(x => !allowed.Contains(x.Status))) return BadRequest(new { message = "وضعیت حضور و غیاب نامعتبر است." });
        var ids = request.Records.Select(x => x.StudentId).Distinct().ToArray();
        if (ids.Length != request.Records.Count) return BadRequest(new { message = "برای یک دانشجو وضعیت تکراری ارسال شده است." });
        var valid = await identity.Users.Where(x => ids.Contains(x.Id) && x.IsActive && x.DepartmentId == departmentId.Value &&
                x.UserRoles.Any(ur => ur.Role.Name == CanonicalRoleNames.Student))
            .Select(x => x.Id).ToListAsync(ct);
        if (valid.Count != ids.Length) return BadRequest(new { message = "یک یا چند دانشجو به این دپارتمان تعلق ندارند." });
        var actorId = User.CanonicalUserId();
        if (!actorId.HasValue) return Unauthorized();
        var existing = await operations.AttendanceRecords.Where(x => x.DepartmentId == departmentId.Value &&
            x.AttendanceDate == request.Date && ids.Contains(x.StudentUserId)).ToDictionaryAsync(x => x.StudentUserId, ct);
        foreach (var item in request.Records)
        {
            if (existing.TryGetValue(item.StudentId, out var record))
            {
                record.Status = item.Status; record.RecordedByUserId = actorId.Value; record.CreatedAt = DateTime.UtcNow;
            }
            else operations.AttendanceRecords.Add(new CanonicalAttendanceRecord { StudentUserId = item.StudentId,
                DepartmentId = departmentId.Value, RecordedByUserId = actorId.Value, AttendanceDate = request.Date,
                Status = item.Status, CreatedAt = DateTime.UtcNow });
        }
        await operations.SaveChangesAsync(ct);
        return Ok(new { message = "حضور و غیاب ذخیره شد.", saved = request.Records.Count, date = request.Date });
    }
}
public sealed class CanonicalSaveAttendanceRequest
{
    public DateOnly Date { get; set; }
    public long? DepartmentId { get; set; }
    public List<CanonicalAttendanceItem>? Records { get; set; }
}
public sealed class CanonicalAttendanceItem
{
    public long StudentId { get; set; }
    public string Status { get; set; } = "";
}
