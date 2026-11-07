using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/questions/folders")]
[Authorize(Policy = "TeacherOrAbove")]
public sealed class CanonicalQuestionFoldersController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private long? CurrentUserId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    private IQueryable<CanonicalQuestionFolder> VisibleFolders()
    {
        var query = academic.QuestionFolders.AsNoTracking().AsQueryable();
        if (User.IsInRole("Teacher")) return query.Where(x => x.OwnerUserId == CurrentUserId);
        if (User.IsInRole("Department"))
        {
            // Resolve identity IDs before querying the academic database.
            var ids = identity.Users.AsNoTracking().Where(x => x.DepartmentId == CurrentDepartmentId).Select(x => x.Id);
            return query.Where(x => ids.Contains(x.OwnerUserId));
        }
        return query;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        var folders = await VisibleFolders().OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.OwnerUserId, x.ParentFolderId, x.CreatedAt,
                QuestionCount = academic.Questions.Count(q => q.FolderId == x.Id) })
            .ToListAsync(ct);
        var ownerIds = folders.Select(x => x.OwnerUserId).Distinct().ToArray();
        var owners = await identity.Users.AsNoTracking().Where(x => ownerIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return Ok(folders.Select(x => new { x.Id, x.Name, x.OwnerUserId, x.ParentFolderId, x.CreatedAt,
            x.QuestionCount, OwnerName = owners.GetValueOrDefault(x.OwnerUserId, "کاربر") }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FolderUpsert request, CancellationToken ct)
    {
        if (!User.IsInRole("Teacher") && !User.IsInRole("SuperAdmin")) return Forbid();
        if (CurrentUserId is null) return Unauthorized();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            return BadRequest(new { message = "نام پوشه الزامی و حداکثر ۱۵۰ کاراکتر است." });
        if (request.ParentFolderId.HasValue && !await academic.QuestionFolders.AnyAsync(x =>
            x.Id == request.ParentFolderId && x.OwnerUserId == CurrentUserId, ct))
            return BadRequest(new { message = "پوشه والد معتبر نیست." });
        if (await academic.QuestionFolders.AnyAsync(x => x.OwnerUserId == CurrentUserId &&
            x.ParentFolderId == request.ParentFolderId && x.Name == name, ct))
            return Conflict(new { message = "پوشه‌ای با این نام در این مسیر وجود دارد." });
        var folder = new CanonicalQuestionFolder { OwnerUserId = CurrentUserId.Value, ParentFolderId = request.ParentFolderId, Name = name };
        academic.QuestionFolders.Add(folder);
        await academic.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(List), new { }, new { folder.Id, folder.Name, folder.OwnerUserId, folder.ParentFolderId, folder.CreatedAt });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Rename(long id, [FromBody] FolderUpsert request, CancellationToken ct)
    {
        var folder = await academic.QuestionFolders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (folder is null) return NotFound();
        if (!await CanManageOwner(folder.OwnerUserId, ct)) return Forbid();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
            return BadRequest(new { message = "نام پوشه الزامی و حداکثر ۱۵۰ کاراکتر است." });
        if (await academic.QuestionFolders.AnyAsync(x => x.Id != id && x.OwnerUserId == folder.OwnerUserId &&
            x.ParentFolderId == folder.ParentFolderId && x.Name == name, ct))
            return Conflict(new { message = "پوشه‌ای با این نام در این مسیر وجود دارد." });
        folder.Name = name;
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        var folder = await academic.QuestionFolders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (folder is null) return NotFound();
        if (!await CanManageOwner(folder.OwnerUserId, ct)) return Forbid();
        if (await academic.QuestionFolders.AnyAsync(x => x.ParentFolderId == id, ct) ||
            await academic.Questions.AnyAsync(x => x.FolderId == id, ct))
            return Conflict(new { message = "پوشه باید خالی باشد و زیرپوشه‌ای نداشته باشد؛ ابتدا سؤال‌ها و زیرپوشه‌ها را منتقل کنید." });
        academic.QuestionFolders.Remove(folder);
        await academic.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("move")]
    public async Task<IActionResult> MoveQuestions([FromBody] MoveQuestionsRequest request, CancellationToken ct)
    {
        if (request.QuestionIds is null || request.QuestionIds.Count == 0 || request.QuestionIds.Count > 500)
            return BadRequest(new { message = "بین ۱ تا ۵۰۰ سؤال را انتخاب کنید." });
        if (request.QuestionIds.Distinct().Count() != request.QuestionIds.Count)
            return BadRequest(new { message = "شناسه تکراری در فهرست سؤال‌ها وجود دارد." });
        var questions = await academic.Questions.Where(x => request.QuestionIds.Contains(x.Id)).ToListAsync(ct);
        if (questions.Count != request.QuestionIds.Count) return NotFound(new { message = "بعضی از سؤال‌های انتخاب‌شده پیدا نشدند." });
        var owners = questions.Select(x => x.CreatedByUserId).Distinct().ToArray();
        if (owners.Length != 1 || !await CanManageOwner(owners[0], ct)) return Forbid();
        if (request.FolderId.HasValue && !await academic.QuestionFolders.AnyAsync(x =>
            x.Id == request.FolderId && x.OwnerUserId == owners[0], ct))
            return BadRequest(new { message = "پوشه مقصد متعلق به مالک این سؤال‌ها نیست." });
        foreach (var question in questions) question.FolderId = request.FolderId;
        await academic.SaveChangesAsync(ct);
        return Ok(new { moved = questions.Count, folderId = request.FolderId });
    }

    [HttpPut("questions/{id:long}/organization")]
    public async Task<IActionResult> UpdateQuestionOrganization(long id, [FromBody] QuestionOrganizationRequest request, CancellationToken ct)
    {
        var question = await academic.Questions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (question is null) return NotFound();
        if (!await CanManageOwner(question.CreatedByUserId, ct)) return Forbid();
        if (request.FolderId.HasValue && !await academic.QuestionFolders.AnyAsync(x =>
            x.Id == request.FolderId && x.OwnerUserId == question.CreatedByUserId, ct))
            return BadRequest(new { message = "پوشه مقصد معتبر نیست." });
        var tags = (request.Tags ?? new List<string>()).Select(x => x?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();
        if (tags.Any(x => x.Length > 40)) return BadRequest(new { message = "هر برچسب حداکثر ۴۰ کاراکتر است." });
        question.FolderId = request.FolderId;
        question.TagsJson = tags.Count == 0 ? null : JsonSerializer.Serialize(tags);
        await academic.SaveChangesAsync(ct);
        return Ok(new { question.Id, question.FolderId, Tags = tags });
    }

    [HttpGet("duplicates")]
    public async Task<IActionResult> Duplicates(CancellationToken ct)
    {
        var query = academic.Questions.AsNoTracking().AsQueryable();
        if (User.IsInRole("Teacher")) query = query.Where(x => x.CreatedByUserId == CurrentUserId);
        else if (User.IsInRole("Department"))
        {
            var ids = await identity.Users.AsNoTracking().Where(x => x.DepartmentId == CurrentDepartmentId)
                .Select(x => x.Id).ToListAsync(ct);
            query = query.Where(x => ids.Contains(x.CreatedByUserId));
        }
        var rows = await query.Select(x => new { x.Id, x.SkillId, x.Text, x.ImportBatchId, x.FolderId }).ToListAsync(ct);
        var duplicates = rows.GroupBy(x => new { x.SkillId, Text = Normalize(x.Text) })
            .Where(g => g.Key.Text.Length > 0 && g.Count() > 1)
            .Select(g => new { g.Key.SkillId, Text = g.First().Text, Count = g.Count(),
                Questions = g.Select(x => new { x.Id, x.ImportBatchId, x.FolderId }).OrderBy(x => x.Id).ToList() })
            .ToList();
        return Ok(duplicates);
    }

    private async Task<bool> CanManageOwner(long ownerId, CancellationToken ct)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        if (User.IsInRole("Teacher")) return CurrentUserId == ownerId;
        return User.IsInRole("Department") &&
            await identity.Users.AnyAsync(x => x.Id == ownerId && x.DepartmentId == CurrentDepartmentId, ct);
    }

    private static string Normalize(string? value) => string.Join(' ', (value ?? "").Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public sealed class FolderUpsert { public string Name { get; set; } = ""; public long? ParentFolderId { get; set; } }
public sealed class MoveQuestionsRequest { public List<long> QuestionIds { get; set; } = new(); public long? FolderId { get; set; } }
public sealed class QuestionOrganizationRequest { public long? FolderId { get; set; } public List<string> Tags { get; set; } = new(); }
