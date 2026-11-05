using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;
using UglyToad.PdfPig;

namespace Mft.API.Controllers;

[ApiController, Route("api/questions"), Authorize(Policy = "TeacherOrAbove")]
public sealed class QuestionPdfImportController(AppDbContext db, ILogger<QuestionPdfImportController> logger) : ControllerBase
{
    private static readonly Regex QuestionStart = new(@"^(?:Q(?:UESTION)?\s*\d+|س[ؤو]ال\s*[0-9۰-۹٠-٩]+)\s*[:.)\-]\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // PdfPig may return RTL text with the Persian question marker at the END of the line,
    // e.g. ": متن سؤال؟۱ سؤال" instead of "سؤال ۱: متن سؤال؟".
    private static readonly Regex TrailingPersianQuestion = new(@"^(?<body>.+?)\s*[:：؟.!،]?\s*(?<number>[0-9۰-۹٠-٩]+)\s*س[ؤو]ال\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex OptionLine = new(@"^(?:([A-E])|([الفبجد]))[).:\-]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnswerLine = new(@"^(?:ANSWER|پاسخ)\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PairLine = new(@"^(?:PAIR|جفت)\s*[:=]\s*(.+?)\s*(?:=>|->|→|=)\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [HttpPost("import-pdf/preview"), RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> PreviewPdf(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "فایل PDF را انتخاب کنید." });
        if (file.Length > 15_000_000) return BadRequest(new { message = "حداکثر حجم فایل ۱۵ مگابایت است." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل PDF پذیرفته می‌شود." });
        try
        {
            await using var stream = file.OpenReadStream();
            using var pdf = PdfDocument.Open(stream);
            var extractedText = string.Join("\n", pdf.GetPages().Select(page => page.Text));
            var blocks = Parse(NormalizeExtractedText(extractedText));
            if (blocks.Count == 0)
                logger.LogWarning("PDF preview extraction found no question blocks. File={FileName}, Pages={PageCount}, Characters={CharacterCount}, EnglishMarkers={EnglishMarkers}, PersianMarkers={PersianMarkers}",
                    Path.GetFileName(file.FileName), pdf.NumberOfPages, extractedText.Length,
                    Regex.Matches(extractedText, @"Q(?:UESTION)?\s*\d+\s*[:.)\-]", RegexOptions.IgnoreCase).Count,
                    Regex.Matches(extractedText, @"س[ؤو]ال\s*[0-9۰-۹٠-٩]+\s*[:.)\-]").Count);
            if (blocks.Count == 0)
                return BadRequest(new { message = "سؤالی پیدا نشد. ساختار PDF را مطابق راهنمای صفحه بارگذاری تنظیم کنید." });
            return Ok(new
            {
                count = blocks.Count,
                questions = blocks.Select((q, index) => new
                {
                    number = index + 1,
                    text = q.Text,
                    type = q.Type,
                    options = q.Options,
                    answerKeyAvailable = !string.IsNullOrWhiteSpace(q.CorrectAnswer)
                })
            });
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return BadRequest(new { message = "خواندن PDF ناموفق بود. فایل باید سالم و دارای متن قابل انتخاب باشد؛ PDF اسکن‌شده به OCR نیاز دارد." });
        }
    }

    [HttpPost("import-pdf"), RequestSizeLimit(15_000_000)]
    public async Task<IActionResult> ImportPdf(
        IFormFile? file,
        [FromForm] string? category,
        [FromForm] string difficulty = "Medium",
        [FromForm] decimal score = 1,
        [FromForm] Guid? departmentId = null,
        CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "فایل PDF را انتخاب کنید." });
        if (file.Length > 15_000_000) return BadRequest(new { message = "حداکثر حجم فایل ۱۵ مگابایت است." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل PDF پذیرفته می‌شود." });
        if (score <= 0 || difficulty is not ("Easy" or "Medium" or "Hard"))
            return BadRequest(new { message = "نمره یا سطح دشواری معتبر نیست." });

        var user = await db.Users.AsNoTracking()
            .Where(x => x.Id == User.UserId())
            .Select(x => new { x.DepartmentId })
            .SingleOrDefaultAsync(ct);
        if (user is null) return Unauthorized();

        var effectiveDepartment = User.IsInRole("SuperAdmin") ? departmentId : user.DepartmentId;
        if (!effectiveDepartment.HasValue ||
            !await db.Departments.AnyAsync(x => x.Id == effectiveDepartment.Value, ct))
            return BadRequest(new { message = "دپارتمان معتبر را انتخاب کنید." });

        List<ParsedQuestion> blocks;
        try
        {
            await using var stream = file.OpenReadStream();
            using var pdf = PdfDocument.Open(stream);
            var extractedText = string.Join("\n", pdf.GetPages().Select(page => page.Text));
            blocks = Parse(NormalizeExtractedText(extractedText));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return BadRequest(new { message = "خواندن PDF ناموفق بود. مطمئن شوید فایل سالم و دارای متن قابل انتخاب است. PDF اسکن‌شده به OCR نیاز دارد." });
        }

        if (blocks.Count == 0)
            return BadRequest(new { message = "سؤالی پیدا نشد. ساختار PDF را مطابق راهنمای صفحه بارگذاری تنظیم کنید." });

        var questions = blocks.Select(block => new Question
        {
            Text = block.Text,
            DepartmentId = effectiveDepartment.Value,
            Category = string.IsNullOrWhiteSpace(category) ? "عمومی" : category.Trim(),
            Type = block.Type,
            Difficulty = difficulty,
            OptionsJson = JsonSerializer.Serialize(block.Options),
            CorrectAnswer = block.CorrectAnswer,
            Score = score,
            CreatedByUserId = User.UserId()
        }).ToList();

        db.Questions.AddRange(questions);
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            message = $"{questions.Count} سؤال از PDF استخراج و به بانک سؤال اضافه شد.",
            imported = questions.Count,
            questions = questions.Select((q, index) => new
            {
                id = q.Id,
                number = index + 1,
                text = q.Text,
                type = q.Type,
                optionCount = blockOptionCount(q.OptionsJson)
            })
        });
    }

    private sealed record ParsedQuestion(string Text, string Type, string[] Options, string? CorrectAnswer);

    // PdfPig can return visually separate PDF lines joined into one text line depending
    // on the document's text layout. Reinsert line boundaries before import markers.
    private static string NormalizeExtractedText(string source)
    {
        var text = source.Replace("\r\n", "\n").Replace('\r', '\n');

        text = Regex.Replace(
            text,
            @"[ \t]+(?=(?:Q(?:UESTION)?\s*\d+|س[ؤو]ال\s*[0-9۰-۹٠-٩])\s*[:.)\-])",
            "\n",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(
            text,
            @"[ \t]+(?=(?:[A-E]|[الفبجد])[).:\-]\s*)",
            "\n",
            RegexOptions.IgnoreCase);

        text = Regex.Replace(
            text,
            @"[ \t]+(?=(?:ANSWER|TYPE|PAIR|پاسخ|نوع|جفت)\s*[:=])",
            "\n",
            RegexOptions.IgnoreCase);

        return text;
    }

    private static List<ParsedQuestion> Parse(string source)
    {
        var result = new List<ParsedQuestion>();
        List<string>? current = null;
        foreach (var raw in source.Replace("\r", "").Split('\n'))
        {
            var line = Regex.Replace(raw, @"\s+", " ").Trim();
            if (line.Length == 0) continue;

            // Normalize RTL-extracted Persian question markers before detecting question boundaries.
            var trailingMarker = TrailingPersianQuestion.Match(line);
            if (trailingMarker.Success)
            {
                var body = trailingMarker.Groups["body"].Value.Trim().TrimStart(':', '：', '؟');
                var number = trailingMarker.Groups["number"].Value;
                line = $"سؤال {number}: {body}";
            }

            var start = QuestionStart.Match(line);
            if (start.Success)
            {
                if (current is not null) AddBlock(result, current);
                current = new List<string>();
                if (!string.IsNullOrWhiteSpace(start.Groups[1].Value)) current.Add(start.Groups[1].Value.Trim());
                continue;
            }
            if (current is not null) current.Add(line);
        }
        if (current is not null) AddBlock(result, current);
        return result;
    }

    private static void AddBlock(List<ParsedQuestion> result, List<string> lines)
    {
        var text = new List<string>();
        var options = new List<string>();
        var pairs = new List<(string Left, string Right)>();
        string? answer = null;
        var declaredType = "";

        foreach (var line in lines)
        {
            var typeMatch = Regex.Match(line, @"^(?:TYPE|نوع)\s*[:=]\s*(.+)$", RegexOptions.IgnoreCase);
            if (typeMatch.Success) { declaredType = typeMatch.Groups[1].Value.Trim().ToUpperInvariant(); continue; }
            var pair = PairLine.Match(line);
            if (pair.Success) { pairs.Add((pair.Groups[1].Value.Trim(), pair.Groups[2].Value.Trim())); continue; }
            var option = OptionLine.Match(line);
            if (option.Success) { options.Add(option.Groups[3].Value.Trim()); continue; }
            var answerMatch = AnswerLine.Match(line);
            if (answerMatch.Success) { answer = answerMatch.Groups[1].Value.Trim(); continue; }
            text.Add(line);
        }

        var questionText = string.Join(" ", text).Trim();
        if (questionText.Length == 0) return;
        if (pairs.Count >= 2 || declaredType.Contains("MATCH") || declaredType.Contains("وصل"))
        {
            if (pairs.Count < 2) return;
            var left = pairs.Select(x => x.Left).ToArray();
            var right = pairs.Select(x => x.Right).ToArray();
            if (left.Distinct(StringComparer.OrdinalIgnoreCase).Count() != left.Length ||
                right.Distinct(StringComparer.OrdinalIgnoreCase).Count() != right.Length) return;
            result.Add(new ParsedQuestion(questionText, "Matching", left,
                JsonSerializer.Serialize(pairs.ToDictionary(x => x.Left, x => x.Right))));
            return;
        }

        if (options.Count >= 2 || declaredType.Contains("MCQ") || declaredType.Contains("چهار"))
        {
            if (options.Count < 2) return;
            string? correct = null;
            if (!string.IsNullOrWhiteSpace(answer))
            {
                var key = answer.ToUpperInvariant();
                var index = key switch { "A" or "آ" or "الف" => 0, "B" or "ب" => 1, "C" or "ج" => 2, "D" or "د" => 3, "E" or "ه" => 4, _ => -1 };
                correct = index >= 0 && index < options.Count ? options[index] :
                    options.FirstOrDefault(x => string.Equals(x, answer, StringComparison.OrdinalIgnoreCase));
            }
            result.Add(new ParsedQuestion(questionText, "MultipleChoice", options.ToArray(), correct));
            return;
        }

        result.Add(new ParsedQuestion(questionText, "Essay", Array.Empty<string>(), null));
    }

    private static int blockOptionCount(string? json)
    {
        try { return string.IsNullOrWhiteSpace(json) ? 0 : JsonSerializer.Deserialize<string[]>(json)?.Length ?? 0; }
        catch (JsonException) { return 0; }
    }
}
