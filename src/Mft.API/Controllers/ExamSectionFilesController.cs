using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/exam-files")]
[Authorize]
public sealed class ExamSectionFilesController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity,
    IWebHostEnvironment environment) : ControllerBase
{
    private const long DefaultMaxFileBytes = 20 * 1024 * 1024;
    private const long AbsoluteMaxFileBytes = 100 * 1024 * 1024;
    private string StorageDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "exam-files");
    private long? UserId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? DepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    private async Task<bool> CanManage(CanonicalExam exam, CancellationToken ct)
    {
        if (User.IsInRole("SuperAdmin")) return true;
        if (User.IsInRole("Department")) return exam.DepartmentId == DepartmentId;
        if (User.IsInRole("Teacher"))
            return exam.CreatedByUserId == UserId ||
                (exam.ClassId.HasValue && await academic.ClassTeachers.AnyAsync(
                    x => x.ClassId == exam.ClassId.Value && x.TeacherUserId == UserId, ct));
        return false;
    }

    private long MaxBytes(CanonicalExam exam)
    {
        var configured = exam.MaxPdfFileSizeBytes.GetValueOrDefault(DefaultMaxFileBytes);
        return Math.Clamp(configured, 1, AbsoluteMaxFileBytes);
    }

    private async Task<(string StoredName, string OriginalName)?> SavePdf(IFormFile? file, long maxBytes, CancellationToken ct)
    {
        if (file is null || file.Length <= 0 || file.Length > maxBytes) return null;
        if (!Path.GetExtension(file.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return null;

        await using var input = file.OpenReadStream();
        var header = new byte[5];
        var read = await input.ReadAsync(header.AsMemory(), ct);
        if (read != 5 || !header.AsSpan().SequenceEqual("%PDF-"u8)) return null;

        Directory.CreateDirectory(StorageDirectory);
        var stored = $"{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(StorageDirectory, stored);
        try
        {
            await using var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await output.WriteAsync(header.AsMemory(), ct);
            await input.CopyToAsync(output, ct);
        }
        catch
        {
            if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            throw;
        }

        var original = new string(Path.GetFileName(file.FileName.Replace('\\', '/'))
            .Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(original)) original = "document.pdf";
        if (original.Length > 255) original = original[^255..];
        return (stored, original);
    }

    private string Resolve(string storedName)
    {
        if (Path.GetFileName(storedName) != storedName ||
            !Path.GetExtension(storedName).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid stored PDF reference.");
        return Path.Combine(StorageDirectory, storedName);
    }

    private async Task<(CanonicalExamSection Section, CanonicalExam Exam)?> LoadSection(long sectionId, CancellationToken ct)
    {
        var section = await academic.ExamSections.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sectionId, ct);
        if (section is null) return null;
        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == section.ExamId, ct);
        return exam is null ? null : (section, exam);
    }

    private async Task<bool> StudentCanAccess(CanonicalExam exam, long attemptId, CancellationToken ct)
    {
        if (UserId is null || !exam.IsPublished || exam.DepartmentId != DepartmentId ||
            !exam.ClassId.HasValue ||
            !await academic.ClassMembers.AnyAsync(x => x.ClassId == exam.ClassId.Value && x.StudentUserId == UserId, ct))
            return false;
        return await academic.ExamAttempts.AnyAsync(x =>
            x.Id == attemptId && x.ExamId == exam.Id && x.StudentUserId == UserId, ct);
    }

    [HttpPost("sections/{sectionId:long}/question-paper")]
    [Authorize(Policy = "TeacherOrAbove")]
    [RequestSizeLimit(AbsoluteMaxFileBytes)]
    public async Task<IActionResult> UploadQuestionPaper(long sectionId, IFormFile? file, CancellationToken ct)
    {
        var loaded = await LoadSection(sectionId, ct);
        if (loaded is null) return NotFound();
        var (section, exam) = loaded.Value;
        if (!await CanManage(exam, ct)) return Forbid();
        if (!string.Equals(section.SectionType, "Essay", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط برای بخش Essay امکان بارگذاری PDF صورت سؤال وجود دارد." });
        if (exam.IsPublished || await academic.ExamAttempts.AnyAsync(x => x.ExamId == exam.Id, ct))
            return Conflict(new { message = "پس از انتشار آزمون یا شروع تلاش دانشجو، PDF صورت سؤال این بخش قابل جایگزینی نیست." });
        if (UserId is null || !await identity.Users.AnyAsync(x => x.Id == UserId && x.IsActive, ct))
            return Unauthorized();

        var maxBytes = MaxBytes(exam);
        var saved = await SavePdf(file, maxBytes, ct);
        if (saved is null)
            return BadRequest(new { message = $"فایل باید PDF معتبر و حداکثر {maxBytes / 1024 / 1024} مگابایت باشد." });

        var existing = await academic.ExamSectionDocuments.FirstOrDefaultAsync(x => x.ExamSectionId == sectionId, ct);
        var oldPath = existing is null ? null : Resolve(existing.StoredFileName);
        if (existing is null)
        {
            existing = new CanonicalExamSectionDocument { ExamSectionId = sectionId };
            academic.ExamSectionDocuments.Add(existing);
        }
        existing.StoredFileName = saved.Value.StoredName;
        existing.OriginalFileName = saved.Value.OriginalName;
        existing.FileSize = file!.Length;
        existing.UploadedByUserId = UserId.Value;
        existing.UploadedAt = DateTime.UtcNow;
        try { await academic.SaveChangesAsync(ct); }
        catch
        {
            var newPath = Resolve(saved.Value.StoredName);
            if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
            throw;
        }
        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { sectionId, fileName = existing.OriginalFileName, size = existing.FileSize, uploadedAt = existing.UploadedAt });
    }

    [HttpGet("sections/{sectionId:long}/question-paper")]
    public async Task<IActionResult> DownloadQuestionPaper(long sectionId, [FromQuery] long? attemptId, CancellationToken ct)
    {
        var loaded = await LoadSection(sectionId, ct);
        if (loaded is null) return NotFound();
        var (section, exam) = loaded.Value;
        if (User.IsInRole("Student"))
        {
            if (!attemptId.HasValue || !await StudentCanAccess(exam, attemptId.Value, ct)) return NotFound();
        }
        else if (!await CanManage(exam, ct)) return Forbid();

        var doc = await academic.ExamSectionDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.ExamSectionId == sectionId, ct);
        if (doc is null) return NotFound();
        var path = Resolve(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return PhysicalFile(path, "application/pdf", doc.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("sections/{sectionId:long}/attempts/{attemptId:long}/answer")]
    [Authorize(Roles = "Student")]
    [RequestSizeLimit(AbsoluteMaxFileBytes)]
    public async Task<IActionResult> UploadSectionAnswer(long sectionId, long attemptId, IFormFile? file, CancellationToken ct)
    {
        var loaded = await LoadSection(sectionId, ct);
        if (loaded is null) return NotFound();
        var (section, exam) = loaded.Value;
        if (!string.Equals(section.SectionType, "Essay", StringComparison.OrdinalIgnoreCase)) return NotFound();
        if (UserId is null || !await StudentCanAccess(exam, attemptId, ct)) return NotFound();

        var attempt = await academic.ExamAttempts.FirstAsync(x => x.Id == attemptId, ct);
        var deadline = attempt.StartedAt.AddMinutes(exam.DurationMinutes);
        if (attempt.StatusId != 1 || DateTime.UtcNow >= deadline)
            return Conflict(new { message = "مهلت بارگذاری پاسخ این بخش به پایان رسیده است." });
        if (!await academic.ExamSectionDocuments.AnyAsync(x => x.ExamSectionId == sectionId, ct))
            return Conflict(new { message = "فایل PDF صورت سؤال این بخش هنوز توسط استاد بارگذاری نشده است." });
        if (!await identity.Users.AnyAsync(x => x.Id == UserId && x.IsActive, ct)) return Unauthorized();

        var maxBytes = MaxBytes(exam);
        var saved = await SavePdf(file, maxBytes, ct);
        if (saved is null)
            return BadRequest(new { message = $"فایل پاسخ باید PDF معتبر و حداکثر {maxBytes / 1024 / 1024} مگابایت باشد." });

        var existing = await academic.ExamSectionSubmissionDocuments
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.ExamSectionId == sectionId, ct);
        var oldPath = existing is null ? null : Resolve(existing.StoredFileName);
        if (existing is null)
        {
            existing = new CanonicalExamSectionSubmissionDocument { AttemptId = attemptId, ExamSectionId = sectionId };
            academic.ExamSectionSubmissionDocuments.Add(existing);
        }
        existing.StoredFileName = saved.Value.StoredName;
        existing.OriginalFileName = saved.Value.OriginalName;
        existing.FileSize = file!.Length;
        existing.UploadedByUserId = UserId.Value;
        existing.UploadedAt = DateTime.UtcNow;
        try { await academic.SaveChangesAsync(ct); }
        catch
        {
            var newPath = Resolve(saved.Value.StoredName);
            if (System.IO.File.Exists(newPath)) System.IO.File.Delete(newPath);
            throw;
        }
        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { sectionId, attemptId, fileName = existing.OriginalFileName, size = existing.FileSize, uploadedAt = existing.UploadedAt });
    }

    [HttpGet("sections/{sectionId:long}/attempts/{attemptId:long}/answer")]
    public async Task<IActionResult> DownloadSectionAnswer(long sectionId, long attemptId, CancellationToken ct)
    {
        var loaded = await LoadSection(sectionId, ct);
        if (loaded is null) return NotFound();
        var (section, exam) = loaded.Value;
        if (User.IsInRole("Student"))
        {
            if (UserId is null || !await StudentCanAccess(exam, attemptId, ct)) return NotFound();
        }
        else if (!await CanManage(exam, ct)) return Forbid();

        var doc = await academic.ExamSectionSubmissionDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId && x.ExamSectionId == sectionId, ct);
        if (doc is null) return NotFound();
        var path = Resolve(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf", doc.OriginalFileName, enableRangeProcessing: true);
    }
}
