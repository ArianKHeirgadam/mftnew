using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;
using Mft.Domain.Exams;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/exams/{examId:long}/attempt")]
[Authorize(Roles = "Student")]
public sealed class CanonicalExamAttemptController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private static List<MatchingRightMapItem> CreateMatchingRightMap(IReadOnlyCollection<long> pairIds)
    {
        var result = new List<MatchingRightMapItem>(pairIds.Count);
        var usedTokens = new HashSet<long>();
        foreach (var pairId in pairIds)
        {
            long token;
            do
            {
                var bytes = RandomNumberGenerator.GetBytes(8);
                // The browser currently transports these opaque IDs as JavaScript Numbers.
                // Keep them within Number.MAX_SAFE_INTEGER so JSON round-trips preserve
                // the exact Int64 value used by the scoring map.
                token = BitConverter.ToInt64(bytes) & 0x001FFFFFFFFFFFFF;
            } while (token == 0 || !usedTokens.Add(token));
            result.Add(new MatchingRightMapItem(pairId, token));
        }
        for (var i = result.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (result[i], result[j]) = (result[j], result[i]);
        }
        return result;
    }

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
            ExamId = examId, StudentUserId = StudentId.Value, StartedAt = now,
            StatusId = inProgress.Id, ManualReviewRequired = false
        };
        await using var startTransaction = await academic.Database.BeginTransactionAsync(ct);
        academic.ExamAttempts.Add(attempt);
        try
        {
            await academic.SaveChangesAsync(ct);
            var matchingQuestions = await academic.ExamQuestions.AsNoTracking()
                .Where(eq => academic.ExamSections.Any(s => s.Id == eq.ExamSectionId && s.ExamId == examId) &&
                    academic.Questions.Any(q => q.Id == eq.QuestionId &&
                        academic.QuestionTypes.Any(t => t.Id == q.QuestionTypeId && t.Code == "Matching")))
                .Select(eq => new { eq.Id, eq.DisplayOrder, eq.QuestionId })
                .ToListAsync(ct);
            foreach (var question in matchingQuestions)
            {
                var pairs = await academic.MatchingPairs.AsNoTracking()
                    .Where(p => p.QuestionId == question.QuestionId)
                    .Select(p => p.Id).ToListAsync(ct);
                var shuffledMap = CreateMatchingRightMap(pairs);
                academic.ExamAttemptQuestionOrders.Add(new CanonicalExamAttemptQuestionOrder
                {
                    ExamAttemptId = attempt.Id,
                    ExamQuestionId = question.Id,
                    QuestionOrder = question.DisplayOrder,
                    MatchingRightOrderJson = JsonSerializer.Serialize(shuffledMap)
                });
            }
            await academic.SaveChangesAsync(ct);
            await startTransaction.CommitAsync(ct);
        }
        catch (DbUpdateException)
        {
            await startTransaction.RollbackAsync(ct);
            academic.ChangeTracker.Clear();
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
                s.Id, s.SkillId, s.DurationMinutes, s.DisplayOrder, s.WeightPercent, s.SectionType, s.TotalPoints,
                QuestionPaper = academic.ExamSectionDocuments.Where(d => d.ExamSectionId == s.Id)
                    .Select(d => new { d.OriginalFileName, d.FileSize, d.UploadedAt }).FirstOrDefault(),
                StudentAnswerPdf = academic.ExamSectionSubmissionDocuments
                    .Where(d => d.ExamSectionId == s.Id && d.AttemptId == attemptId)
                    .Select(d => new { d.OriginalFileName, d.FileSize, d.UploadedAt }).FirstOrDefault(),
                Questions = academic.ExamQuestions.Where(eq => eq.ExamSectionId == s.Id).OrderBy(eq => eq.DisplayOrder)
                    .Select(eq => new {
                        ExamQuestionId = eq.Id, eq.DisplayOrder,
                        QuestionId = eq.QuestionId,
                        Text = academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => q.Text).FirstOrDefault(),
                        Points = academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => eq.PointsOverride ?? q.Points).FirstOrDefault(),
                        QuestionType = academic.QuestionTypes.Where(t => t.Id == academic.Questions.Where(q => q.Id == eq.QuestionId).Select(q => q.QuestionTypeId).FirstOrDefault()).Select(t => t.Code).FirstOrDefault(),
                        Options = academic.MultipleChoiceOptions.Where(o => o.QuestionId == eq.QuestionId).OrderBy(o => o.DisplayOrder).Select(o => new { o.Id, o.OptionText, o.DisplayOrder }).ToList(),
                        MatchingPairs = academic.MatchingPairs.Where(p => p.QuestionId == eq.QuestionId)
                            .OrderBy(p => p.DisplayOrder).Select(p => new { p.Id, p.LeftItem, p.RightItem }).ToList()
                    }).ToList()
            }).ToListAsync(ct);
        var orderRows = await academic.ExamAttemptQuestionOrders.AsNoTracking()
            .Where(x => x.ExamAttemptId == attemptId && x.MatchingRightOrderJson != null)
            .ToDictionaryAsync(x => x.ExamQuestionId, x => x.MatchingRightOrderJson!, ct);
        var missingMatchingOrder = sections.SelectMany(s => s.Questions)
            .Where(q => string.Equals(q.QuestionType, "Matching", StringComparison.OrdinalIgnoreCase) &&
                q.MatchingPairs.Count > 0 && !orderRows.ContainsKey(q.ExamQuestionId))
            .Select(q => q.ExamQuestionId).ToArray();
        if (missingMatchingOrder.Length > 0)
            return Conflict(new { message = "اطلاعات ترتیب وصل‌کردنی برای این تلاش قدیمی موجود نیست؛ برای حفظ داده‌های تاریخی، ترتیب جدیدی ساخته نمی‌شود.", examQuestionIds = missingMatchingOrder });

        var sectionsForResponse = sections.Select(section => new
        {
            section.Id, section.SkillId, section.DurationMinutes, section.DisplayOrder, section.WeightPercent,
            section.SectionType, section.TotalPoints, section.QuestionPaper, section.StudentAnswerPdf,
            Questions = section.Questions.Select(question =>
            {
                var rightMap = orderRows.TryGetValue(question.ExamQuestionId, out var json)
                    ? JsonSerializer.Deserialize<List<MatchingRightMapItem>>(json) ?? new List<MatchingRightMapItem>()
                    : new List<MatchingRightMapItem>();
                var pairById = question.MatchingPairs.ToDictionary(p => p.Id);
                return new
                {
                    question.ExamQuestionId, question.DisplayOrder, question.QuestionId, question.Text,
                    question.Points, question.QuestionType, question.Options,
                    MatchingLeftItems = question.MatchingPairs.Select(p => new { Id = p.Id, Text = p.LeftItem }).ToList(),
                    MatchingRightItems = rightMap.Where(m => pairById.ContainsKey(m.PairId))
                        .Select(m => new { Id = m.TokenId, Text = pairById[m.PairId].RightItem }).ToList()
                };
            }).ToList()
        }).ToList();
        var saved = await academic.StudentAnswers.AsNoTracking().Where(x => x.ExamAttemptId == attemptId)
            .ToDictionaryAsync(x => x.ExamQuestionId, x => x.AnswerJson, ct);
        var examPdf = await academic.ExamDocuments.AsNoTracking().Where(x => x.ExamId == examId)
            .Select(x => new { x.OriginalFileName, x.FileSize }).FirstOrDefaultAsync(ct);
        var isPdfOnly = sections.Count == 0 && examPdf is not null;
        return Ok(new { attempt.Id, StartedAtUtc = DateTime.SpecifyKind(attempt.StartedAt, DateTimeKind.Utc), Deadline = deadline,
            ServerNowUtc = DateTime.UtcNow, IsPdfOnly = isPdfOnly, ExamPdfAvailable = examPdf is not null,
            ExamPdfFileName = examPdf?.OriginalFileName, Sections = sectionsForResponse, SavedAnswers = saved });
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
        // Touch the attempt row even when StatusId remains InProgress. RowVersion then
        // serializes autosave against submit and against another concurrent autosave.
        academic.Entry(attempt).Property(x => x.StatusId).IsModified = true;
        try
        {
            await using var saveTransaction = await academic.Database.BeginTransactionAsync(ct);
            await academic.SaveChangesAsync(ct);
            await saveTransaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "این تلاش هم‌زمان در درخواست دیگری تغییر کرده است؛ پاسخ‌ها را دوباره بارگذاری و ذخیره کنید." });
        }
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
        if (!request.AutoSubmit && serverNow > deadline.AddSeconds(30))
            return Conflict(new { message = "مهلت آزمون به پایان رسیده است." });

        var essaySectionIds = await academic.ExamSections.AsNoTracking()
            .Where(s => s.ExamId == examId && s.SectionType == "Essay")
            .Select(s => s.Id).ToListAsync(ct);
        var missingEssayPapers = await academic.ExamSectionDocuments.AsNoTracking()
            .Where(d => essaySectionIds.Contains(d.ExamSectionId)).Select(d => d.ExamSectionId).ToListAsync(ct);
        var missingEssayAnswers = await academic.ExamSectionSubmissionDocuments.AsNoTracking()
            .Where(d => d.AttemptId == attemptId && essaySectionIds.Contains(d.ExamSectionId))
            .Select(d => d.ExamSectionId).ToListAsync(ct);
        var missingQuestionPaperIds = essaySectionIds.Except(missingEssayPapers).ToArray();
        var missingAnswerIds = essaySectionIds.Except(missingEssayAnswers).ToArray();
        if (missingQuestionPaperIds.Length > 0 && !request.AutoSubmit)
            return Conflict(new { message = "فایل PDF صورت سؤال برای یک یا چند بخش تشریحی موجود نیست.", missingSectionIds = missingQuestionPaperIds });
        if (missingAnswerIds.Length > 0 && !request.AutoSubmit)
            return BadRequest(new { message = "برای ثبت نهایی، پاسخ PDF تمام بخش‌های تشریحی را بارگذاری کنید.", missingSectionIds = missingAnswerIds });
        attempt.ManualReviewRequired = missingAnswerIds.Length > 0 || missingQuestionPaperIds.Length > 0;
        // At the deadline, auto-submit locks the attempt even if a PDF was omitted.
        // The manual grading workflow can then flag the missing section response.

        var examQuestions = await academic.ExamQuestions.AsNoTracking()
            .Where(x => academic.ExamSections.Any(s => s.Id == x.ExamSectionId && s.ExamId == examId))
            .Select(x => new {
                x.Id, x.QuestionId, x.ExamSectionId,
                Points = x.PointsOverride ?? academic.Questions.Where(q => q.Id == x.QuestionId).Select(q => q.Points).FirstOrDefault(),
                TypeCode = academic.QuestionTypes.Where(t => t.Id == academic.Questions.Where(q => q.Id == x.QuestionId).Select(q => q.QuestionTypeId).FirstOrDefault()).Select(t => t.Code).FirstOrDefault(),
                NegativeMarkingEnabled = academic.ExamSections.Where(s => s.Id == x.ExamSectionId).Select(s => s.NegativeMarkingEnabled).FirstOrDefault(),
                NegativeMarkingPoints = academic.ExamSections.Where(s => s.Id == x.ExamSectionId).Select(s => s.NegativeMarkingPoints).FirstOrDefault()
            }).ToListAsync(ct);
        var hasExamPdfForSubmission = await academic.ExamDocuments.AnyAsync(x => x.ExamId == examId, ct);
        var hasSubmissionPdfForExam = await academic.ExamSubmissionDocuments.AnyAsync(x => x.AttemptId == attemptId, ct);
        if (examQuestions.Count == 0 && essaySectionIds.Count > 0)
        {
            var submittedStatusForEssay = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 2 && x.Code == "Submitted", ct);
            if (submittedStatusForEssay is null) return StatusCode(500, new { message = "وضعیت Submitted در جدول مرجع تعریف نشده است." });
            attempt.SubmittedAt = DateTime.UtcNow;
            attempt.StatusId = submittedStatusForEssay.Id;
            attempt.AutoGradedScore = 0m;
            attempt.FinalScore = null;
            attempt.GradingStatus = "PendingManual";
            try
            {
                await using var transaction = await academic.Database.BeginTransactionAsync(ct);
                await academic.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "این تلاش هم‌زمان توسط درخواست دیگری ثبت شده است." });
            }
            return Ok(new { attempt.Id, attempt.StatusId, attempt.SubmittedAt, attempt.AutoGradedScore, attempt.FinalScore, RequiresManualGrading = true, IsPdfOnly = false });
        }
        if (examQuestions.Count == 0)
        {
            if (!hasExamPdfForSubmission) return Conflict(new { message = "فایل PDF صورت آزمون موجود نیست." });
            if (!hasSubmissionPdfForExam) return BadRequest(new { message = "برای ثبت نهایی، ابتدا فایل PDF پاسخ خود را بارگذاری کنید." });
            var submittedStatus = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 2 && x.Code == "Submitted", ct);
            if (submittedStatus is null) return StatusCode(500, new { message = "وضعیت Submitted در جدول مرجع تعریف نشده است." });
            attempt.SubmittedAt = DateTime.UtcNow;
            attempt.StatusId = submittedStatus.Id;
            attempt.AutoGradedScore = 0m;
            attempt.ManualGradedScore = null;
            attempt.FinalScore = null;
            attempt.ManualReviewRequired = true;
            attempt.GradingStatus = "PendingManual";
            try
            {
                await using var transaction = await academic.Database.BeginTransactionAsync(ct);
                await academic.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { message = "این تلاش هم‌زمان توسط درخواست دیگری ثبت شده است." });
            }
            return Ok(new { attempt.Id, attempt.StatusId, attempt.SubmittedAt, attempt.AutoGradedScore, attempt.FinalScore, RequiresManualGrading = true, IsPdfOnly = true });
        }
        if (request.Answers.Select(x => x.ExamQuestionId).Distinct().Count() != request.Answers.Count ||
            request.Answers.Any(a => examQuestions.All(q => q.Id != a.ExamQuestionId)))
            return BadRequest(new { message = "پاسخ تکراری یا متعلق به سؤال دیگری ارسال شده است." });

        var status = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 2 && x.Code == "Submitted", ct);
        var partial = await academic.ExamAttemptStatuses.SingleOrDefaultAsync(x => x.Id == 3 && x.Code == "PartiallyGraded", ct);
        if (status is null || partial is null)
            return StatusCode(500, new { message = "وضعیت‌های Submitted و PartiallyGraded باید در جدول مرجع تعریف شده باشند." });

        var questionScoresBySection = new Dictionary<long, List<decimal>>();
        var pendingManual = essaySectionIds.Count > 0;
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
                var hasSelection = answer.Value.ValueKind == JsonValueKind.Object &&
                    answer.Value.TryGetProperty("selectedOptionId", out var optionNode) &&
                    optionNode.ValueKind == JsonValueKind.Number && optionNode.TryGetInt64(out optionId);
                var isCorrect = false;
                if (hasSelection)
                {
                    var option = await academic.MultipleChoiceOptions.AsNoTracking()
                        .FirstOrDefaultAsync(x => x.Id == optionId && x.QuestionId == q.QuestionId, ct);
                    if (option is null) return BadRequest(new { message = "گزینه انتخاب‌شده متعلق به این سؤال نیست." });
                    isCorrect = option.IsCorrect;
                }
                score = ExamScoring.CalculateMultipleChoice(q.Points, hasSelection, isCorrect,
                    q.NegativeMarkingEnabled, q.NegativeMarkingPoints);
                auto = true;
            }
            else if (string.Equals(q.TypeCode, "Matching", StringComparison.OrdinalIgnoreCase))
            {
                var orderJson = await academic.ExamAttemptQuestionOrders.AsNoTracking()
                    .Where(x => x.ExamAttemptId == attemptId && x.ExamQuestionId == q.Id)
                    .Select(x => x.MatchingRightOrderJson).SingleOrDefaultAsync(ct);
                if (string.IsNullOrWhiteSpace(orderJson))
                    return Conflict(new { message = "ترتیب اختصاصی گزینه‌های وصل‌کردنی برای این تلاش موجود نیست؛ این تلاش قدیمی یا ناقص است و برای جلوگیری از تغییر داده‌های تاریخی ثبت نمی‌شود." });
                var rightMap = JsonSerializer.Deserialize<List<MatchingRightMapItem>>(orderJson) ?? new List<MatchingRightMapItem>();
                var correctMap = rightMap.ToDictionary(x => x.PairId, x => x.TokenId);
                var submitted = new List<MatchingPairSelection>();
                if (answer.Value.ValueKind == JsonValueKind.Object &&
                    answer.Value.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pair in pairs.EnumerateArray())
                    {
                        if (pair.ValueKind != JsonValueKind.Object ||
                            !pair.TryGetProperty("leftItemId", out var left) || !left.TryGetInt64(out var leftId) ||
                            !pair.TryGetProperty("rightItemId", out var right) || !right.TryGetInt64(out var rightId))
                            return BadRequest(new { message = "ساختار شناسه‌های جفت وصل‌کردنی معتبر نیست." });
                        submitted.Add(new MatchingPairSelection(leftId, rightId));
                    }
                }
                try { score = ExamScoring.CalculateMatching(q.Points, correctMap, submitted); }
                catch (ArgumentException) { return BadRequest(new { message = "شناسه تکراری یا نامعتبر در پاسخ وصل‌کردنی وجود دارد." }); }
                auto = true;
            }
            else if (string.Equals(q.TypeCode, "Essay", StringComparison.OrdinalIgnoreCase))
            {
                if (answer.Value.ValueKind != JsonValueKind.Object)
                    return BadRequest(new { message = "ساختار پاسخ سؤال تشریحی معتبر نیست." });
                pendingManual = true;
            }
            else return BadRequest(new { message = "نوع سؤال پشتیبانی نمی‌شود." });

            if (auto)
            {
                if (!questionScoresBySection.TryGetValue(q.ExamSectionId, out var sectionScores))
                    questionScoresBySection[q.ExamSectionId] = sectionScores = new List<decimal>();
                sectionScores.Add(score ?? 0m);
            }
            if (await academic.StudentAnswers.FirstOrDefaultAsync(x => x.ExamAttemptId == attempt.Id && x.ExamQuestionId == q.Id, ct) is { } existingAnswer)
            {
                existingAnswer.AnswerJson = json;
                existingAnswer.IsAutoGraded = auto;
                existingAnswer.Score = score;
                existingAnswer.AutoScore = auto ? score : null;
            }
            else
            {
                academic.StudentAnswers.Add(new CanonicalStudentAnswer {
                    ExamAttemptId = attempt.Id, ExamQuestionId = q.Id, AnswerJson = json,
                    IsAutoGraded = auto, Score = score, AutoScore = auto ? score : null
                });
            }
        }
        var autoScore = ExamScoring.CalculateFinalTotal(
            questionScoresBySection.Values.Select(ExamScoring.CalculateSectionTotal));
        attempt.AutoGradedScore = autoScore;
        if (pendingManual) attempt.GradingStatus = "PendingManual";
        attempt.SubmittedAt = DateTime.UtcNow;
        attempt.StatusId = pendingManual ? partial.Id : status.Id;
        if (!pendingManual) attempt.FinalScore = autoScore;
        try
        {
            await using var submitTransaction = await academic.Database.BeginTransactionAsync(ct);
            await academic.SaveChangesAsync(ct);
            await submitTransaction.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "این تلاش قبلاً توسط درخواست دیگری ثبت یا تغییر داده شده است؛ ثبت دوباره انجام نشد." });
        }
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

public sealed record MatchingRightMapItem(long PairId, long TokenId);
