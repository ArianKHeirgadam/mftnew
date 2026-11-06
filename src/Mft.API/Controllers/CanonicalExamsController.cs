using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/exams")]
[Authorize]
public sealed class CanonicalExamsController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? CurrentUserId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var isStudent = User.IsInRole("Student");
        var studentId = CurrentUserId;
        if (isStudent && studentId is null) return Unauthorized();
        var q = academic.Exams.AsNoTracking().AsQueryable();
        if (isStudent)
            q = q.Where(x => x.IsPublished && x.DepartmentId == CurrentDepartmentId && x.ClassId.HasValue &&
                academic.ClassMembers.Any(m => m.ClassId == x.ClassId.Value && m.StudentUserId == studentId));
        else if (User.IsInRole("Department"))
            q = q.Where(x => x.DepartmentId == CurrentDepartmentId);
        else if (User.IsInRole("Teacher"))
            q = q.Where(x => x.CreatedByUserId == CurrentUserId);
        var rows = await q.OrderByDescending(x => x.Id).Select(x => new {
            x.Id, x.Title, x.ExamCategoryId, x.DepartmentId, x.ClassId, x.CreatedByUserId,
            x.StartAt, x.EndAt, x.DurationMinutes, x.IsPublished,
            SectionCount = academic.ExamSections.Count(s => s.ExamId == x.Id),
            QuestionCount = academic.ExamSections.Where(s => s.ExamId == x.Id)
                .SelectMany(s => academic.ExamQuestions.Where(eq => eq.ExamSectionId == s.Id)).Count(),
            ExistingAttemptId = isStudent
                ? academic.ExamAttempts.Where(a => a.ExamId == x.Id && a.StudentUserId == studentId).Select(a => (long?)a.Id).FirstOrDefault()
                : null,
            ExistingAttemptStatusId = isStudent
                ? academic.ExamAttempts.Where(a => a.ExamId == x.Id && a.StudentUserId == studentId).Select(a => (byte?)a.StatusId).FirstOrDefault()
                : null,
            HasAttempt = isStudent && academic.ExamAttempts.Any(a => a.ExamId == x.Id && a.StudentUserId == studentId)
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (exam is null) return NotFound();
        var isEnrolledStudent = User.IsInRole("Student") && exam.IsPublished &&
            exam.DepartmentId == CurrentDepartmentId && exam.ClassId.HasValue &&
            await academic.ClassMembers.AnyAsync(m => m.ClassId == exam.ClassId.Value && m.StudentUserId == CurrentUserId, ct);
        if (!await CanManage(exam, ct) && !isEnrolledStudent)
            return Forbid();
        var sections = await academic.ExamSections.AsNoTracking().Where(x => x.ExamId == id)
            .OrderBy(x => x.DisplayOrder).Select(x => new {
                x.Id, x.SkillId, x.DurationMinutes, x.DisplayOrder, x.WeightPercent,
                Questions = academic.ExamQuestions.Where(q => q.ExamSectionId == x.Id)
                    .OrderBy(q => q.DisplayOrder).Select(q => new { q.Id, q.QuestionId, q.DisplayOrder, q.PointsOverride }).ToList()
            }).ToListAsync(ct);
        return Ok(new { exam.Id, exam.Title, exam.ExamCategoryId, exam.DepartmentId, exam.ClassId, exam.CreatedByUserId,
            exam.StartAt, exam.EndAt, exam.DurationMinutes, exam.IsPublished, Sections = sections });
    }

    [HttpPost]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Create(ExamUpsert request, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        var departmentId = User.IsInRole("SuperAdmin") ? request.DepartmentId : CurrentDepartmentId;
        var validation = await Validate(request, departmentId, ct);
        if (validation is not null) return validation;
        if (!departmentId.HasValue || !await identity.Departments.AnyAsync(x => x.Id == departmentId && x.IsActive, ct))
            return BadRequest(new { message = "دپارتمان فعال و معتبر برای آزمون الزامی است." });
        var exam = new CanonicalExam {
            Title = request.Title.Trim(), ExamCategoryId = request.ExamCategoryId,
            DepartmentId = departmentId.Value, ClassId = request.ClassId, CreatedByUserId = CurrentUserId.Value,
            StartAt = request.StartAt, EndAt = request.EndAt,
            DurationMinutes = request.DurationMinutes, IsPublished = request.IsPublished
        };
        academic.Exams.Add(exam);
        await academic.SaveChangesAsync(ct);
        await ReplaceSections(exam.Id, request.Sections, ct);
        return CreatedAtAction(nameof(Get), new { id = exam.Id }, new { id = exam.Id });
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Update(long id, ExamUpsert request, CancellationToken ct)
    {
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        if (await academic.ExamAttempts.AnyAsync(x => x.ExamId == id, ct))
            return Conflict(new { message = "پس از ثبت اولین تلاش دانشجو، ساختار آزمون و سؤال‌های آن قابل تغییر نیستند تا سوابق پاسخ‌ها مخدوش نشوند." });
        var validation = await Validate(request, exam.DepartmentId, ct);
        if (validation is not null) return validation;
        exam.Title = request.Title.Trim();
        exam.ClassId = request.ClassId;
        exam.ExamCategoryId = request.ExamCategoryId;
        exam.StartAt = request.StartAt;
        exam.EndAt = request.EndAt;
        exam.DurationMinutes = request.DurationMinutes;
        exam.IsPublished = request.IsPublished;
        await academic.SaveChangesAsync(ct);
        await ReplaceSections(id, request.Sections, ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        // ExamAttempts have NO_ACTION FK to Exams; never cascade/delete historical attempts.
        return Conflict(new { message = "حذف آزمون از طریق این API انجام نمی‌شود؛ ابتدا باید سیاست نگهداری سوابق آزمون و تلاش‌های دانشجویان مشخص شود." });
    }

    private async Task<IActionResult?> Validate(ExamUpsert r, long? departmentId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 300 || r.ClassId <= 0 ||
            r.DurationMinutes is < 1 or > 600 || r.EndAt <= r.StartAt ||
            r.Sections.Count == 0 || r.Sections.SelectMany(x => x.QuestionIds).Distinct().Count() != r.Sections.Sum(x => x.QuestionIds.Count))
            return BadRequest(new { message = "عنوان، زمان‌بندی، مدت آزمون و فهرست بخش‌ها/سؤال‌ها معتبر نیست." });
        if (!await academic.ExamCategories.AnyAsync(x => x.Id == r.ExamCategoryId, ct))
            return BadRequest(new { message = "دسته‌بندی آزمون معتبر نیست." });
        if (!departmentId.HasValue || !await academic.Classes.AnyAsync(x =>
            x.Id == r.ClassId && x.DepartmentId == departmentId.Value, ct))
            return BadRequest(new { message = "کلاس انتخاب‌شده معتبر نیست یا به دپارتمان آزمون تعلق ندارد." });
        if (r.Sections.Any(x => x.SkillId <= 0 || x.DurationMinutes < 1 || x.QuestionIds.Count == 0 || x.WeightPercent <= 0))
            return BadRequest(new { message = "هر بخش باید مهارت، زمان، وزن و حداقل یک سؤال معتبر داشته باشد." });
        if (Math.Abs(r.Sections.Sum(x => x.WeightPercent) - 100m) > 0.01m)
            return BadRequest(new { message = "مجموع وزن بخش‌های آزمون باید دقیقاً ۱۰۰ درصد باشد." });
        var skillIds = r.Sections.Select(x => x.SkillId).Distinct().ToArray();
        if (await academic.Skills.CountAsync(x => skillIds.Contains(x.Id) && x.ExamCategoryId == r.ExamCategoryId, ct) != skillIds.Length)
            return BadRequest(new { message = "یک یا چند مهارت به دسته‌بندی انتخاب‌شده تعلق ندارد." });
        var questionIds = r.Sections.SelectMany(x => x.QuestionIds).Distinct().ToArray();
        if (await academic.Questions.CountAsync(x => questionIds.Contains(x.Id) && skillIds.Contains(x.SkillId), ct) != questionIds.Length)
            return BadRequest(new { message = "یک یا چند سؤال معتبر نیست یا با مهارت بخش آزمون تطابق ندارد." });
        if (User.IsInRole("Teacher") && await academic.Questions.AnyAsync(x => questionIds.Contains(x.Id) && x.CreatedByUserId != CurrentUserId, ct))
            return BadRequest(new { message = "فقط سؤال‌های ثبت‌شده توسط خودتان قابل استفاده هستند." });
        if (User.IsInRole("Department") && await academic.Questions.AnyAsync(x => questionIds.Contains(x.Id) &&
            !identity.Users.Any(u => u.Id == x.CreatedByUserId && u.DepartmentId == CurrentDepartmentId), ct))
            return BadRequest(new { message = "یک یا چند سؤال خارج از دپارتمان شماست." });
        return null;
    }

    private async Task ReplaceSections(long examId, List<ExamSectionInput> inputs, CancellationToken ct)
    {
        var old = await academic.ExamSections.Where(x => x.ExamId == examId).ToListAsync(ct);
        if (old.Count > 0) academic.ExamSections.RemoveRange(old);
        await academic.SaveChangesAsync(ct);
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var section = new CanonicalExamSection {
                ExamId = examId, SkillId = input.SkillId,
                DurationMinutes = input.DurationMinutes, DisplayOrder = i + 1,
                WeightPercent = input.WeightPercent
            };
            academic.ExamSections.Add(section);
            await academic.SaveChangesAsync(ct);
            for (var j = 0; j < input.QuestionIds.Count; j++)
                academic.ExamQuestions.Add(new CanonicalExamQuestion {
                    ExamSectionId = section.Id, QuestionId = input.QuestionIds[j],
                    DisplayOrder = j + 1
                });
        }
        await academic.SaveChangesAsync(ct);
    }

    private async Task<bool> CanManage(CanonicalExam exam, CancellationToken ct)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        if (User.IsInRole("Teacher")) return exam.CreatedByUserId == CurrentUserId;
        return User.IsInRole("Department") && exam.DepartmentId == CurrentDepartmentId;
    }
}

public sealed class ExamUpsert
{
    public string Title { get; set; } = "";
    public long ExamCategoryId { get; set; }
    public long? DepartmentId { get; set; }
    public long ClassId { get; set; }
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public int DurationMinutes { get; set; }
    public bool IsPublished { get; set; }
    public List<ExamSectionInput> Sections { get; set; } = new();
}
public sealed class ExamSectionInput
{
    public long SkillId { get; set; }
    public int DurationMinutes { get; set; }
    public decimal WeightPercent { get; set; }
    public List<long> QuestionIds { get; set; } = new();
}
