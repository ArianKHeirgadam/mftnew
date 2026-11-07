using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/teacher/essay-grading")]
[Authorize(Roles = "Teacher")]
public sealed class CanonicalEssayGradingController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity,
    IWebHostEnvironment environment) : ControllerBase
{
    private long? TeacherId =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private string StorageDirectory =>
        Path.Combine(environment.ContentRootPath, "App_Data", "exam-files");

    private async Task<bool> CanAccessExam(CanonicalExam exam, CancellationToken ct)
    {
        if (TeacherId is null || !exam.ClassId.HasValue) return false;

        return await identity.Users.AsNoTracking().AnyAsync(user =>
                user.Id == TeacherId.Value &&
                user.IsActive &&
                user.DepartmentId == exam.DepartmentId &&
                user.UserRoles.Any(role => role.Role.Name == CanonicalRoleNames.Teacher), ct) &&
            await academic.ClassTeachers.AsNoTracking().AnyAsync(assignment =>
                assignment.ClassId == exam.ClassId.Value &&
                assignment.TeacherUserId == TeacherId.Value, ct);
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue(CancellationToken ct)
    {
        if (TeacherId is null) return Unauthorized();

        var rows = await academic.ExamAttempts.AsNoTracking()
            .Where(attempt =>
                attempt.ManualReviewRequired.HasValue &&
                attempt.GradingStatus == "PendingManual" &&
                academic.Exams.Any(exam =>
                    exam.Id == attempt.ExamId &&
                    exam.ClassId.HasValue &&
                    academic.ClassTeachers.Any(assignment =>
                        assignment.ClassId == exam.ClassId.Value &&
                        assignment.TeacherUserId == TeacherId.Value)))
            .OrderBy(attempt => attempt.SubmittedAt)
            .Take(200)
            .Select(attempt => new
            {
                attempt.Id,
                attempt.ExamId,
                attempt.StudentUserId,
                attempt.SubmittedAt,
                attempt.AutoGradedScore,
                attempt.ManualReviewRequired,
                Sections = academic.ExamSections
                    .Where(section => section.ExamId == attempt.ExamId &&
                        section.SectionType == "Essay")
                    .OrderBy(section => section.DisplayOrder)
                    .Select(section => new
                    {
                        section.Id,
                        section.DisplayOrder,
                        section.TotalPoints,
                        QuestionPaperFileName = academic.ExamSectionDocuments
                            .Where(document => document.ExamSectionId == section.Id)
                            .Select(document => document.OriginalFileName).FirstOrDefault(),
                        AnswerFileName = academic.ExamSectionSubmissionDocuments
                            .Where(document => document.AttemptId == attempt.Id &&
                                document.ExamSectionId == section.Id)
                            .Select(document => document.OriginalFileName).FirstOrDefault(),
                        Grade = academic.ExamSectionManualGrades
                            .Where(grade => grade.AttemptId == attempt.Id &&
                                grade.ExamSectionId == section.Id)
                            .Select(grade => new { grade.Score, grade.Feedback, grade.GradedAt, grade.RowVersion })
                            .FirstOrDefault()
                    }).ToList()
            }).ToListAsync(ct);

        var studentIds = rows.Select(row => row.StudentUserId).Distinct().ToArray();
        var students = await identity.Users.AsNoTracking()
            .Where(user => studentIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.FullName, ct);

        return Ok(rows.Select(row => new
        {
            row.Id,
            row.ExamId,
            row.SubmittedAt,
            row.AutoGradedScore,
            row.ManualReviewRequired,
            Student = students.GetValueOrDefault(row.StudentUserId) ?? "دانشجوی نامشخص",
            Sections = row.Sections.Select(section => new
            {
                section.Id,
                section.DisplayOrder,
                section.TotalPoints,
                section.QuestionPaperFileName,
                section.AnswerFileName,
                HasAnswerPdf = section.AnswerFileName is not null,
                section.Grade
            })
        }));
    }

    [HttpGet("attempts/{attemptId:long}/sections/{sectionId:long}/answer.pdf")]
    public async Task<IActionResult> DownloadAnswer(long attemptId, long sectionId, CancellationToken ct)
    {
        var loaded = await LoadAuthorizedSection(attemptId, sectionId, ct);
        if (loaded is null) return NotFound();

        var document = await academic.ExamSectionSubmissionDocuments.AsNoTracking()
            .FirstOrDefaultAsync(item => item.AttemptId == attemptId &&
                item.ExamSectionId == sectionId, ct);
        if (document is null) return NotFound();

        var storedName = document.StoredFileName;
        if (Path.GetFileName(storedName) != storedName ||
            !Path.GetExtension(storedName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return NotFound();

        var path = Path.Combine(StorageDirectory, storedName);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/pdf", document.OriginalFileName,
            enableRangeProcessing: true);
    }

    [HttpGet("attempts/{attemptId:long}/sections/{sectionId:long}/history")]
    public async Task<IActionResult> History(long attemptId, long sectionId, CancellationToken ct)
    {
        var loaded = await LoadAuthorizedSection(attemptId, sectionId, ct);
        if (loaded is null) return NotFound();

        var history = await academic.ExamSectionManualGradeHistory.AsNoTracking()
            .Where(item => item.AttemptId == attemptId && item.ExamSectionId == sectionId)
            .OrderByDescending(item => item.ChangedAt)
            .Select(item => new
            {
                item.Id,
                item.PreviousScore,
                item.NewScore,
                item.PreviousFeedback,
                item.NewFeedback,
                item.ChangedByUserId,
                item.ChangedAt
            }).ToListAsync(ct);

        return Ok(history);
    }

    [HttpPut("attempts/{attemptId:long}/sections/{sectionId:long}/grade")]
    public async Task<IActionResult> Grade(
        long attemptId,
        long sectionId,
        CanonicalEssayGradeRequest request,
        CancellationToken ct)
    {
        var loaded = await LoadAuthorizedSection(attemptId, sectionId, ct);
        if (loaded is null) return NotFound();

        var (attempt, section) = loaded.Value;
        if (attempt.StatusId == 1 || attempt.SubmittedAt is null)
            return Conflict(new { message = "فقط پاسخنامه ثبت‌شده قابل تصحیح است." });

        var maxScore = section.TotalPoints;
        if (!maxScore.HasValue)
        {
            maxScore = await academic.ExamQuestions.AsNoTracking()
                .Where(question => question.ExamSectionId == sectionId)
                .Select(question => question.PointsOverride ??
                    academic.Questions.Where(item => item.Id == question.QuestionId)
                        .Select(item => item.Points).FirstOrDefault())
                .SumAsync(ct);
        }

        if (request.Score < 0m || request.Score > maxScore.Value)
            return BadRequest(new { message = "نمره باید بین صفر و بارم این بخش باشد.", maxScore });

        var feedback = request.Feedback?.Trim();
        if (feedback?.Length > 2000)
            return BadRequest(new { message = "بازخورد نمی‌تواند بیشتر از ۲۰۰۰ کاراکتر باشد." });

        var grade = await academic.ExamSectionManualGrades
            .FirstOrDefaultAsync(item => item.AttemptId == attemptId &&
                item.ExamSectionId == sectionId, ct);

        var previousScore = grade?.Score;
        var previousFeedback = grade?.Feedback;
        if (grade is not null)
        {
            if (string.IsNullOrWhiteSpace(request.RowVersion))
                return BadRequest(new { message = "برای ویرایش نمره، نسخه قبلی رکورد الزامی است." });

            byte[] expectedVersion;
            try { expectedVersion = Convert.FromBase64String(request.RowVersion); }
            catch (FormatException) { return BadRequest(new { message = "RowVersion معتبر نیست." }); }

            academic.Entry(grade).Property(item => item.RowVersion).OriginalValue = expectedVersion;
            grade.Score = request.Score;
            grade.Feedback = feedback;
            grade.GradedByUserId = TeacherId!.Value;
            grade.GradedAt = DateTime.UtcNow;
        }
        else
        {
            grade = new CanonicalExamSectionManualGrade
            {
                AttemptId = attemptId,
                ExamSectionId = sectionId,
                Score = request.Score,
                Feedback = feedback,
                GradedByUserId = TeacherId!.Value,
                GradedAt = DateTime.UtcNow
            };
            academic.ExamSectionManualGrades.Add(grade);
        }

        academic.ExamSectionManualGradeHistory.Add(new CanonicalExamSectionManualGradeHistory
        {
            ManualGrade = grade,
            AttemptId = attemptId,
            ExamSectionId = sectionId,
            PreviousScore = previousScore,
            NewScore = request.Score,
            PreviousFeedback = previousFeedback,
            NewFeedback = feedback,
            ChangedByUserId = TeacherId!.Value,
            ChangedAt = DateTime.UtcNow
        });

        var otherGrades = await academic.ExamSectionManualGrades.AsNoTracking()
            .Where(item => item.AttemptId == attemptId && item.ExamSectionId != sectionId)
            .Select(item => item.Score).ToListAsync(ct);
        var manualTotal = otherGrades.Sum() + request.Score;

        var essaySectionIds = await academic.ExamSections.AsNoTracking()
            .Where(item => item.ExamId == attempt.ExamId && item.SectionType == "Essay")
            .Select(item => item.Id).ToListAsync(ct);
        var alreadyGradedIds = await academic.ExamSectionManualGrades.AsNoTracking()
            .Where(item => item.AttemptId == attemptId && item.ExamSectionId != sectionId)
            .Select(item => item.ExamSectionId).ToListAsync(ct);
        var allSectionsGraded = essaySectionIds.All(id =>
            id == sectionId || alreadyGradedIds.Contains(id));

        attempt.ManualGradedScore = decimal.Round(manualTotal, 2, MidpointRounding.AwayFromZero);
        if (allSectionsGraded)
        {
            var finalStatus = await academic.ExamAttemptStatuses.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == 4 && item.Code == "FullyGraded", ct);
            if (finalStatus is null)
                return Conflict(new { message = "وضعیت FullyGraded (شناسه ۴) در ExamAttemptStatuses تعریف نشده است؛ نمره ذخیره نشد." });

            attempt.FinalScore = decimal.Round(
                Math.Max(0m, (attempt.AutoGradedScore ?? 0m) + manualTotal),
                2, MidpointRounding.AwayFromZero);
            attempt.GradingStatus = "Final";
            attempt.StatusId = finalStatus.Id;
        }
        else
        {
            attempt.FinalScore = null;
            attempt.GradingStatus = "PendingManual";
        }

        try
        {
            await using var transaction = await academic.Database.BeginTransactionAsync(ct);
            await academic.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "این پاسخنامه هم‌زمان توسط استاد دیگری تغییر کرده است؛ صف را تازه‌سازی کنید." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "ثبت نمره هم‌زمان انجام شده است؛ صف را تازه‌سازی و دوباره بررسی کنید." });
        }

        return Ok(new
        {
            attemptId,
            sectionId,
            score = request.Score,
            maxScore,
            manualGradedScore = attempt.ManualGradedScore,
            finalScore = attempt.FinalScore,
            gradingStatus = attempt.GradingStatus,
            rowVersion = Convert.ToBase64String(grade.RowVersion)
        });
    }

    private async Task<(CanonicalExamAttempt Attempt, CanonicalExamSection Section)?> LoadAuthorizedSection(
        long attemptId, long sectionId, CancellationToken ct)
    {
        if (TeacherId is null) return null;

        var attempt = await academic.ExamAttempts
            .FirstOrDefaultAsync(item => item.Id == attemptId, ct);
        if (attempt is null) return null;

        var section = await academic.ExamSections.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == sectionId &&
                item.ExamId == attempt.ExamId &&
                item.SectionType == "Essay", ct);
        if (section is null) return null;

        var exam = await academic.Exams.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == attempt.ExamId, ct);
        if (exam is null || !await CanAccessExam(exam, ct)) return null;

        // Null is reserved for historical rows created before this workflow.
        // Those attempts are intentionally excluded from the new grading queue.
        if (!attempt.ManualReviewRequired.HasValue) return null;

        return (attempt, section);
    }
}

public sealed class CanonicalEssayGradeRequest
{
    public decimal Score { get; set; }
    public string? Feedback { get; set; }
    public string? RowVersion { get; set; }
}
