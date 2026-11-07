using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;
using UglyToad.PdfPig;

namespace Mft.API.Controllers;

public sealed record AnswerKeyEntry(long? QuestionId, int? QuestionNumber, string Answer);
public sealed record AnswerKeyRequest(List<AnswerKeyEntry>? Entries, bool ReplaceExisting = true);

/// <summary>Imports an answer key from direct entry, JSON, XML, CSV, TXT, Excel or text-based PDF.</summary>
[ApiController, Route("api/canonical/exams/{examId:long}/answer-key"), Authorize(Policy = "TeacherOrAbove")]
public sealed class AnswerKeyController(CanonicalAcademicDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(long examId, CancellationToken ct)
    {
        var exam = await LoadExam(examId, ct);
        if (exam is null) return NotFound();
        var links = await GetExamQuestions(examId, ct);
        var rows = new List<object>();
        for (var index = 0; index < links.Count; index++)
        {
            var link = links[index];
            var question = await db.Questions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.QuestionId, ct);
            if (question is null) continue;
            var options = await db.MultipleChoiceOptions.AsNoTracking()
                .Where(x => x.QuestionId == question.Id).OrderBy(x => x.DisplayOrder)
                .Select(x => new { x.Id, x.OptionText, x.IsCorrect, x.DisplayOrder }).ToListAsync(ct);
            rows.Add(new { questionId = question.Id, questionNumber = index + 1,
                question = question.Text, answer = options.Where(x => x.IsCorrect).Select(x => x.OptionText).ToList(),
                options });
        }
        return Ok(new { examId, examTitle = exam.Title, entries = rows });
    }

    [HttpPut]
    public async Task<IActionResult> SaveDirect(long examId, AnswerKeyRequest request, CancellationToken ct)
    {
        var exam = await LoadExam(examId, ct);
        if (exam is null) return NotFound();
        return await ApplyEntries(examId, request.Entries ?? new(), request.ReplaceExisting, "direct", ct);
    }

    [HttpPost("import"), RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> Import(long examId, IFormFile? file, [FromForm] bool replaceExisting = true, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "یک فایل پاسخ‌نامه انتخاب کنید." });
        if (file.Length > 15_000_000) return BadRequest(new { message = "حداکثر حجم فایل ۱۵ مگابایت است." });
        var exam = await LoadExam(examId, ct);
        if (exam is null) return NotFound();
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        try
        {
            await using var stream = file.OpenReadStream();
            var entries = ext switch
            {
                ".xlsx" or ".xlsm" => ReadExcel(stream),
                ".csv" => ReadDelimited(await ReadText(stream, ct), ','),
                ".tsv" => ReadDelimited(await ReadText(stream, ct), '\t'),
                ".json" => ReadJson(await ReadText(stream, ct)),
                ".xml" => ReadXml(await ReadText(stream, ct)),
                ".txt" => ReadTextKey(await ReadText(stream, ct)),
                ".pdf" => ReadPdf(stream),
                _ => throw new InvalidDataException("فرمت پشتیبانی نمی‌شود. فرمت‌های مجاز: PDF، XLSX، XLSM، CSV، TSV، JSON، XML و TXT.")
            };
            if (entries.Count == 0) return BadRequest(new { message = "هیچ پاسخ معتبری در فایل پیدا نشد." });
            return await ApplyEntries(examId, entries, replaceExisting, ext.TrimStart('.'), ct);
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or FormatException or IOException or XmlException or InvalidOperationException)
        {
            return BadRequest(new { message = $"خواندن پاسخ‌نامه ناموفق بود: {ex.Message}" });
        }
        catch (Exception) when (ext == ".pdf")
        {
            return BadRequest(new { message = "PDF قابل خواندن نیست یا اسکن‌شده است. PDF باید متن قابل انتخاب داشته باشد؛ برای PDF اسکن‌شده ابتدا OCR انجام دهید." });
        }
    }

    private long? CurrentUserId =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId =>
        long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    private async Task<CanonicalExam?> LoadExam(long examId, CancellationToken ct)
    {
        var exam = await db.Exams.AsNoTracking().FirstOrDefaultAsync(x => x.Id == examId, ct);
        if (exam is null) return null;
        if (User.IsInRole("Department") && exam.DepartmentId != CurrentDepartmentId) return null;
        if (User.IsInRole("Teacher") && exam.CreatedByUserId != CurrentUserId) return null;
        return exam;
    }

    private async Task<List<CanonicalExamQuestion>> GetExamQuestions(long examId, CancellationToken ct)
    {
        var sectionIds = await db.ExamSections.AsNoTracking()
            .Where(x => x.ExamId == examId).Select(x => x.Id).ToListAsync(ct);
        return await db.ExamQuestions.AsNoTracking()
            .Where(x => sectionIds.Contains(x.ExamSectionId))
            .OrderBy(x => x.DisplayOrder).ToListAsync(ct);
    }

    private async Task<IActionResult> ApplyEntries(long examId, List<AnswerKeyEntry> entries,
        bool replaceExisting, string source, CancellationToken ct)
    {
        if (entries.Count > 5000) return BadRequest(new { message = "حداکثر ۵۰۰۰ پاسخ در هر بار قابل ثبت است." });
        var links = await GetExamQuestions(examId, ct);
        if (links.Count == 0) return BadRequest(new { message = "ابتدا سؤال‌ها را به آزمون اضافه کنید." });
        var questionIds = links.Select(x => x.QuestionId).Distinct().ToArray();
        var questions = await db.Questions.Where(x => questionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var options = await db.MultipleChoiceOptions.Where(x => questionIds.Contains(x.QuestionId))
            .OrderBy(x => x.DisplayOrder).ToListAsync(ct);
        var types = await db.QuestionTypes.ToDictionaryAsync(x => x.Id, x => x.Code, ct);
        var byId = links.GroupBy(x => x.QuestionId).ToDictionary(x => x.Key, x => x.First());
        var byNumber = links.Select((link, index) => new { Number = index + 1, Link = link }).ToDictionary(x => x.Number, x => x.Link);
        var seen = new HashSet<long>();
        var updated = 0;
        var errors = new List<string>();
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Answer)) { errors.Add("پاسخ خالی نادیده گرفته شد."); continue; }
            CanonicalExamQuestion? link = null;
            if (entry.QuestionId.HasValue) byId.TryGetValue(entry.QuestionId.Value, out link);
            else if (entry.QuestionNumber.HasValue) byNumber.TryGetValue(entry.QuestionNumber.Value, out link);
            if (link is null) { errors.Add($"شماره سؤال/شناسه نامعتبر: {entry.QuestionNumber?.ToString() ?? entry.QuestionId?.ToString() ?? "نامشخص"}"); continue; }
            if (!seen.Add(link.QuestionId)) errors.Add($"پاسخ تکراری برای سؤال شماره {link.DisplayOrder}؛ آخرین پاسخ اعمال شد.");
            if (!questions.TryGetValue(link.QuestionId, out var question)) { errors.Add($"سؤال {link.DisplayOrder} پیدا نشد."); continue; }
            if (!types.TryGetValue(question.QuestionTypeId, out var typeCode) || typeCode != "MultipleChoice")
            { errors.Add($"ثبت کلید پاسخ خودکار برای سؤال {link.DisplayOrder} فقط برای سؤال چندگزینه‌ای پشتیبانی می‌شود."); continue; }

            var questionOptions = options.Where(x => x.QuestionId == question.Id).OrderBy(x => x.DisplayOrder).ToList();
            if (questionOptions.Count == 0) { errors.Add($"سؤال {link.DisplayOrder} گزینه‌ای ندارد."); continue; }
            var answer = entry.Answer.Trim();
            var matching = questionOptions.FirstOrDefault(x => string.Equals(x.OptionText.Trim(), answer, StringComparison.OrdinalIgnoreCase));
            if (matching is null)
            {
                var letterIndex = answer.ToUpperInvariant() switch { "A" or "آ" => 0, "B" or "ب" => 1, "C" or "ج" => 2, "D" or "د" => 3, "E" or "ه" => 4, _ => -1 };
                if (letterIndex >= 0 && letterIndex < questionOptions.Count) matching = questionOptions[letterIndex];
            }
            if (matching is null) { errors.Add($"پاسخ سؤال {link.DisplayOrder} با گزینه‌های ثبت‌شده مطابقت ندارد."); continue; }
            if (!replaceExisting && questionOptions.Any(x => x.IsCorrect))
            { errors.Add($"پاسخ قبلی سؤال {link.DisplayOrder} حفظ شد."); continue; }
            foreach (var option in questionOptions) option.IsCorrect = option.Id == matching.Id;
            updated++;
        }
        if (updated == 0) return BadRequest(new { message = "هیچ پاسخی ثبت نشد.", errors });
        await db.SaveChangesAsync(ct);
        return Ok(new { examId, source, updatedCount = updated, skippedCount = errors.Count, errors,
            message = $"پاسخ‌نامه ثبت شد؛ {updated} پاسخ به‌روزرسانی شد." });
    }

    private static async Task<string> ReadText(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    private static List<AnswerKeyEntry> ReadExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("کاربرگ Excel خالی است.");
        var rows = sheet.RangeUsed()?.RowsUsed().ToList() ?? new();
        var result = new List<AnswerKeyEntry>();
        foreach (var row in rows)
        {
            var first = row.Cell(1).GetString().Trim();
            var second = row.Cell(2).GetString().Trim();
            if (row.RowNumber() == 1 && (first.Contains("question", StringComparison.OrdinalIgnoreCase) || first.Contains("سوال") || first.Contains("شماره"))) continue;
            if (int.TryParse(first, out var number) && !string.IsNullOrWhiteSpace(second)) result.Add(new(null, number, second));
        }
        return result;
    }

    private static List<AnswerKeyEntry> ReadDelimited(string text, char delimiter)
    {
        var result = new List<AnswerKeyEntry>();
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(delimiter, 2);
            if (parts.Length < 2) continue;
            var left = parts[0].Trim().Trim('"', '\''); var right = parts[1].Trim().Trim('"', '\'');
            if (left.Contains("question", StringComparison.OrdinalIgnoreCase) || left.Contains("شماره") || left.Contains("سوال")) continue;
            if (int.TryParse(left, out var number) && right.Length > 0) result.Add(new(null, number, right));
        }
        return result;
    }

    private static List<AnswerKeyEntry> ReadJson(string text)
    {
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("entries", out var entries)) root = entries;
        if (root.ValueKind != JsonValueKind.Array) throw new InvalidDataException("JSON باید آرایه‌ای از پاسخ‌ها یا شیء دارای entries باشد.");
        var result = new List<AnswerKeyEntry>();
        foreach (var item in root.EnumerateArray())
        {
            long? id = TryLong(item, "questionId", "QuestionId", "id");
            int? number = TryInt(item, "questionNumber", "QuestionNumber", "number", "question", "شماره");
            var answer = TryString(item, "answer", "Answer", "correctAnswer", "پاسخ");
            if (!string.IsNullOrWhiteSpace(answer)) result.Add(new(id, number, answer));
        }
        return result;
    }
    private static int? TryInt(JsonElement item, params string[] names)
    {
        foreach (var property in item.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase)) continue;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var number)) return number;
            if (int.TryParse(property.Value.ToString(), out number)) return number;
        }
        return null;
    }

    private static long? TryLong(JsonElement item, params string[] names) { foreach (var p in item.EnumerateObject()) if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && long.TryParse(p.Value.ToString(), out var v)) return v; return null; }
    private static string? TryString(JsonElement item, params string[] names) { foreach (var p in item.EnumerateObject()) if (names.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) return p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString(); return null; }

    private static List<AnswerKeyEntry> ReadXml(string text)
    {
        var doc = XDocument.Parse(text);
        return doc.Descendants().Where(x => x.Name.LocalName.Equals("entry", StringComparison.OrdinalIgnoreCase) || x.Name.LocalName.Equals("answer", StringComparison.OrdinalIgnoreCase))
            .Select(x => { var idText = (string?)x.Attribute("questionId"); var numberText = (string?)x.Attribute("questionNumber") ?? (string?)x.Attribute("number"); var answer = (string?)x.Attribute("value") ?? x.Value; long.TryParse(idText, out var id); int.TryParse(numberText, out var number); return new AnswerKeyEntry(idText is not null && long.TryParse(idText, out _) ? id : null, numberText is not null && number > 0 ? number : null, answer.Trim()); })
            .Where(x => !string.IsNullOrWhiteSpace(x.Answer)).ToList();
    }

    private static List<AnswerKeyEntry> ReadTextKey(string text)
    {
        var result = new List<AnswerKeyEntry>();
        foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = Regex.Match(line.Trim(), @"^(?:سؤال\s*)?(\d+)\s*[-:،,.)]\s*(.+?)\s*$");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var number)) result.Add(new(null, number, match.Groups[2].Value.Trim()));
        }
        return result;
    }

    private static List<AnswerKeyEntry> ReadPdf(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        var text = string.Join("\n", document.GetPages().Select(page => page.Text));
        return ReadTextKey(text);
    }
}
