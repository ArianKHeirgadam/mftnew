using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/results")]
[Authorize]
public sealed class CanonicalResultsController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? CurrentUserId =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId =>
        long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long? examId, [FromQuery] long? studentId, CancellationToken ct)
    {
        var isStudent = User.IsInRole("Student");
        if (isStudent && CurrentUserId is null) return Unauthorized();

        var query = from attempt in academic.ExamAttempts.AsNoTracking()
                    join exam in academic.Exams.AsNoTracking() on attempt.ExamId equals exam.Id
                    select new { Attempt = attempt, Exam = exam };

        if (isStudent)
            query = query.Where(x => x.Attempt.StudentUserId == CurrentUserId &&
                ((academic.ExamSections.Any(section => section.ExamId == x.Exam.Id) &&
                  (x.Attempt.StatusId == 2 || x.Attempt.StatusId == 3)) ||
                 (x.Exam.ResultsPublished && x.Attempt.StatusId == 4)));
        else if (User.IsInRole("Teacher"))
            query = query.Where(x => x.Exam.CreatedByUserId == CurrentUserId ||
                (x.Exam.ClassId.HasValue && academic.ClassTeachers.Any(t =>
                    t.ClassId == x.Exam.ClassId.Value && t.TeacherUserId == CurrentUserId)));
        else if (User.IsInRole("Department"))
            query = query.Where(x => x.Exam.DepartmentId == CurrentDepartmentId);
        else if (!User.IsInRole("SuperAdmin")) return Forbid();

        if (examId.HasValue) query = query.Where(x => x.Exam.Id == examId.Value);
        if (studentId.HasValue && !isStudent) query = query.Where(x => x.Attempt.StudentUserId == studentId.Value);

        var rows = await query.OrderByDescending(x => x.Attempt.SubmittedAt)
            .Select(x => new {
                AttemptId = x.Attempt.Id, ExamId = x.Exam.Id, x.Exam.Title,
                x.Attempt.StudentUserId, x.Attempt.StartedAt, x.Attempt.SubmittedAt,
                x.Attempt.FinalScore, x.Attempt.AutoGradedScore, x.Attempt.StatusId,
                x.Exam.ResultsPublished,
                HasAutoGradedAnswers = academic.StudentAnswers.Any(answer => answer.ExamAttemptId == x.Attempt.Id && answer.IsAutoGraded)
            }).ToListAsync(ct);
        var userIds = rows.Select(x => x.StudentUserId).Distinct().ToArray();
        var names = await identity.Users.AsNoTracking().Where(x => userIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);

        return Ok(rows.Select(x => new {
            id = x.AttemptId, examId = x.ExamId, examTitle = x.Title,
            studentId = x.StudentUserId,
            studentName = names.GetValueOrDefault(x.StudentUserId),
            startedAt = x.StartedAt, submittedAt = x.SubmittedAt,
            score = isStudent
                ? (x.StatusId == 4 ? (x.ResultsPublished ? x.FinalScore : null) : x.AutoGradedScore)
                : x.FinalScore ?? x.AutoGradedScore,
            status = StatusCodeName(x.StatusId),
            resultsPublished = x.ResultsPublished
        }));
    }

    [HttpGet("{attemptId:long}")]
    public async Task<IActionResult> Get(long attemptId, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
        if (exam is null) return NotFound();
        var isStudent = User.IsInRole("Student");
        if (isStudent && (attempt.StudentUserId != CurrentUserId ||
            !((await academic.ExamSections.AnyAsync(x => x.ExamId == exam.Id, ct) && (attempt.StatusId == 2 || attempt.StatusId == 3)) ||
              (exam.ResultsPublished && attempt.StatusId == 4))))
            return NotFound();
        if (!isStudent && !await CanManage(exam, ct)) return Forbid();

        var answers = await (from answer in academic.StudentAnswers.AsNoTracking()
            join examQuestion in academic.ExamQuestions.AsNoTracking() on answer.ExamQuestionId equals examQuestion.Id
            join question in academic.Questions.AsNoTracking() on examQuestion.QuestionId equals question.Id
            where answer.ExamAttemptId == attemptId
            orderby examQuestion.DisplayOrder
            select new {
                answer.Id, answer.ExamQuestionId, QuestionId = question.Id,
                question.Text, question.QuestionTypeId, answer.AnswerJson,
                answer.IsAutoGraded, answer.Score, answer.GradedByUserId, answer.GradedAt,
                examQuestion.PointsOverride, question.Points
            }).ToListAsync(ct);
        var questionTypeIds = answers.Select(x => x.QuestionTypeId).Distinct().ToArray();
        var typeCodes = await academic.QuestionTypes.AsNoTracking().Where(x => questionTypeIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var questionIds = answers.Select(x => x.QuestionId).Distinct().ToArray();
        var options = await academic.MultipleChoiceOptions.AsNoTracking().Where(x => questionIds.Contains(x.QuestionId))
            .OrderBy(x => x.DisplayOrder).ToListAsync(ct);
        var matching = await academic.MatchingPairs.AsNoTracking().Where(x => questionIds.Contains(x.QuestionId))
            .OrderBy(x => x.DisplayOrder).ToListAsync(ct);
        var isPdfOnly = answers.Count == 0 && !await academic.ExamSections.AnyAsync(x => x.ExamId == exam.Id, ct) &&
            await academic.ExamDocuments.AnyAsync(x => x.ExamId == exam.Id, ct);
        var submissionDocument = isPdfOnly
            ? await academic.ExamSubmissionDocuments.AsNoTracking().Where(x => x.AttemptId == attemptId)
                .Select(x => new { x.OriginalFileName, x.FileSize, x.UploadedAt }).FirstOrDefaultAsync(ct)
            : null;
        var maxScore = isPdfOnly ? 100m : answers.Sum(x => x.PointsOverride ?? x.Points);
        var payload = answers.Select(x => {
            var code = typeCodes.GetValueOrDefault(x.QuestionTypeId) ?? "Unknown";
            var reveal = !isStudent || exam.ResultsPublished;
            return new {
                questionId = x.QuestionId, examQuestionId = x.ExamQuestionId, question = x.Text,
                questionType = code,
                answer = ParseJson(x.AnswerJson),
                correctAnswer = reveal && code == "MultipleChoice"
                    ? options.Where(o => o.QuestionId == x.QuestionId && o.IsCorrect).Select(o => new { o.Id, o.OptionText }).ToList()
                    : null,
                matchingKey = reveal && code == "Matching"
                    ? matching.Where(p => p.QuestionId == x.QuestionId).Select(p => new { p.LeftItem, p.RightItem }).ToList()
                    : null,
                awardedScore = reveal || (isStudent && x.IsAutoGraded) ? x.Score : null,
                maxScore = x.PointsOverride ?? x.Points,
                isAutoGraded = x.IsAutoGraded,
                isManuallyGraded = !x.IsAutoGraded && x.Score.HasValue,
                gradedAt = x.GradedAt
            };
        }).ToList();
        var hasAutoGradedAnswers = answers.Any(x => x.IsAutoGraded);
        var pending = isPdfOnly ? (attempt.StatusId == 4 ? 0 : 1) : answers.Count(x => !x.IsAutoGraded && !x.Score.HasValue);
        return Ok(new {
            id = attempt.Id, examId = exam.Id, exam = exam.Title, isPdfOnly,
            submissionPdf = submissionDocument,
            score = isPdfOnly
                ? (isStudent && !exam.ResultsPublished ? null : attempt.FinalScore)
                : (isStudent && (attempt.StatusId == 2 || attempt.StatusId == 3)
                    ? (hasAutoGradedAnswers ? attempt.AutoGradedScore : null)
                    : (exam.ResultsPublished ? attempt.FinalScore : null)),
            autoScore = isStudent && (attempt.StatusId == 2 || attempt.StatusId == 3)
                ? (hasAutoGradedAnswers ? attempt.AutoGradedScore : null)
                : (isStudent && !exam.ResultsPublished ? null : attempt.AutoGradedScore),
            maxScore, pendingManualCount = pending,
            status = exam.ResultsPublished ? StatusCodeName(attempt.StatusId) : "AwaitingPublication",
            resultsPublished = exam.ResultsPublished, answers = payload
        });
    }

    [HttpPut("{attemptId:long}/answers/{examQuestionId:long}/grade")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> GradeAnswer(long attemptId, long examQuestionId,
        [FromBody] CanonicalManualGradeRequest request, CancellationToken ct)
    {
        if (request.Score < 0) return BadRequest(new { message = "نمره سؤال نمی‌تواند منفی باشد." });
        var attempt = await academic.ExamAttempts.FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        if (exam.ResultsPublished) return Conflict(new { message = "برای تغییر نمره، ابتدا انتشار نتایج را لغو کنید." });
        if (attempt.StatusId == 1) return BadRequest(new { message = "آزمون هنوز ارسال نشده است." });

        var answer = await academic.StudentAnswers.FirstOrDefaultAsync(
            x => x.ExamAttemptId == attemptId && x.ExamQuestionId == examQuestionId, ct);
        var examQuestion = await academic.ExamQuestions.FirstOrDefaultAsync(x => x.Id == examQuestionId, ct);
        if (answer is null || examQuestion is null) return NotFound();
        var belongsToExam = await academic.ExamSections.AnyAsync(
            x => x.Id == examQuestion.ExamSectionId && x.ExamId == exam.Id, ct);
        if (!belongsToExam) return NotFound();
        var question = await academic.Questions.FirstOrDefaultAsync(x => x.Id == examQuestion.QuestionId, ct);
        if (question is null) return NotFound();
        var typeCode = await academic.QuestionTypes.Where(x => x.Id == question.QuestionTypeId)
            .Select(x => x.Code).FirstOrDefaultAsync(ct);
        if (typeCode != "Essay") return BadRequest(new { message = "فقط سؤال تشریحی از این مسیر قابل تصحیح دستی است." });
        var maxPoints = examQuestion.PointsOverride ?? question.Points;
        if (request.Score > maxPoints)
            return BadRequest(new { message = $"حداکثر نمره این سؤال {maxPoints} است." });

        answer.Score = request.Score;
        answer.IsAutoGraded = false;
        answer.GradedByUserId = CurrentUserId;
        answer.GradedAt = DateTime.UtcNow;
        var allAnswers = await academic.StudentAnswers.Where(x => x.ExamAttemptId == attemptId).ToListAsync(ct);
        var pending = allAnswers.Any(x => !x.IsAutoGraded && !x.Score.HasValue);
        var total = allAnswers.Sum(x => x.Score ?? 0m);
        var partial = await academic.ExamAttemptStatuses.SingleAsync(x => x.Id == 3 && x.Code == "PartiallyGraded", ct);
        var fully = await academic.ExamAttemptStatuses.SingleAsync(x => x.Id == 4 && x.Code == "FullyGraded", ct);
        attempt.AutoGradedScore = allAnswers.Where(x => x.IsAutoGraded).Sum(x => x.Score ?? 0m);
        attempt.StatusId = pending ? partial.Id : fully.Id;
        attempt.FinalScore = pending ? null : total;
        await academic.SaveChangesAsync(ct);
        return Ok(new { attemptId, examQuestionId, awardedScore = answer.Score,
            attempt.AutoGradedScore, attempt.FinalScore, pendingManualCount = allAnswers.Count(x => !x.IsAutoGraded && !x.Score.HasValue),
            status = StatusCodeName(attempt.StatusId) });
    }

    [HttpPut("{attemptId:long}/pdf-grade")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> GradePdfAttempt(long attemptId, [FromBody] CanonicalManualGradeRequest request, CancellationToken ct)
    {
        if (request.Score < 0 || request.Score > 100) return BadRequest(new { message = "نمره آزمون PDFمحور باید بین ۰ تا ۱۰۰ باشد." });
        var attempt = await academic.ExamAttempts.FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        if (exam.ResultsPublished) return Conflict(new { message = "برای تغییر نمره، ابتدا انتشار نتایج را لغو کنید." });
        if (attempt.StatusId == 1) return BadRequest(new { message = "دانشجو هنوز آزمون را ثبت نهایی نکرده است." });
        var isPdfOnly = !await academic.ExamSections.AnyAsync(x => x.ExamId == exam.Id, ct) &&
            await academic.ExamDocuments.AnyAsync(x => x.ExamId == exam.Id, ct);
        if (!isPdfOnly) return BadRequest(new { message = "این تلاش متعلق به آزمون PDFمحور نیست." });
        if (!await academic.ExamSubmissionDocuments.AnyAsync(x => x.AttemptId == attemptId, ct))
            return BadRequest(new { message = "فایل پاسخ دانشجو ثبت نشده است." });
        var fully = await academic.ExamAttemptStatuses.SingleAsync(x => x.Id == 4 && x.Code == "FullyGraded", ct);
        attempt.AutoGradedScore = 0m;
        attempt.FinalScore = request.Score;
        attempt.StatusId = fully.Id;
        await academic.SaveChangesAsync(ct);
        return Ok(new { attemptId, awardedScore = attempt.FinalScore, maxScore = 100m, status = StatusCodeName(attempt.StatusId) });
    }

    [HttpPost("exams/{examId:long}/publish")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Publish(long examId, CancellationToken ct)
    {
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        var attempts = await academic.ExamAttempts.Where(x => x.ExamId == examId).ToListAsync(ct);
        var isPdfOnly = !await academic.ExamSections.AnyAsync(x => x.ExamId == examId, ct) &&
            await academic.ExamDocuments.AnyAsync(x => x.ExamId == examId, ct);
        if (isPdfOnly && attempts.Any(x => x.StatusId != 4))
            return BadRequest(new { message = "برای انتشار نتایج آزمون PDFمحور، باید فایل تمام دانشجویان تصحیح و نمره‌گذاری شده باشد." });
        if (attempts.Any(x => x.StatusId == 1))
            return BadRequest(new { message = "هنوز دانشجویی در حال برگزاری آزمون است." });
        var attemptIds = attempts.Select(x => x.Id).ToArray();
        var answers = await academic.StudentAnswers.Where(x => attemptIds.Contains(x.ExamAttemptId)).ToListAsync(ct);
        if (answers.Any(x => !x.IsAutoGraded && !x.Score.HasValue))
            return BadRequest(new { message = "هنوز پاسخ تشریحی تصحیح‌نشده وجود دارد." });
        var fully = await academic.ExamAttemptStatuses.SingleAsync(x => x.Id == 4 && x.Code == "FullyGraded", ct);
        if (!isPdfOnly)
        {
            foreach (var attempt in attempts)
            {
                var attemptAnswers = answers.Where(x => x.ExamAttemptId == attempt.Id).ToList();
                attempt.AutoGradedScore = attemptAnswers.Where(x => x.IsAutoGraded).Sum(x => x.Score ?? 0m);
                attempt.FinalScore = attemptAnswers.Sum(x => x.Score ?? 0m);
                attempt.StatusId = fully.Id;
            }
        }
        exam.ResultsPublished = true;
        exam.IsPublished = false;
        exam.ResultsPublishedAt = DateTime.UtcNow;
        exam.ResultsPublishedByUserId = CurrentUserId;
        await academic.SaveChangesAsync(ct);
        return Ok(new { examId, resultsPublished = true, publishedAt = exam.ResultsPublishedAt });
    }

    [HttpPost("exams/{examId:long}/unpublish")]
    [Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Unpublish(long examId, CancellationToken ct)
    {
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!await CanManage(exam, ct)) return Forbid();
        exam.ResultsPublished = false;
        exam.ResultsPublishedAt = null;
        exam.ResultsPublishedByUserId = null;
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> CanManage(CanonicalExam exam, CancellationToken ct) =>
        User.IsInRole("SuperAdmin") ||
        User.IsInRole("Department") && exam.DepartmentId == CurrentDepartmentId ||
        User.IsInRole("Teacher") && (exam.CreatedByUserId == CurrentUserId ||
            (exam.ClassId.HasValue && await academic.ClassTeachers.AnyAsync(
                x => x.ClassId == exam.ClassId.Value && x.TeacherUserId == CurrentUserId, ct)));

    private static string StatusCodeName(byte id) => id switch {
        1 => "InProgress", 2 => "Submitted", 3 => "PartiallyGraded", 4 => "FullyGraded", _ => "Unknown"
    };

    private static JsonElement? ParseJson(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone(); }
        catch (JsonException) { return null; }
    }
}

public sealed class CanonicalManualGradeRequest
{
    public decimal Score { get; set; }
}
