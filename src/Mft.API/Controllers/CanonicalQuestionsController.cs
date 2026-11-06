using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/canonical/questions")]
[Authorize(Policy = "TeacherOrAbove")]
public sealed class CanonicalQuestionsController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? CurrentUserId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] long? skillId, [FromQuery] long? questionTypeId, CancellationToken ct)
    {
        var q = academic.Questions.AsNoTracking().AsQueryable();
        if (User.IsInRole("Teacher")) q = q.Where(x => x.CreatedByUserId == CurrentUserId);
        if (User.IsInRole("Department")) q = q.Where(x => identity.Users.Any(u => u.Id == x.CreatedByUserId && u.DepartmentId == CurrentDepartmentId));
        if (skillId.HasValue) q = q.Where(x => x.SkillId == skillId.Value);
        if (questionTypeId.HasValue) q = q.Where(x => x.QuestionTypeId == questionTypeId.Value);
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Text.Contains(search));
        var rows = await q.OrderByDescending(x => x.Id).Select(x => new {
            x.Id, x.SkillId, x.QuestionTypeId, x.Text, x.Points, x.DifficultyLevel,
            SkillName = academic.Skills.Where(s => s.Id == x.SkillId).Select(s => s.Name).FirstOrDefault(),
            CategoryName = academic.ExamCategories.Where(cat => cat.Id == academic.Skills.Where(s => s.Id == x.SkillId).Select(s => s.ExamCategoryId).FirstOrDefault()).Select(cat => cat.Name).FirstOrDefault(),
            QuestionTypeCode = academic.QuestionTypes.Where(t => t.Id == x.QuestionTypeId).Select(t => t.Code).FirstOrDefault(),
            QuestionTypeName = academic.QuestionTypes.Where(t => t.Id == x.QuestionTypeId).Select(t => t.Name).FirstOrDefault(),
            Options = academic.MultipleChoiceOptions.Where(o => o.QuestionId == x.Id).OrderBy(o => o.DisplayOrder).Select(o => new { o.Id, o.OptionText, o.IsCorrect, o.DisplayOrder }).ToList(),
            MatchingPairs = academic.MatchingPairs.Where(p => p.QuestionId == x.Id).OrderBy(p => p.DisplayOrder).Select(p => new { p.Id, p.LeftItem, p.RightItem, p.DisplayOrder }).ToList(),
            Criteria = academic.EssayGradingCriteria.Where(c => c.QuestionId == x.Id).Select(c => new { c.Id, c.CriteriaText, c.MaxPoints }).ToList()
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("lookups")]
    public async Task<IActionResult> Lookups(CancellationToken ct)
    {
        var categories = await academic.ExamCategories.AsNoTracking()
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct);
        var skills = await academic.Skills.AsNoTracking()
            .OrderBy(x => x.ExamCategoryId).ThenBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.ExamCategoryId }).ToListAsync(ct);
        var types = await academic.QuestionTypes.AsNoTracking()
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Name }).ToListAsync(ct);
        return Ok(new { Categories = categories, Skills = skills, QuestionTypes = types });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var q = await academic.Questions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (q is null) return NotFound();
        if (!await CanManage(q.CreatedByUserId, ct)) return Forbid();
        return Ok(new {
            q.Id, q.SkillId, q.QuestionTypeId, q.Text, q.Points, q.DifficultyLevel,
            Options = await academic.MultipleChoiceOptions.AsNoTracking().Where(x => x.QuestionId == id).OrderBy(x => x.DisplayOrder).ToListAsync(ct),
            MatchingPairs = await academic.MatchingPairs.AsNoTracking().Where(x => x.QuestionId == id).OrderBy(x => x.DisplayOrder).ToListAsync(ct),
            Criteria = await academic.EssayGradingCriteria.AsNoTracking().Where(x => x.QuestionId == id).ToListAsync(ct)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(QuestionUpsert request, CancellationToken ct)
    {
        var validation = await Validate(request, ct);
        if (validation is not null) return validation;
        if (CurrentUserId is null) return Unauthorized();
        var entity = new CanonicalQuestion {
            SkillId = request.SkillId, QuestionTypeId = request.QuestionTypeId,
            Text = request.Text.Trim(), Points = request.Points,
            DifficultyLevel = request.DifficultyLevel, CreatedByUserId = CurrentUserId.Value,
            MetadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata)
        };
        academic.Questions.Add(entity);
        ApplyChildren(entity, request);
        await academic.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new { id = entity.Id });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, QuestionUpsert request, CancellationToken ct)
    {
        var entity = await academic.Questions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!await CanManage(entity.CreatedByUserId, ct)) return Forbid();
        var validation = await Validate(request, ct);
        if (validation is not null) return validation;
        if (await academic.ExamQuestions.AnyAsync(x => x.QuestionId == id, ct) &&
            entity.QuestionTypeId != request.QuestionTypeId)
            return Conflict(new { message = "نوع سؤال استفاده‌شده در آزمون قابل تغییر نیست." });
        entity.SkillId = request.SkillId;
        entity.QuestionTypeId = request.QuestionTypeId;
        entity.Text = request.Text.Trim();
        entity.Points = request.Points;
        entity.DifficultyLevel = request.DifficultyLevel;
        entity.MetadataJson = request.Metadata is null ? null : JsonSerializer.Serialize(request.Metadata);
        academic.MultipleChoiceOptions.RemoveRange(academic.MultipleChoiceOptions.Where(x => x.QuestionId == id));
        academic.MatchingPairs.RemoveRange(academic.MatchingPairs.Where(x => x.QuestionId == id));
        academic.EssayGradingCriteria.RemoveRange(academic.EssayGradingCriteria.Where(x => x.QuestionId == id));
        await academic.SaveChangesAsync(ct);
        ApplyChildren(entity, request);
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var entity = await academic.Questions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound();
        if (!await CanManage(entity.CreatedByUserId, ct)) return Forbid();
        if (await academic.ExamQuestions.AnyAsync(x => x.QuestionId == id, ct))
            return Conflict(new { message = "این سؤال در ساختار آزمون استفاده شده و قابل حذف نیست." });
        academic.Questions.Remove(entity);
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<IActionResult?> Validate(QuestionUpsert r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Text) || r.Points <= 0 || r.Points > 9999 ||
            r.DifficultyLevel is > 5 || r.SkillId <= 0 || r.QuestionTypeId <= 0)
            return BadRequest(new { message = "متن، مهارت، نوع، سطح دشواری و نمره سؤال معتبر الزامی است." });
        var skill = await academic.Skills.FirstOrDefaultAsync(x => x.Id == r.SkillId, ct);
        if (skill is null) return BadRequest(new { message = "مهارت انتخاب‌شده معتبر نیست." });
        if (!await academic.QuestionTypes.AnyAsync(x => x.Id == r.QuestionTypeId, ct))
            return BadRequest(new { message = "نوع سؤال انتخاب‌شده معتبر نیست." });
        if (User.IsInRole("Department") && skill.ExamCategoryId <= 0) return Forbid();
        var type = await academic.QuestionTypes.Where(x => x.Id == r.QuestionTypeId).Select(x => x.Code).SingleAsync(ct);
        if (type.Equals("MultipleChoice", StringComparison.OrdinalIgnoreCase) &&
            (r.Options.Count < 2 || r.Options.Count(x => x.IsCorrect) != 1 || r.Options.Any(x => string.IsNullOrWhiteSpace(x.OptionText))))
            return BadRequest(new { message = "سؤال چهارگزینه‌ای باید حداقل دو گزینه و دقیقاً یک پاسخ صحیح داشته باشد." });
        if (type.Equals("Matching", StringComparison.OrdinalIgnoreCase) &&
            (r.MatchingPairs.Count < 2 || r.MatchingPairs.Any(x => string.IsNullOrWhiteSpace(x.LeftItem) || string.IsNullOrWhiteSpace(x.RightItem))))
            return BadRequest(new { message = "سؤال وصل‌کردنی باید حداقل دو جفت کامل داشته باشد." });
        return null;
    }

    private void ApplyChildren(CanonicalQuestion q, QuestionUpsert r)
    {
        for (var i = 0; i < r.Options.Count; i++)
            academic.MultipleChoiceOptions.Add(new CanonicalMultipleChoiceOption { QuestionId = q.Id, OptionText = r.Options[i].OptionText.Trim(), IsCorrect = r.Options[i].IsCorrect, DisplayOrder = r.Options[i].DisplayOrder > 0 ? r.Options[i].DisplayOrder : i + 1 });
        for (var i = 0; i < r.MatchingPairs.Count; i++)
            academic.MatchingPairs.Add(new CanonicalMatchingPair { QuestionId = q.Id, LeftItem = r.MatchingPairs[i].LeftItem.Trim(), RightItem = r.MatchingPairs[i].RightItem.Trim(), DisplayOrder = r.MatchingPairs[i].DisplayOrder > 0 ? r.MatchingPairs[i].DisplayOrder : i + 1 });
        foreach (var c in r.Criteria.Where(x => !string.IsNullOrWhiteSpace(x.CriteriaText) && x.MaxPoints > 0))
            academic.EssayGradingCriteria.Add(new CanonicalEssayGradingCriterion { QuestionId = q.Id, CriteriaText = c.CriteriaText.Trim(), MaxPoints = c.MaxPoints });
    }

    private async Task<bool> CanManage(long creatorId, CancellationToken ct)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        if (User.IsInRole("Teacher")) return CurrentUserId == creatorId;
        return User.IsInRole("Department") && await identity.Users.AnyAsync(x => x.Id == creatorId && x.DepartmentId == CurrentDepartmentId, ct);
    }
}

public sealed class QuestionUpsert
{
    public long SkillId { get; set; }
    public byte QuestionTypeId { get; set; }
    public string Text { get; set; } = "";
    public decimal Points { get; set; } = 1;
    public byte? DifficultyLevel { get; set; }
    public JsonElement? Metadata { get; set; }
    public List<QuestionOptionInput> Options { get; set; } = new();
    public List<MatchingPairInput> MatchingPairs { get; set; } = new();
    public List<EssayCriterionInput> Criteria { get; set; } = new();
}
public sealed class QuestionOptionInput { public string OptionText { get; set; } = ""; public bool IsCorrect { get; set; } public int DisplayOrder { get; set; } }
public sealed class MatchingPairInput { public string LeftItem { get; set; } = ""; public string RightItem { get; set; } = ""; public int DisplayOrder { get; set; } }
public sealed class EssayCriterionInput { public string CriteriaText { get; set; } = ""; public decimal MaxPoints { get; set; } }
