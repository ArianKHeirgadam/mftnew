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
[ApiController, Route("api/exams"), Authorize]
public class ExamsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct){
        var q = db.Exams.Include(x => x.Questions).AsQueryable();
        if (User.IsInRole("Student"))q = q.Where(x => x.IsPublished && (x.DepartmentId == null || x.DepartmentId == User.DepartmentId()));
        if (User.IsInRole("Department"))q = q.Where(x => x.DepartmentId == User.DepartmentId());
        if (User.IsInRole("Teacher"))q = q.Where(x => x.CreatedByUserId == User.UserId());
        // Subject, Description, EndsAt and CreatedAt are not mapped in the supplied database schema.
        // Return explicit nulls rather than asking EF Core to translate ignored properties.
        return Ok(await q.OrderBy(x => x.Title).Select(x => new{
            id = x.Id, title = x.Title, subject = (string?)null, description = (string?)null, durationMinutes = x.DurationMinutes, passingScore = x.PassingScore, allowQuestionNavigation = x.AllowQuestionNavigation, startsAt = x.StartsAt, endsAt = (DateTime?)null, isPublished = x.IsPublished, departmentId = x.DepartmentId, questionCount = x.Questions.Count, createdAt = (DateTime?)null
        }
        ).ToListAsync(ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null || User.IsInRole("Student") && (!e.IsPublished || e.DepartmentId.HasValue && e.DepartmentId != User.DepartmentId())) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        return Ok(new {
            id = e.Id, title = e.Title, subject = e.Subject, description = e.Description, durationMinutes = e.DurationMinutes, passingScore = e.PassingScore, allowQuestionNavigation = e.AllowQuestionNavigation, startsAt = e.StartsAt, endsAt = e.EndsAt, isPublished = e.IsPublished, questionIds = e.Questions.OrderBy(x => x.Order).Select(x => x.QuestionId)
        }
        );
    }
    [HttpPost, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Create(ExamRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Title) || r.DurationMinutes < 1 || r.DurationMinutes>600 || r.PassingScore < 0 || r.PassingScore>100) return BadRequest(new {
            message = "عنوان آزمون، مدت زمان و نمره قبولی معتبر الزامی است."
        }
        );
        if (r.StartsAt.HasValue && r.EndsAt.HasValue && r.EndsAt <= r.StartsAt) return BadRequest(new {
            message = "زمان پایان باید بعد از زمان شروع باشد."
        }
        );
        var ownerId = User.UserId();
        if ((User.IsInRole("SuperAdmin") || User.IsInRole("Department")) && r.CreatedByUserId.HasValue){
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == r.CreatedByUserId.Value && u.Role == UserRole.Teacher, ct);
            if (owner is null) return BadRequest(new {
                message = "استاد انتخاب‌شده معتبر نیست."
            }
            );
            if (User.IsInRole("Department") && owner.DepartmentId != User.DepartmentId()) return Forbid();
            if (r.DepartmentId.HasValue && owner.DepartmentId.HasValue && r.DepartmentId != owner.DepartmentId) return BadRequest(new {
                message = "دپارتمان انتخاب‌شده با دپارتمان استاد مطابقت ندارد."
            }
            );
            ownerId = owner.Id;
        }
        var effectiveDepartmentId = User.IsInRole("Department") || User.IsInRole("Teacher") ? User.DepartmentId() : r.DepartmentId;
        if (!effectiveDepartmentId.HasValue || !await db.Departments.AnyAsync(department => department.Id == effectiveDepartmentId.Value, ct))
            return BadRequest(new { message = "برای ساخت آزمون باید دپارتمان معتبر مشخص باشد." });
        var e = new Exam{
            Title = r.Title, Subject = r.Subject, Description = r.Description, DurationMinutes = r.DurationMinutes, PassingScore = r.PassingScore, AllowQuestionNavigation = r.AllowQuestionNavigation, StartsAt = r.StartsAt ?? DateTime.UtcNow, EndsAt = r.EndsAt, IsPublished = r.IsPublished, DepartmentId = effectiveDepartmentId, CreatedByUserId = ownerId
        }
        ;
        var ids = (r.QuestionIds??Array.Empty<Guid>()).Distinct().ToArray();
        if (ids.Length>0){
            var validQuery = db.Questions.Where(q => ids.Contains(q.Id));
            if (User.IsInRole("Teacher"))validQuery = validQuery.Where(q => q.CreatedByUserId == User.UserId());
            if (User.IsInRole("Department"))validQuery = validQuery.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == User.DepartmentId()));
            var valid = await validQuery.Select(q => q.Id).ToListAsync(ct);
            if (valid.Count != ids.Length) return BadRequest(new {
                message = "یک یا چند سؤال وجود ندارد یا به شما تعلق ندارد."
            }
            );
            var baseScore = Math.Floor(100m/ids.Length*100)/100;
            for (int i = 0;i<ids.Length;i++) e.Questions.Add(new ExamQuestion{
                QuestionId = ids[i], Order = i+1, Score = i == ids.Length-1?100m-baseScore*(ids.Length-1):baseScore
            }
            );
        }
        db.Exams.Add(e);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new {
            id = e.Id
        }
        ,  new{
            id = e.Id
        }
        );
    }
    [HttpPut("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Update(Guid id, ExamRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Title) || r.DurationMinutes < 1 || r.DurationMinutes>600 || r.PassingScore < 0 || r.PassingScore>100) return BadRequest(new {
            message = "عنوان آزمون، مدت زمان و نمره قبولی معتبر الزامی است."
        }
        );
        if (r.StartsAt.HasValue && r.EndsAt.HasValue && r.EndsAt <= r.StartsAt) return BadRequest(new {
            message = "زمان پایان باید بعد از زمان شروع باشد."
        }
        );
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        var ownerId = e.CreatedByUserId;
        if ((User.IsInRole("SuperAdmin") || User.IsInRole("Department")) && r.CreatedByUserId.HasValue){
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == r.CreatedByUserId.Value && u.Role == UserRole.Teacher, ct);
            if (owner is null) return BadRequest(new {
                message = "استاد انتخاب‌شده معتبر نیست."
            }
            );
            if (User.IsInRole("Department") && owner.DepartmentId != User.DepartmentId()) return Forbid();
            if (r.DepartmentId.HasValue && owner.DepartmentId.HasValue && r.DepartmentId != owner.DepartmentId) return BadRequest(new {
                message = "دپارتمان انتخاب‌شده با دپارتمان استاد مطابقت ندارد."
            }
            );
            ownerId = owner.Id;
        }
        e.CreatedByUserId = ownerId;
        e.Title = r.Title;
        e.Subject = r.Subject;
        e.Description = r.Description;
        e.DurationMinutes = r.DurationMinutes;
        e.PassingScore = r.PassingScore;
        e.AllowQuestionNavigation = r.AllowQuestionNavigation;
        e.StartsAt = r.StartsAt;
        e.EndsAt = r.EndsAt;
        e.IsPublished = r.IsPublished;
        e.DepartmentId = User.IsInRole("Department") || User.IsInRole("Teacher")?User.DepartmentId():r.DepartmentId;
        var ids = (r.QuestionIds??Array.Empty<Guid>()).Distinct().ToArray();
        var validQuery = db.Questions.Where(q => ids.Contains(q.Id));
        if (User.IsInRole("Teacher"))validQuery = validQuery.Where(q => q.CreatedByUserId == User.UserId());
        if (User.IsInRole("Department"))validQuery = validQuery.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == User.DepartmentId()));
        var validIds = await validQuery.Select(q => q.Id).ToListAsync(ct);
        if (validIds.Count != ids.Length) return BadRequest(new {
            message = "یک یا چند سؤال وجود ندارد یا به شما تعلق ندارد."
        }
        );
        var existingQuestions = e.Questions.ToDictionary(question => question.QuestionId);
        foreach (var existing in e.Questions.Where(question => !ids.Contains(question.QuestionId)).ToList()){
            e.Questions.Remove(existing);
            db.ExamQuestions.Remove(existing);
        }
        var baseScore = Math.Floor(100m/ids.Length*100)/100;
        for (int i = 0;i<ids.Length;i++){
            var score = i == ids.Length-1?100m-baseScore*(ids.Length-1):baseScore;
            if (existingQuestions.TryGetValue(ids[i], out var existing)){
                existing.Order = i+1;
                existing.Score = score;
            }
            else e.Questions.Add(new ExamQuestion{
                ExamId = id, QuestionId = ids[i], Order = i+1, Score = score
            }
            );
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpDelete("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var e = await db.Exams.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (e is null) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        db.Exams.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPost("{id:guid}/start"), Authorize(Roles ="Student")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct){
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, ct);
        if (e is null) return NotFound();
        if (e.ResultsPublished) return BadRequest(new { message = "نتایج این آزمون منتشر شده و دیگر امکان شروع تلاش جدید وجود ندارد." });
        if (e.DepartmentId.HasValue && e.DepartmentId != User.DepartmentId()) return NotFound();
        var now = DateTime.UtcNow;
        if (e.StartsAt.HasValue && now<e.StartsAt.Value) return BadRequest(new {
            message = "زمان شروع آزمون نرسیده است."
        }
        );
        if (e.EndsAt.HasValue && now>e.EndsAt.Value) return BadRequest(new {
            message = "مهلت شرکت در آزمون پایان یافته است."
        }
        );
        var examPdfAvailable = await db.ExamDocuments.AnyAsync(x => x.ExamId == id, ct);
        if (e.Questions.Count == 0 && !examPdfAvailable)
            return BadRequest(new { message = "برای شروع این آزمون هنوز فایل PDF سؤال بارگذاری نشده است." });
        var student = User.UserId();
        var active = await db.ExamAttempts.FirstOrDefaultAsync(x => x.ExamId == id && x.StudentId == student && x.Status =="InProgress",  ct);
        if (active is null){
            active = new ExamAttempt{
                ExamId = id, StudentId = student
            }
            ;
            db.ExamAttempts.Add(active);
            await db.SaveChangesAsync(ct);
        }
        var questionIds = e.Questions.OrderBy(x => x.Order).Select(x => x.QuestionId).ToArray();
        var questionRows = await db.Questions.Where(q => questionIds.Contains(q.Id)).Select(q => new{
            id = q.Id, text = q.Text, type = q.Type, optionsJson = q.OptionsJson, correctAnswer = q.CorrectAnswer
        }
        ).ToListAsync(ct);
        var qs = questionRows.Select(q =>
        {
            var options = string.IsNullOrWhiteSpace(q.optionsJson) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(q.optionsJson) ?? Array.Empty<string>();
            var matchingOptions = Array.Empty<string>();
            if (q.type == "Matching" && !string.IsNullOrWhiteSpace(q.correctAnswer))
            {
                try { matchingOptions = JsonSerializer.Deserialize<Dictionary<string, string>>(q.correctAnswer)?.Values.OrderBy(_ => Guid.NewGuid()).ToArray() ?? Array.Empty<string>(); }
                catch (JsonException) { matchingOptions = Array.Empty<string>(); }
            }
            return new { id = q.id, text = q.text, type = q.type, options, matchingOptions };
        }).OrderBy(q => Array.IndexOf(questionIds, q.id)).ToList();
        var savedAnswers = await db.ExamAnswers.Where(x => x.ExamAttemptId == active.Id).Select(x => new{
            questionId = x.QuestionId, answer = x.Answer
        }
        ).ToListAsync(ct);
        return Ok(new {
            attemptId = active.Id, startedAt = active.StartedAt, exam = new{
                id = e.Id, title = e.Title, durationMinutes = e.DurationMinutes, allowQuestionNavigation = e.AllowQuestionNavigation,
                examPdfAvailable
            }
            ,  questions = qs, answers = savedAnswers
        }
        );
    }
    [HttpPut("attempts/{attemptId:guid}/answers"), Authorize(Roles ="Student")]
    public async Task<IActionResult> SaveAnswers(Guid attemptId, Mft.Application.Contracts.SubmitAttemptRequest r, CancellationToken ct){
        var a = await db.ExamAttempts.Include(x => x.Exam).ThenInclude(x => x.Questions).FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == User.UserId() && x.Status =="InProgress",  ct);
        if (a is null) return NotFound();
        if (DateTime.UtcNow>a.StartedAt.AddMinutes(a.Exam.DurationMinutes)) return Conflict(new {
            message = "زمان آزمون به پایان رسیده است؛ پاسخ‌ها را نهایی کنید."
        }
        );
        foreach (var answer in r.Answers){
            if (!a.Exam.Questions.Any(x => x.QuestionId == answer.QuestionId))continue;
            var existing = await db.ExamAnswers.FirstOrDefaultAsync(x => x.ExamAttemptId == attemptId && x.QuestionId == answer.QuestionId, ct);
            if (existing is null)db.ExamAnswers.Add(new ExamAnswer{
                ExamAttemptId = attemptId, QuestionId = answer.QuestionId, Answer = answer.Answer
            }
            );
            else{
                existing.Answer = answer.Answer;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPost("attempts/{attemptId:guid}/submit"), Authorize(Roles ="Student")]
    public async Task<IActionResult> Submit(Guid attemptId, Mft.Application.Contracts.SubmitAttemptRequest r, CancellationToken ct)
    {
        var attempt = await db.ExamAttempts
            .Include(x => x.Exam)
            .ThenInclude(x => x.Questions)
            .FirstOrDefaultAsync(
                x => x.Id == attemptId &&
                     x.StudentId == User.UserId() &&
                     x.Status == "InProgress", ct);

        if (attempt is null) return NotFound();

        var essayQuestionIds = await db.ExamQuestions
            .Where(item => item.ExamId == attempt.ExamId)
            .Join(db.Questions, item => item.QuestionId, question => question.Id,
                (item, question) => new { item.QuestionId, question.Type })
            .Where(item => item.Type == "Essay" || item.Type == "Text")
            .Select(item => item.QuestionId)
            .ToListAsync(ct);
        var hasEssayQuestions = essayQuestionIds.Count > 0;
        if (hasEssayQuestions && !await db.ExamSubmissionDocuments.AnyAsync(x => x.AttemptId == attemptId, ct))
            return BadRequest(new { message = "این آزمون سؤال تشریحی دارد؛ ابتدا پاسخ‌نامه PDF یا تصویر آن را بارگذاری کنید." });

        if (attempt.Exam.Questions.Count == 0)
        {
            var answerDocumentExists = await db.ExamSubmissionDocuments.AnyAsync(x => x.AttemptId == attemptId, ct);
            if (!answerDocumentExists)
                return BadRequest(new { message = "ابتدا تصویر یا PDF پاسخ‌نامه را بارگذاری کنید." });
            attempt.Status = "Submitted";
            attempt.Score = null;
            attempt.SubmittedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return Ok(new { id = attempt.Id, score = (decimal?)null, pendingManualCount = 1, status = attempt.Status, submittedAt = attempt.SubmittedAt });
        }

        var withinTime = DateTime.UtcNow <=
            attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes);

        // Accept the final payload only while the server-side timer is still valid.
        // Answers autosaved before expiry remain eligible for grading after expiry.
        if (withinTime)
        {
            foreach (var answer in r.Answers
                .GroupBy(x => x.QuestionId)
                .Select(group => group.Last()))
            {
                if (!attempt.Exam.Questions.Any(
                    item => item.QuestionId == answer.QuestionId))
                    continue;

                var saved = await db.ExamAnswers.FirstOrDefaultAsync(
                    item => item.ExamAttemptId == attemptId &&
                            item.QuestionId == answer.QuestionId, ct);

                if (saved is null)
                {
                    saved = new ExamAnswer
                    {
                        ExamAttemptId = attemptId,
                        QuestionId = answer.QuestionId
                    };
                    db.ExamAnswers.Add(saved);
                }

                saved.Answer = answer.Answer;
                saved.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Create explicit empty answer rows for unanswered questions. This keeps blank
        // essay questions available for manual grading from the student's uploaded PDF.
        var existingQuestionIdList = await db.ExamAnswers
            .Where(item => item.ExamAttemptId == attemptId)
            .Select(item => item.QuestionId)
            .ToListAsync(ct);
        var existingQuestionIds = existingQuestionIdList.ToHashSet();
        foreach (var examQuestion in attempt.Exam.Questions)
        {
            if (existingQuestionIds.Add(examQuestion.QuestionId))
            {
                db.ExamAnswers.Add(new ExamAnswer
                {
                    ExamAttemptId = attemptId,
                    QuestionId = examQuestion.QuestionId,
                    Answer = null
                });
            }
        }
        await db.SaveChangesAsync(ct);

        // Grade the complete persisted answer set, not only the final request body.
        var savedAnswers = await db.ExamAnswers
            .Where(item => item.ExamAttemptId == attemptId)
            .ToListAsync(ct);

        var answeredQuestionIds = savedAnswers
            .Select(item => item.QuestionId)
            .Distinct()
            .ToArray();

        var questionLookup = await db.Questions
            .Where(question => answeredQuestionIds.Contains(question.Id))
            .ToDictionaryAsync(question => question.Id, ct);

        var examQuestionLookup = attempt.Exam.Questions
            .ToDictionary(item => item.QuestionId);

        foreach (var saved in savedAnswers)
        {
            if (!questionLookup.TryGetValue(saved.QuestionId, out var question) ||
                !examQuestionLookup.TryGetValue(saved.QuestionId, out var examQuestion))
                continue;

            if (question.Type == "Matching")
            {
                try
                {
                    var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(question.CorrectAnswer ?? "") ?? new();
                    var submitted = JsonSerializer.Deserialize<Dictionary<string, string>>(saved.Answer ?? "") ?? new();
                    var correctCount = expected.Count(pair => submitted.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
                    var totalExamPoints = attempt.Exam.Questions.Sum(item => item.Score);
                    var normalizedQuestionScore = totalExamPoints <= 0 ? 0m : examQuestion.Score * 100m / totalExamPoints;
                    saved.IsCorrect = expected.Count > 0 && correctCount == expected.Count;
                    saved.AwardedScore = expected.Count == 0 ? 0m : Math.Round(normalizedQuestionScore * correctCount / expected.Count, 2);
                }
                catch (JsonException) { saved.IsCorrect = false; saved.AwardedScore = 0m; }
            }
            else if (question.Type == "MultipleChoice")
            {
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                {
                    saved.IsCorrect = null;
                    saved.AwardedScore = 0m;
                    continue;
                }

                saved.IsCorrect = string.Equals(
                    question.CorrectAnswer.Trim(),
                    saved.Answer?.Trim(),
                    StringComparison.OrdinalIgnoreCase);

                var totalExamPoints = attempt.Exam.Questions.Sum(item => item.Score);
                var normalizedQuestionScore = totalExamPoints <= 0
                    ? 0m
                    : examQuestion.Score * 100m / totalExamPoints;

                saved.AwardedScore = saved.IsCorrect == true
                    ? normalizedQuestionScore
                    : 0m;
            }
            else
            {
                // Essay answers require manual grading.
                saved.IsCorrect = null;
                saved.AwardedScore = 0m;
            }
        }

        attempt.Score = savedAnswers.Sum(item => item.AwardedScore);
        var pendingManualCount = savedAnswers.Count(item => item.IsCorrect is null);
        attempt.Status = pendingManualCount == 0 ? "Graded" : "Submitted";
        attempt.SubmittedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            id = attempt.Id,
            score = pendingManualCount > 0 ? (decimal?)null : attempt.Score,
            autoScore = attempt.Score,
            pendingManualCount,
            status = attempt.Status,
            submittedAt = attempt.SubmittedAt
        });
    }
}
[ApiController, Route("api/results"), Authorize]
public class ResultsController(AppDbContext db) : ControllerBase
{
    private bool CanManage(Exam exam) =>
        User.IsInRole("SuperAdmin") ||
        User.IsInRole("Department") && exam.DepartmentId == User.DepartmentId() ||
        User.IsInRole("Teacher") && exam.CreatedByUserId == User.UserId();

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? examId, [FromQuery] Guid? studentId, CancellationToken ct)
    {
        var q = db.ExamAttempts.Include(x => x.Exam).AsQueryable();
        if (User.IsInRole("Student")) q = q.Where(x => x.StudentId == User.UserId() && x.Exam.ResultsPublished && x.Status == "Graded");
        else if (User.IsInRole("Teacher")) q = q.Where(x => x.Exam.CreatedByUserId == User.UserId());
        else if (User.IsInRole("Department")) q = q.Where(x => x.Exam.DepartmentId == User.DepartmentId());
        if (examId.HasValue) q = q.Where(x => x.ExamId == examId.Value);
        if (studentId.HasValue && !User.IsInRole("Student")) q = q.Where(x => x.StudentId == studentId.Value);

        return Ok(await q.OrderByDescending(x => x.SubmittedAt).Select(x => new
        {
            id = x.Id, examId = x.ExamId, examTitle = x.Exam.Title,
            studentId = x.StudentId,
            studentName = db.Users.Where(u => u.Id == x.StudentId).Select(u => u.FullName).FirstOrDefault(),
            startedAt = x.StartedAt, submittedAt = x.SubmittedAt,
            score = (!User.IsInRole("Student") || x.Exam.ResultsPublished) ? x.Score : null,
            status = User.IsInRole("Student") && !x.Exam.ResultsPublished ? "AwaitingPublication" : x.Status,
            resultsPublished = x.Exam.ResultsPublished
        }).ToListAsync(ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Answers)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        var isStudent = User.IsInRole("Student");
        if (isStudent && (a.StudentId != User.UserId() || a.Status == "InProgress")) return NotFound();
        if (!isStudent && !CanManage(a.Exam)) return Forbid();

        var questionIds = a.Answers.Select(x => x.QuestionId).Distinct().ToArray();
        var questionDetails = await db.Questions.Where(x => questionIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Text, x.Type, x.CorrectAnswer }).ToDictionaryAsync(x => x.Id, ct);
        var questionScores = await db.ExamQuestions.Where(x => x.ExamId == a.ExamId && questionIds.Contains(x.QuestionId))
            .ToDictionaryAsync(x => x.QuestionId, x => x.Score, ct);
        var totalRawPoints = await db.ExamQuestions.Where(x => x.ExamId == a.ExamId)
            .SumAsync(x => (decimal?)x.Score, ct) ?? 0m;
        return Ok(new
        {
            id = a.Id, examId = a.ExamId, exam = a.Exam.Title,
            score = a.Exam.ResultsPublished ? a.Score : null,
            autoScore = isStudent && a.Exam.ResultsPublished ? null : a.Score,
            pendingManualCount = a.Answers.Count(x =>
                x.IsCorrect == null &&
                questionDetails.TryGetValue(x.QuestionId, out var pendingQuestion) &&
                (pendingQuestion.Type == "Essay" || pendingQuestion.Type == "Text")),
            maxScore = 100m, passingScore = a.Exam.PassingScore,
            status = a.Exam.ResultsPublished ? a.Status : "AwaitingPublication",
            resultsPublished = a.Exam.ResultsPublished,
            answers = a.Answers.Select(answer => new
            {
                questionId = answer.QuestionId,
                question = questionDetails.TryGetValue(answer.QuestionId, out var detail) ? detail.Text : null,
                questionType = questionDetails.TryGetValue(answer.QuestionId, out var typeDetail) ? typeDetail.Type : null,
                correctAnswer = (!isStudent || a.Exam.ResultsPublished) && questionDetails.TryGetValue(answer.QuestionId, out var correct) ? correct.CorrectAnswer : null,
                answer = answer.Answer,
                isCorrect = (!isStudent || a.Exam.ResultsPublished) ? answer.IsCorrect : null,
                awardedScore = (!isStudent || a.Exam.ResultsPublished) ? answer.AwardedScore : (decimal?)null,
                maxRawScore = questionScores.TryGetValue(answer.QuestionId, out var maxRaw) ? maxRaw : 0m,
                rawAwardedScore = totalRawPoints <= 0 ? 0m : answer.AwardedScore * totalRawPoints / 100m,
                isManuallyGraded = answer.IsManuallyGraded,
                gradingComment = answer.GradingComment
            })
        });
    }

    [HttpPut("{id:guid}/grade"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Grade(Guid id, [FromBody] decimal score, CancellationToken ct)
    {
        if (score < 0 || score > 100) return BadRequest(new { message = "نمره باید بین صفر تا ۱۰۰ باشد." });
        var a = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (!CanManage(a.Exam)) return Forbid();
        if (a.Exam.ResultsPublished) return BadRequest(new { message = "برای تغییر نمره، ابتدا انتشار نتایج آزمون را لغو کنید." });
        if (a.Status == "InProgress") return BadRequest(new { message = "ابتدا دانشجو باید آزمون را ارسال کند." });
        var pending = await db.ExamAnswers.Where(x => x.ExamAttemptId == id && x.IsCorrect == null).ToListAsync(ct);
        if (pending.Count > 0) return BadRequest(new { message = "برای تصحیح، از مسیر نمره‌دهی سؤال‌به‌سؤال استفاده کنید." });
        a.Score = score;
        a.Status = "Graded";
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("{attemptId:guid}/answers/{questionId:guid}/grade"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> GradeAnswer(Guid attemptId, Guid questionId, [FromBody] ManualGradeRequest request, CancellationToken ct)
    {
        if (request.Score < 0) return BadRequest(new { message = "نمره سؤال نمی‌تواند منفی باشد." });
        var attempt = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        if (!CanManage(attempt.Exam)) return Forbid();
        if (attempt.Exam.ResultsPublished) return BadRequest(new { message = "برای تغییر نمره، ابتدا انتشار نتایج آزمون را لغو کنید." });
        if (attempt.Status == "InProgress") return BadRequest(new { message = "آزمون هنوز ارسال نشده است." });
        var answer = await db.ExamAnswers.FirstOrDefaultAsync(x => x.ExamAttemptId == attemptId && x.QuestionId == questionId, ct);
        var examQuestion = await db.ExamQuestions.FirstOrDefaultAsync(x => x.ExamId == attempt.ExamId && x.QuestionId == questionId, ct);
        var questionType = await db.Questions.Where(x => x.Id == questionId).Select(x => x.Type).FirstOrDefaultAsync(ct);
        if (answer is null || examQuestion is null) return NotFound();
        if (questionType is not ("Essay" or "Text"))
            return BadRequest(new { message = "فقط سؤال تشریحی از این مسیر قابل تصحیح دستی است." });
        if (request.Score > examQuestion.Score) return BadRequest(new { message = $"حداکثر نمره خام این سؤال {examQuestion.Score} است." });
        var totalExamPoints = await db.ExamQuestions.Where(x => x.ExamId == attempt.ExamId).SumAsync(x => (decimal?)x.Score, ct) ?? 0m;
        answer.AwardedScore = totalExamPoints <= 0 ? 0m : request.Score * 100m / totalExamPoints;
        answer.IsCorrect = request.Score == examQuestion.Score;
        answer.IsManuallyGraded = true;
        answer.GradingComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        answer.GradedByUserId = User.UserId();
        answer.GradedAt = DateTime.UtcNow;
        var answers = await db.ExamAnswers.Where(x => x.ExamAttemptId == attemptId).ToListAsync(ct);
        attempt.Score = answers.Sum(x => x.AwardedScore);
        if (answers.All(x => x.IsCorrect != null)) attempt.Status = "Graded";
        await db.SaveChangesAsync(ct);
        return Ok(new { attemptId, questionId, answer.AwardedScore, attempt.Score, pendingManualCount = answers.Count(x => x.IsCorrect == null), status = attempt.Status });
    }

    [HttpPost("exams/{examId:guid}/publish"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Publish(Guid examId, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!CanManage(exam)) return Forbid();
        var inProgress = await db.ExamAttempts.AnyAsync(x => x.ExamId == examId && x.Status == "InProgress", ct);
        if (inProgress) return BadRequest(new { message = "هنوز دانشجویی در حال برگزاری آزمون است." });
        var pending = await db.ExamAttempts.Where(x => x.ExamId == examId)
            .AnyAsync(x => x.Answers.Any(a => a.IsCorrect == null) ||
                (!db.ExamQuestions.Any(question => question.ExamId == x.ExamId) && x.Status != "Graded"), ct);
        if (pending) return BadRequest(new { message = "هنوز پاسخ تشریحی تصحیح‌نشده وجود دارد." });
        var completedAttempts = await db.ExamAttempts.Include(x => x.Answers)
            .Where(x => x.ExamId == examId && x.Status != "InProgress").ToListAsync(ct);
        foreach (var attempt in completedAttempts)
        {
            attempt.Score = attempt.Answers.Count > 0
                ? attempt.Answers.Sum(answer => answer.AwardedScore)
                : attempt.Score ?? 0m;
            attempt.Status = "Graded";
        }
        exam.ResultsPublished = true;
        exam.IsPublished = false;
        exam.ResultsPublishedAt = DateTime.UtcNow;
        exam.ResultsPublishedByUserId = User.UserId();
        await db.SaveChangesAsync(ct);
        return Ok(new { examId, resultsPublished = true, publishedAt = exam.ResultsPublishedAt });
    }

    [HttpPost("exams/{examId:guid}/unpublish"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Unpublish(Guid examId, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!CanManage(exam)) return Forbid();
        exam.ResultsPublished = false;
        exam.ResultsPublishedAt = null;
        exam.ResultsPublishedByUserId = null;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

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
