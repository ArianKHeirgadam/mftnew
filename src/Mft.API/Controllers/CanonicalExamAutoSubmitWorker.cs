using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

/// <summary>
/// Server-side deadline finalizer. It invokes the same canonical Submit action used
/// by students, so automatic grading, matching validation and manual-review flags
/// are not reimplemented in a second scoring path.
/// </summary>
public sealed class CanonicalExamAutoSubmitWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CanonicalExamAutoSubmitWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredAttempts(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Canonical exam auto-submit polling failed.");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task ProcessExpiredAttempts(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var academic = scope.ServiceProvider.GetRequiredService<CanonicalAcademicDbContext>();
        var identity = scope.ServiceProvider.GetRequiredService<CanonicalIdentityDbContext>();
        var now = DateTime.UtcNow;

        var expired = await (
            from attempt in academic.ExamAttempts.AsNoTracking()
            join exam in academic.Exams.AsNoTracking() on attempt.ExamId equals exam.Id
            where attempt.StatusId == 1 &&
                attempt.ManualReviewRequired.HasValue &&
                attempt.StartedAt.AddMinutes(exam.DurationMinutes) <= now
            orderby attempt.StartedAt
            select new
            {
                AttemptId = attempt.Id,
                attempt.ExamId,
                attempt.StudentUserId,
                exam.DepartmentId
            })
            .Take(50)
            .ToListAsync(ct);

        foreach (var item in expired)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var questionIds = await academic.ExamQuestions.AsNoTracking()
                    .Where(question => academic.ExamSections.Any(section =>
                        section.Id == question.ExamSectionId && section.ExamId == item.ExamId))
                    .Select(question => question.Id)
                    .ToListAsync(ct);

                var savedAnswers = await academic.StudentAnswers.AsNoTracking()
                    .Where(answer => answer.ExamAttemptId == item.AttemptId)
                    .ToDictionaryAsync(answer => answer.ExamQuestionId, answer => answer.AnswerJson, ct);

                var answers = new List<CanonicalSubmittedAnswer>(questionIds.Count);
                foreach (var questionId in questionIds)
                {
                    var json = savedAnswers.GetValueOrDefault(questionId) ?? "{}";
                    using var document = JsonDocument.Parse(json);
                    answers.Add(new CanonicalSubmittedAnswer
                    {
                        ExamQuestionId = questionId,
                        Value = document.RootElement.Clone()
                    });
                }

                var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, item.StudentUserId.ToString()),
                    new Claim(ClaimTypes.Role, "Student"),
                    new Claim("departmentId", item.DepartmentId.ToString())
                }, "CanonicalExamAutoSubmit"));

                var httpContext = new DefaultHttpContext
                {
                    RequestServices = scope.ServiceProvider,
                    User = principal
                };
                var controller = new CanonicalExamAttemptController(academic, identity)
                {
                    ControllerContext = new ControllerContext { HttpContext = httpContext }
                };

                var result = await controller.Submit(
                    item.ExamId,
                    item.AttemptId,
                    new CanonicalSubmitAttemptRequest { AutoSubmit = true, Answers = answers },
                    ct);

                if (result is ObjectResult objectResult &&
                    objectResult.StatusCode is >= 400)
                {
                    logger.LogWarning(
                        "Auto-submit did not finalize attempt {AttemptId}; API status {StatusCode}.",
                        item.AttemptId, objectResult.StatusCode);
                }
                else
                {
                    logger.LogInformation(
                        "Auto-submit finalized expired attempt {AttemptId}.", item.AttemptId);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception,
                    "Auto-submit failed for expired attempt {AttemptId}.", item.AttemptId);
            }
        }
    }
}
