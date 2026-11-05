using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

public sealed record ManualGradeRequest(decimal Score, string? Comment);

[ApiController, Route("api/exam-files"), Authorize]
public sealed class ExamFilesController(AppDbContext db, IWebHostEnvironment environment) : ControllerBase
{
    private const long MaxPdfBytes = 20 * 1024 * 1024;
    private string StorageDirectory => Path.Combine(environment.ContentRootPath, "App_Data", "exam-files");

    private bool CanManage(Exam exam) =>
        User.IsInRole("SuperAdmin") ||
        User.IsInRole("Department") && exam.DepartmentId == User.DepartmentId() ||
        User.IsInRole("Teacher") && exam.CreatedByUserId == User.UserId();

    private async Task<(string Path, string Name)?> SavePdf(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > MaxPdfBytes) return null;
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase)) return null;

        await using var input = file.OpenReadStream();
        var header = new byte[5];
        var read = await input.ReadAsync(header.AsMemory(0, header.Length), ct);
        if (read != 5 || !header.AsSpan().SequenceEqual("%PDF-"u8)) return null;
        input.Position = 0;

        Directory.CreateDirectory(StorageDirectory);
        var stored = $"{Guid.NewGuid():N}.pdf";
        var fullPath = Path.Combine(StorageDirectory, stored);
        await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            await input.CopyToAsync(output, ct);

        var original = Path.GetFileName(file.FileName);
        if (original.Length > 255) original = original[^255..];
        return (fullPath, original);
    }

    private string ResolveStoredFile(string storedName)
    {
        if (Path.GetFileName(storedName) != storedName || !storedName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid stored file reference.");
        return Path.Combine(StorageDirectory, storedName);
    }

    [HttpPost("exams/{examId:guid}")]
    [Authorize(Policy = "TeacherOrAbove")]
    [RequestSizeLimit(MaxPdfBytes)]
    public async Task<IActionResult> UploadExamPdf(Guid examId, IFormFile? file, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (!CanManage(exam)) return Forbid();
        var saved = await SavePdf(file, ct);
        if (saved is null) return BadRequest(new { message = "فایل باید PDF معتبر و حداکثر ۲۰ مگابایت باشد." });

        var old = await db.ExamDocuments.FirstOrDefaultAsync(x => x.ExamId == examId, ct);
        var oldPath = old is null ? null : ResolveStoredFile(old.StoredFileName);
        if (old is null)
        {
            old = new ExamDocument { ExamId = examId };
            db.ExamDocuments.Add(old);
        }
        old.StoredFileName = Path.GetFileName(saved.Value.Path);
        old.OriginalFileName = saved.Value.Name;
        old.FileSize = file!.Length;
        old.UploadedByUserId = User.UserId();
        old.UploadedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { examId, fileName = old.OriginalFileName, size = old.FileSize, uploadedAt = old.UploadedAt });
    }

    [HttpGet("exams/{examId:guid}")]
    public async Task<IActionResult> DownloadExamPdf(Guid examId, CancellationToken ct)
    {
        var exam = await db.Exams.FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return NotFound();
        if (User.IsInRole("Student"))
        {
            if ((!exam.IsPublished && !exam.ResultsPublished) || (exam.DepartmentId.HasValue && exam.DepartmentId != User.DepartmentId())) return NotFound();
        }
        else if (!CanManage(exam)) return Forbid();

        var doc = await db.ExamDocuments.FirstOrDefaultAsync(x => x.ExamId == examId, ct);
        if (doc is null) return NotFound();
        var path = ResolveStoredFile(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf", doc.OriginalFileName);
    }

    [HttpPost("attempts/{attemptId:guid}")]
    [Authorize(Roles = "Student")]
    [RequestSizeLimit(MaxPdfBytes)]
    public async Task<IActionResult> UploadStudentPdf(Guid attemptId, IFormFile? file, CancellationToken ct)
    {
        var attempt = await db.ExamAttempts.Include(x => x.Exam)
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == User.UserId(), ct);
        if (attempt is null) return NotFound();
        if (attempt.Status != "InProgress" || DateTime.UtcNow > attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes))
            return BadRequest(new { message = "مهلت بارگذاری پاسخ این آزمون به پایان رسیده است." });

        var saved = await SavePdf(file, ct);
        if (saved is null) return BadRequest(new { message = "فایل باید PDF معتبر و حداکثر ۲۰ مگابایت باشد." });
        var old = await db.ExamSubmissionDocuments.FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        var oldPath = old is null ? null : ResolveStoredFile(old.StoredFileName);
        if (old is null)
        {
            old = new ExamSubmissionDocument { AttemptId = attemptId };
            db.ExamSubmissionDocuments.Add(old);
        }
        old.StoredFileName = Path.GetFileName(saved.Value.Path);
        old.OriginalFileName = saved.Value.Name;
        old.FileSize = file!.Length;
        old.UploadedByUserId = User.UserId();
        old.UploadedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        if (oldPath is not null && System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
        return Ok(new { attemptId, fileName = old.OriginalFileName, size = old.FileSize, uploadedAt = old.UploadedAt });
    }

    [HttpGet("attempts/{attemptId:guid}/info")]
    public async Task<IActionResult> StudentPdfInfo(Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        if (User.IsInRole("Student"))
        {
            if (attempt.StudentId != User.UserId() || !attempt.Exam.ResultsPublished || attempt.Status != "Graded") return NotFound();
        }
        else if (!CanManage(attempt.Exam)) return Forbid();
        var doc = await db.ExamSubmissionDocuments.FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        return doc is null ? NotFound() : Ok(new { fileName = doc.OriginalFileName, size = doc.FileSize, uploadedAt = doc.UploadedAt });
    }

    [HttpGet("attempts/{attemptId:guid}")]
    public async Task<IActionResult> DownloadStudentPdf(Guid attemptId, CancellationToken ct)
    {
        var attempt = await db.ExamAttempts.Include(x => x.Exam)
            .FirstOrDefaultAsync(x => x.Id == attemptId, ct);
        if (attempt is null) return NotFound();
        if (User.IsInRole("Student"))
        {
            if (attempt.StudentId != User.UserId() || !attempt.Exam.ResultsPublished) return NotFound();
        }
        else if (!CanManage(attempt.Exam)) return Forbid();

        var doc = await db.ExamSubmissionDocuments.FirstOrDefaultAsync(x => x.AttemptId == attemptId, ct);
        if (doc is null) return NotFound();
        var path = ResolveStoredFile(doc.StoredFileName);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf", doc.OriginalFileName);
    }
}
