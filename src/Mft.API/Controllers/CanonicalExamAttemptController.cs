using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/exams/{examId:long}/attempt")]
[Authorize(Roles = "Student")]
public sealed class CanonicalExamAttemptController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? StudentId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? DepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    [HttpPost("start")]
    public async Task<IActionResult> Start(long examId, CancellationToken ct)
    {
        if (StudentId is null) return Unauthorized();
        var now = DateTime.UtcNow;
        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!exam.IsPublished || exam.DepartmentId != DepartmentId || !exam.ClassId.HasValue)
            return Forbid();
        if (!await academic.ClassMembers.AnyAsync(x =>
            x.ClassId == exam.ClassId.Value && x.StudentUserId == StudentId.Value, ct))
            return Forbid();
        if (now < exam.StartAt || now > exam.EndAt)
            return Conflict(new { message = "آزمون در حال حاضر در بازه مجاز برگزاری نیست." });

        var existing = await academic.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ExamId == examId && x.StudentUserId == StudentId, ct);
        if (existing is not null)
            return Conflict(new { message = "برای این آزمون قبلاً تلاش ثبت شده است و امکان شرکت مجدد وجود ندارد.", attemptId = existing.Id, statusId = existing.StatusId });

        if (!await identity.Users.AnyAsync(x => x.Id == StudentId && x.IsActive && x.DepartmentId == exam.DepartmentId, ct))
            return Forbid();

        var inProgress = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 1 && x.Code == "InProgress", ct);
        if (inProgress is null) return StatusCode(500, new { message = "وضعیت InProgress در ExamAttemptStatuses تعریف نشده است." });

        var attempt = new CanonicalExamAttempt {
            ExamId = examId, StudentUserId = StudentId.Value, StartedAt = now, StatusId = inProgress.Id
        };
        academic.ExamAttempts.Add(attempt);
        try { await academic.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The unique (ExamId, StudentUserId) index is the final concurrency-safe one-attempt guarantee.
            academic.Entry(attempt).State = EntityState.Detached;
            var prior = await academic.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId && x.StudentUserId == StudentId, ct);
            if (prior is not null)
                return Conflict(new { message = "برای این آزمون قبلاً تلاش ثبت شده است و امکان شرکت مجدد وجود ندارد.", attemptId = prior.Id, statusId = prior.StatusId });
            throw;
        }
        return CreatedAtAction(nameof(Get), new { examId, attemptId = attempt.Id }, new { attemptId = attempt.Id, attempt.StartedAt, expiresAt = attempt.StartedAt.AddMinutes(exam.DurationMinutes) });
    }

    [HttpGet("{attemptId:long}")]
    public async Task<IActionResult> Get(long examId, long attemptId, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.ExamId == examId && x.StudentUserId == StudentId, ct);
        if (attempt is null) return NotFound();
        var exam = await academic.Exams.AsNoTracking().FirstAsync(x => x.Id == examId, ct);
        var deadline = DateTime.SpecifyKind(attempt.StartedAt.AddMinutes(exam.DurationMinutes), DateTimeKind.Utc);
        if (attempt.StatusId != 1)
            return Conflict(new { message = "این تلاش قبلاً ثبت نهایی شده و دیگر قابل ادامه نیست.", attempt.StatusId, attempt.SubmittedAt });
        if (DateTime.UtcNow >= deadline)
            return Conflict(new { message = "زمان مجاز آزمون به پایان رسیده است؛ پاسخ‌ها باید ثبت نهایی شوند.", deadline });

        var sections = await academic.ExamSections.AsNoTracking().Where(x => x.ExamId == examId)
            .OrderBy(x => x.DisplayOrder).Select(s => new {
                s.Id, s.SkillId, s.DurationMinutes, s.DisplayOrder, s.WeightPercent,
                Questions = academic.ExamQuestions.Where(eq => eq.ExamSectionId == s.Id).OrderBy(eq => eq.DisplayOrder)
                    .Select(eq => new {
                        ExamQuestionId = eq.Id, eq.DisplayOrder,
                        QuestionId = eq.QuestionId,
                        Text = academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => q.Text).FirstOrDefault(),
                        Points = academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => eq.PointsOverride ?? q.Points).FirstOrDefault(),
                        QuestionType = academic.QuestionTypes.Where(t => t.Id == academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => q.QuestionTypeId).FirstOrDefault()).Select(t => t.Code).FirstOrDefault(),
                        Options = academic.MultipleChoiceOptions.Where(o => o.QuestionId == eq.QuestionId).OrderBy(o => o.DisplayOrder).Select(o => new { o.Id, o.OptionText, o.DisplayOrder }).ToList(),
                        MatchingLeftItems = academic.MatchingPairs.Where(p => p.QuestionId == eq.QuestionId).OrderBy(p => p.DisplayOrder).Select(p => p.LeftItem).ToList(),
                        MatchingRightItems = academic.MatchingPairs.Where(p => p.QuestionId == eq.QuestionId).OrderBy(p => p.DisplayOrder).Select(p => p.RightItem).ToList()
                    }).ToList()
            }).ToListAsync(ct);
        var saved = await academic.StudentAnswers.AsNoTracking().Where(x => x.ExamAttemptId == attemptId)
            .ToDictionaryAsync(x => x.ExamQuestionId, x => x.AnswerJson, ct);
        var examPdf = await academic.ExamDocuments.AsNoTracking().Where(x => x.ExamId == examId)
            .Select(x => new { x.OriginalFileName, x.FileSize }).FirstOrDefaultAsync(ct);
        var isPdfOnly = sections.Count == 0 && examPdf is not null;
        return Ok(new { attempt.Id, StartedAtUtc = DateTime.SpecifyKind(attempt.StartedAt, DateTimeKind.Utc), Deadline = deadline,
            ServerNowUtc = DateTime.UtcNow, IsPdfOnly = isPdfOnly, ExamPdfAvailable = examPdf is not null,
            ExamPdfFileName = examPdf?.OriginalFileName, Sections = sections, SavedAnswers = saved });
    }

    [HttpPut("{attemptId:long}/answers")]
    public async Task<IActionResult> SaveAnswers(long examId, long attemptId, SaveAttemptAnswersRequest request, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.FirstOrDefaultAsync(x =>
            x.Id == attemptId && x.ExamId == examId && x.StudentUserId == StudentId, ct);
        if (attempt is null) return NotFound();
        if (attempt.StatusId != 1)
            return Conflict(new { message = "این تلاش ثبت نهایی شده و پاسخ‌های آن قابل تغییر نیست." });
        var exam = await academic.Exams.AsNoTracking().FirstAsync(x => x.Id == examId, ct);
        var deadline = DateTime.SpecifyKind(attempt.StartedAt.AddMinutes(exam.DurationMinutes), DateTimeKind.Utc);
        if (DateTime.UtcNow >= deadline)
            return Conflict(new { message = "مهلت آزمون به پایان رسیده است." });
        var validIds = await academic.ExamQuestions.AsNoTracking()
            .Where(x => academic.ExamSections.Any(s => s.Id == x.ExamSectionId && s.ExamId == examId))
            .Select(x => x.Id).ToListAsync(ct);
        if (request.Answers.Select(x => x.ExamQuestionId).Distinct().Count() != request.Answers.Count ||
            request.Answers.Any(x => !validIds.Contains(x.ExamQuestionId)))
            return BadRequest(new { message = "پاسخ تکراری یا متعلق به سؤال دیگری ارسال شده است." });

        var existing = await academic.StudentAnswers
            .Where(x => x.ExamAttemptId == attemptId).ToDictionaryAsync(x => x.ExamQuestionId, ct);
        foreach (var answer in request.Answers)
        {
            var json = answer.Value.GetRawText();
            if (existing.TryGetValue(answer.ExamQuestionId, out var saved))
                saved.AnswerJson = json;
            else
                academic.StudentAnswers.Add(new CanonicalStudentAnswer {
                    ExamAttemptId = attemptId, ExamQuestionId = answer.ExamQuestionId, AnswerJson = json
                });
        }
        await academic.SaveChangesAsync(ct);
        return Ok(new { savedCount = request.Answers.Count, savedAt = DateTime.UtcNow });
    }

    [HttpPost("{attemptId:long}/submit")]
    public async Task<IActionResult> Submit(long examId, long attemptId, CanonicalSubmitAttemptRequest request, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.FirstOrDefaultAsync(x => x.Id == attemptId && x.ExamId == examId && x.StudentUserId == StudentId, ct);
        if (attempt is null) return NotFound();
        if (attempt.StatusId != 1)
            return Conflict(new { message = "این آزمون قبلاً ثبت نهایی شده است؛ امکان ارسال مجدد وجود ندارد." });
        var exam = await academic.Exams.AsNoTracking().FirstAsync(x => x.Id == examId, ct);
        var deadline = DateTime.SpecifyKind(attempt.StartedAt.AddMinutes(exam.DurationMinutes), DateTimeKind.Utc);
        var serverNow = DateTime.UtcNow;
        if (request.AutoSubmit && serverNow < deadline)
            return Conflict(new { message = "زمان آزمون طبق ساعت سرور هنوز به پایان نرسیده است؛ پاسخ‌ها ثبت نهایی نشدند.", deadline });
        if (serverNow > deadline.AddSeconds(30))
            return Conflict(new { message = "مهلت آزمون به پایان رسیده است." });

        var examQuestions = await academic.ExamQuestions.AsNoTracking()
            .Where(x => academic.ExamSections.Any(s => s.Id == x.ExamSectionId && s.ExamId == examId))
            .Select(x => new {
                x.Id, x.QuestionId, Points = x.PointsOverride ?? academic.Questions.Where(q => q.Id == x.QuestionId).Select(q => q.Points).FirstOrDefault(),
                TypeCode = academic.QuestionTypes.Where(t => t.Id == academic.Questions.Where(q => q.Id == x.QuestionId).Select(q => q.QuestionTypeId).FirstOrDefault()).Select(t => t.Code).FirstOrDefault()
            }).ToListAsync(ct);
        var hasExamPdfForSubmission = await academic.ExamDocuments.AnyAsync(x => x.ExamId == examId, ct);
        var hasSubmissionPdfForExam = await academic.ExamSubmissionDocuments.AnyAsync(x => x.AttemptId == attemptId, ct);
        if (examQuestions.Count > 0 && hasExamPdfForSubmission && !hasSubmissionPdfForExam)
            return BadRequest(new { message = "برای این آزمون، فایل PDF پاسخ‌نامه الزامی است؛ ابتدا آن را بارگذاری کنید." });
        if (examQuestions.Count == 0)
        {
            if (!hasExamPdfForSubmission) return Conflict(new { message = "فایل PDF صورت آزمون موجود نیست." });
            if (!hasSubmissionPdfForExam) return BadRequest(new { message = "برای ثبت نهایی، ابتدا فایل PDF پاسخ خود را بارگذاری کنید." });
            var submittedStatus = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 2 && x.Code == "Submitted", ct);
            if (submittedStatus is null) return StatusCode(500, new { message = "وضعیت Submitted در جدول مرجع تعریف نشده است." });
            attempt.SubmittedAt = DateTime.UtcNow;
            attempt.StatusId = submittedStatus.Id;
            attempt.AutoGradedScore = null;
            attempt.FinalScore = null;
            await academic.SaveChangesAsync(ct);
            return Ok(new { attempt.Id, attempt.StatusId, attempt.SubmittedAt, attempt.AutoGradedScore, attempt.FinalScore, RequiresManualGrading = true, IsPdfOnly = true });
        }
        if (request.Answers.Select(x => x.ExamQuestionId).Distinct().Count() != request.Answers.Count ||
            request.Answers.Any(a => examQuestions.All(q => q.Id != a.ExamQuestionId)))
            return BadRequest(new { message = "پاسخ تکراری یا متعلق به سؤال دیگری ارسال شده است." });

        var status = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 2 && x.Code == "Submitted", ct);
        var partial = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 3 && x.Code == "PartiallyGraded", ct);
        if (status is null || partial is null)
            return StatusCode(500, new { message = "وضعیت‌های Submitted و PartiallyGraded باید در جدول مرجع تعریف شده باشند." });

        decimal autoScore = 0;
        var pendingManual = false;
        foreach (var q in examQuestions)
        {
            var answer = request.Answers.SingleOrDefault(x => x.ExamQuestionId == q.Id);
            if (answer is null) return BadRequest(new { message = "برای تمام سؤال‌های آزمون باید پاسخ ارسال شود." });
            var json = answer.Value.GetRawText();
            decimal? score = null;
            var auto = false;
            if (string.Equals(q.TypeCode, "MultipleChoice", StringComparison.OrdinalIgnoreCase))
            {
                long optionId = 0;
                var hasSelection = answer.Value.TryGetProperty("selectedOptionId", out var optionNode) &&
                    optionNode.ValueKind == JsonValueKind.Number && optionNode.TryGetInt64(out optionId);
                if (hasSelection)
                {
                    var option = await academic.MultipleChoiceOptions.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == optionId && x.QuestionId == q.QuestionId, ct);
                    if (option is null) return BadRequest(new { message = "گزینه انتخاب‌شده متعلق به این سؤال نیست." });
                    var correct = await academic.MultipleChoiceOptions.AsNoTracking()
                        .AnyAsync(x => x.QuestionId == q.QuestionId && x.IsCorrect && x.Id == optionId, ct);
                    score = correct ? q.Points : 0m;
                }
                else score = 0m;
                auto = true;
            }
            else if (string.Equals(q.TypeCode, "Matching", StringComparison.OrdinalIgnoreCase))
            {
                var expected = await academic.MatchingPairs.AsNoTracking().Where(x => x.QuestionId == q.QuestionId)
                    .Select(x => new { x.LeftItem, x.RightItem }).ToListAsync(ct);
                var submitted = new List<(string LeftItem, string RightItem)>();
                if (answer.Value.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
                    submitted = pairs.EnumerateArray().Select(x => (
                        x.TryGetProperty("leftItem", out var left) ? left.GetString() ?? "" : "",
                        x.TryGetProperty("rightItem", out var right) ? right.GetString() ?? "" : ""
                    )).ToList();
                if (submitted.Select(x => x.LeftItem).Distinct().Count() != submitted.Count ||
                    submitted.Any(x => !expected.Any(e => e.LeftItem == x.LeftItem)))
                    return BadRequest(new { message = "ساختار جفت‌های ارسالی صحیح نیست." });
                var correctCount = expected.Count(e => submitted.Any(s => s.LeftItem == e.LeftItem && s.RightItem == e.RightItem));
                score = expected.Count == 0 ? 0m : Math.Round(q.Points * correctCount / expected.Count, 2);
                auto = true;
            }
            else if (string.Equals(q.TypeCode, "Essay", StringComparison.OrdinalIgnoreCase))
            {
                if (answer.Value.ValueKind != JsonValueKind.Object || !answer.Value.TryGetProperty("text", out var essay) || essay.ValueKind != JsonValueKind.String)
                    return BadRequest(new { message = "متن پاسخ تشریحی معتبر نیست." });
                pendingManual = true;
            }
            else return BadRequest(new { message = "نوع سؤال پشتیبانی نمی‌شود." });

            if (auto) autoScore += score ?? 0;
            if (await academic.StudentAnswers.FirstOrDefaultAsync(x => x.ExamAttemptId == attempt.Id && x.ExamQuestionId == q.Id, ct) is { } existingAnswer)
            {
                existingAnswer.AnswerJson = json;
                existingAnswer.IsAutoGraded = auto;
                existingAnswer.Score = score;
            }
            else
            {
                academic.StudentAnswers.Add(new CanonicalStudentAnswer {
                    ExamAttemptId = attempt.Id, ExamQuestionId = q.Id, AnswerJson = json,
                    IsAutoGraded = auto, Score = score
                });
            }
        }
        attempt.AutoGradedScore = autoScore;
        attempt.SubmittedAt = DateTime.UtcNow;
        attempt.StatusId = pendingManual ? partial.Id : status.Id;
        if (!pendingManual) attempt.FinalScore = autoScore;
        await academic.SaveChangesAsync(ct);
        return Ok(new { attempt.Id, attempt.StatusId, attempt.SubmittedAt, attempt.AutoGradedScore, attempt.FinalScore, RequiresManualGrading = pendingManual });
    }
}

public sealed class CanonicalSubmitAttemptRequest
{
    public List<CanonicalSubmittedAnswer> Answers { get; set; } = new();
    public bool AutoSubmit { get; set; }
}
public sealed class CanonicalSubmittedAnswer
{
    public long ExamQuestionId { get; set; }
    public JsonElement Value { get; set; }
}

public sealed class SaveAttemptAnswersRequest
{
    public List<CanonicalSubmittedAnswer> Answers { get; set; } = new();
}
