using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController, Route("api/exam-files"), Authorize]
public sealed class ExamFilesController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity,
    IWebHostEnvironment environment) : ControllerBase
{
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private string StorageDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "exam-files");

    private long? CurrentUserId =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId =>
        long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    private bool CanManage(CanonicalExam exam) =>
        User.IsInRole("SuperAdmin") ||
        User.IsInRole("Department") && exam.DepartmentId == CurrentDepartmentId ||
        User.IsInRole("Teacher") && (exam.CreatedByUserId == CurrentUserId ||
            (exam.ClassId.HasValue && academic.ClassTeachers.Any(x =>
                x.ClassId == exam.ClassId.Value && x.TeacherUserId == CurrentUserId)));

    private async Task<(string Path, string Name)?> SaveFile(
        IFormFile? file, bool pdfOnly, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > MaxFileBytes) return null;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (pdfOnly ? extension != ".pdf" : extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            return null;

        await using var input = file.OpenReadStream();
        var header = new byte[8];
        var read = await input.ReadAsync(header.AsMemory(), ct);
        var valid = extension switch
        {
            ".pdf" => read >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => !pdfOnly && read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => !pdfOnly && read == 8 && header.AsSpan().SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
            _ => false
        };
        if (!valid) return null;

        Directory.CreateDirectory(StorageDirectory);
        var stored = $"{Guid.NewGuid():N}{extension}";
        var fullPath = Path.Combine(StorageDirectory, stored);
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
        {
            await output.WriteAsync(header.AsMemory(0, read), ct);
            await input.CopyToAsync(output, ct);
        }

        var original = new string(Path.GetFileName(file.FileName.Replace('\\', '/'))
            .Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (string.IsNullOrWhiteSpace(original)) original = $"document{extension}";
        if (original.Length > 255) original = original[^255..];
        return (fullPath, original);
    }

    private string ResolveStoredFile(string storedName)
    {
        var extension = Path.GetExtension(storedName).ToLowerInvariant();
        if (Path.GetFileName(storedName) != storedName ||
            extension is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            throw new InvalidOperationException("Invalid stored file reference.");
        return Path.Combine(StorageDirectory, storedName);
    }

    [HttpPost("exams/{examId:long}")]
    [Authorize(Policy = "TeacherOrAbove")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> UploadExamPdf(long examId, IFormFile? file, CancellationToken ct)
    {
        var exam = await academic.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!CanManage(exam)) return Forbid();

        var saved = await SaveFile(file, pdfOnly: true, ct);
        if (saved is null)
            return BadRequest(new { message = "فایل باید PDF معتبر و حداکثر ۲۰ مگابایت باشد." });

        var uploaderId = CurrentUserId;
        if (uploaderId is null || !await identity.Users.AnyAsync(x => x.Id == uploaderId && x.IsActive, ct))
        {
            System.IO.File.Delete(saved.Value.Path);
            return Unauthorized();
        }

        var document = await academic.ExamDocuments.FirstOrDefaultAsync(x => x.ExamId == examId, ct);
        var oldPath = document is null ? null : ResolveStoredFile(document.StoredFileName);
        if (document is null)
        {
            document = new CanonicalExamDocument { ExamId = examId };
            academic.ExamDocuments.Add(document);
        }
        document.StoredFileName = Path.GetFileName(saved.Value.Path);
        document.OriginalFileName = saved.Value.Name;
        document.FileSize = file!.Length;
        document.UploadedByUserId = uploaderId.Value;
        document.UploadedAt = DateTime.UtcNow;
        await academic.SaveChangesAsync(ct);

        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { examId, fileName = document.OriginalFileName, size = document.FileSize, uploadedAt = document.UploadedAt });
    }

    [HttpGet("exams/{examId:long}")]
    public async Task<IActionResult> DownloadExamPdf(long examId, CancellationToken ct)
    {
        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();

        if (User.IsInRole("Student"))
        {
            if (!exam.IsPublished || exam.DepartmentId != CurrentDepartmentId) return NotFound();
            var userId = CurrentUserId;
            if (userId is null || !exam.ClassId.HasValue ||
                !await academic.ClassMembers.AnyAsync(x => x.ClassId == exam.ClassId.Value && x.StudentUserId == userId, ct))
                return NotFound();
        }
        else if (!CanManage(exam)) return Forbid();

        var doc = await academic.ExamDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.ExamId == examId, ct);
        if (doc is null) return NotFound();
        var path = ResolveStoredFile(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf", doc.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("attempts/{attemptId:long}")]
    [Authorize(Roles = "Student")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> UploadStudentPdf(long attemptId, IFormFile? file, CancellationToken ct)
    {
        var studentId = CurrentUserId;
        if (studentId is null) return Unauthorized();

        var attempt = await academic.ExamAttempts.FirstOrDefaultAsync(
            x => x.Id == attemptId && x.StudentUserId == studentId, ct);
        if (attempt is null) return NotFound();

        var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
        if (exam is null) return NotFound();
        if (attempt.StatusId != 1 || DateTime.UtcNow > attempt.StartedAt.AddMinutes(exam.DurationMinutes))
            return BadRequest(new { message = "مهلت بارگذاری پاسخ این آزمون به پایان رسیده است." });

        var pdfOnlyExam = !await academic.ExamSections.AnyAsync(x => x.ExamId == exam.Id, ct);
        var saved = await SaveFile(file, pdfOnly: pdfOnlyExam, ct);
        if (saved is null)
            return BadRequest(new { message = pdfOnlyExam
                ? "برای آزمون PDFمحور فقط فایل PDF معتبر و حداکثر ۲۰ مگابایت پذیرفته می‌شود."
                : "فایل پاسخ باید PDF، JPG، JPEG یا PNG معتبر و حداکثر ۲۰ مگابایت باشد." });

        if (!await identity.Users.AnyAsync(x => x.Id == studentId && x.IsActive, ct))
        {
            System.IO.File.Delete(saved.Value.Path);
            return Unauthorized();
        }

        var document = await academic.ExamSubmissionDocuments.FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        var oldPath = document is null ? null : ResolveStoredFile(document.StoredFileName);
        if (document is null)
        {
            document = new CanonicalExamSubmissionDocument { AttemptId = attemptId };
            academic.ExamSubmissionDocuments.Add(document);
        }
        document.StoredFileName = Path.GetFileName(saved.Value.Path);
        document.OriginalFileName = saved.Value.Name;
        document.FileSize = file!.Length;
        document.UploadedByUserId = studentId.Value;
        document.UploadedAt = DateTime.UtcNow;
        await academic.SaveChangesAsync(ct);

        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { attemptId, fileName = document.OriginalFileName, size = document.FileSize, uploadedAt = document.UploadedAt });
    }

    [HttpGet("attempts/{attemptId:long}/info")]
    public async Task<IActionResult> StudentPdfInfo(long attemptId, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();

        if (User.IsInRole("Student"))
        {
            if (attempt.StudentUserId != CurrentUserId) return NotFound();
        }
        else
        {
            var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
            if (exam is null) return NotFound();
            if (!CanManage(exam)) return Forbid();
        }

        var doc = await academic.ExamSubmissionDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        return doc is null ? NotFound() : Ok(new { fileName = doc.OriginalFileName, size = doc.FileSize, uploadedAt = doc.UploadedAt });
    }

    [HttpGet("attempts/{attemptId:long}")]
    public async Task<IActionResult> DownloadStudentPdf(long attemptId, CancellationToken ct)
    {
        var attempt = await academic.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();

        if (User.IsInRole("Student"))
        {
            if (attempt.StudentUserId != CurrentUserId) return NotFound();
        }
        else
        {
            var exam = await academic.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attempt.ExamId, ct);
            if (exam is null) return NotFound();
            if (!CanManage(exam)) return Forbid();
        }

        var doc = await academic.ExamSubmissionDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        if (doc is null) return NotFound();
        var path = ResolveStoredFile(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        var contentType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            _ => "application/pdf"
        };
        return PhysicalFile(path, contentType, doc.OriginalFileName, enableRangeProcessing: true);
    }
}
