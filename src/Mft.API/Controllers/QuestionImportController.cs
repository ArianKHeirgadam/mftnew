using System.Globalization;
using System.Security.Claims;
using System.Text;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Infrastructure.Persistence;

namespace Mft.API.Controllers;

[ApiController]
[Route("api/questions/import")]
[Authorize(Policy = "TeacherOrAbove")]
public sealed class QuestionImportController(
    CanonicalAcademicDbContext academic,
    CanonicalIdentityDbContext identity) : ControllerBase
{
    private const int MaxRows = 500;
    private const long MaxFileBytes = 5 * 1024 * 1024;
    private long? CurrentUserId => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private long? CurrentDepartmentId => long.TryParse(User.FindFirstValue("departmentId"), out var id) ? id : null;

    [HttpPost("preview/file")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> PreviewFile([FromForm] IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "فایل انتخاب نشده یا خالی است." });
        if (file.Length > MaxFileBytes) return BadRequest(new { message = "حداکثر حجم فایل ۵ مگابایت است." });
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not ".xlsx" and not ".csv") return BadRequest(new { message = "فقط فایل Excel با پسوند xlsx یا CSV پذیرفته می‌شود." });

        try
        {
            List<ImportQuestionRow> rows;
            await using var stream = file.OpenReadStream();
            if (extension == ".xlsx")
            {
                using var workbook = new XLWorkbook(stream);
                var sheet = workbook.Worksheets.FirstOrDefault();
                if (sheet is null) return BadRequest(new { message = "فایل Excel هیچ برگه‌ای ندارد." });
                rows = ReadWorksheet(sheet);
            }
            else
            {
                using var reader = new StreamReader(stream, Encoding.UTF8, true);
                rows = ReadDelimited(await reader.ReadToEndAsync(ct));
            }
            return await BuildPreview(rows, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { message = "خواندن فایل ناموفق بود. قالب فایل را بررسی کنید.", detail = ex.Message });
        }
    }

    [HttpPost("preview/paste")]
    public async Task<IActionResult> PreviewPaste([FromBody] ImportPasteRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(new { message = "محتوای جدول خالی است." });
        try { return await BuildPreview(ReadDelimited(request.Content), ct); }
        catch (Exception ex) { return BadRequest(new { message = "قالب داده‌های Paste شده قابل خواندن نیست.", detail = ex.Message }); }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ImportConfirmRequest request, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        if (request.Rows is null || request.Rows.Count == 0 || request.Rows.Count > MaxRows)
            return BadRequest(new { message = $"تعداد ردیف‌ها باید بین ۱ تا {MaxRows} باشد." });

        var validation = await ValidateRows(request.Rows, ct);
        if (validation.Errors.Count > 0)
            return Conflict(new { message = "اطلاعات قبل از ثبت تغییر کرده یا دارای خطاست؛ دوباره پیش‌نمایش بگیرید.", errors = validation.Errors });

        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in validation.ValidRows)
        {
            var key = $"{row.SkillId}|{Normalize(row.Text)}";
            if (!duplicateKeys.Add(key))
                return Conflict(new { message = $"سؤال تکراری در ردیف {row.RowNumber} وجود دارد." });
        }

        var skillIds = validation.ValidRows.Select(x => x.SkillId).Distinct().ToArray();
        var existing = await academic.Questions.AsNoTracking()
            .Where(q => skillIds.Contains(q.SkillId))
            .Select(q => new { q.SkillId, q.Text }).ToListAsync(ct);
        var existingKeys = existing.Select(x => $"{x.SkillId}|{Normalize(x.Text)}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingDuplicate = validation.ValidRows.FirstOrDefault(x => existingKeys.Contains($"{x.SkillId}|{Normalize(x.Text)}"));
        if (existingDuplicate is not null)
            return Conflict(new { message = $"سؤال ردیف {existingDuplicate.RowNumber} قبلاً در بانک سؤال ثبت شده است." });

        await using var transaction = await academic.Database.BeginTransactionAsync(ct);
        foreach (var row in validation.ValidRows)
        {
            var question = new CanonicalQuestion
            {
                SkillId = row.SkillId,
                QuestionTypeId = row.QuestionTypeId,
                Text = row.Text.Trim(),
                Points = row.Points,
                DifficultyLevel = row.DifficultyLevel,
                CreatedByUserId = CurrentUserId.Value
            };
            academic.Questions.Add(question);
            await academic.SaveChangesAsync(ct);
            if (row.TypeCode == "MultipleChoice")
            {
                for (var i = 0; i < row.Options.Count; i++)
                    academic.MultipleChoiceOptions.Add(new CanonicalMultipleChoiceOption
                    {
                        QuestionId = question.Id, OptionText = row.Options[i],
                        IsCorrect = i + 1 == row.CorrectOption, DisplayOrder = i + 1
                    });
            }
            else
            {
                for (var i = 0; i < row.MatchingPairs.Count; i++)
                    academic.MatchingPairs.Add(new CanonicalMatchingPair
                    {
                        QuestionId = question.Id, LeftItem = row.MatchingPairs[i].LeftItem,
                        RightItem = row.MatchingPairs[i].RightItem, DisplayOrder = i + 1
                    });
            }
        }
        await academic.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(new { importedCount = validation.ValidRows.Count, message = $"{validation.ValidRows.Count} سؤال با موفقیت ثبت شد." });
    }

    private async Task<IActionResult> BuildPreview(List<ImportQuestionRow> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return BadRequest(new { message = "هیچ ردیف سؤالی پیدا نشد." });
        if (rows.Count > MaxRows) return BadRequest(new { message = $"حداکثر {MaxRows} سؤال را در هر نوبت وارد کنید." });
        var result = await ValidateRows(rows, ct);
        return Ok(new
        {
            totalRows = rows.Count,
            validRows = result.ValidRows.Count,
            invalidRows = result.Errors.Count,
            canImport = result.Errors.Count == 0,
            rows = result.PreviewRows
        });
    }

    private async Task<ValidationResult> ValidateRows(List<ImportQuestionRow> rows, CancellationToken ct)
    {
        var result = new ValidationResult();
        var categories = await academic.ExamCategories.AsNoTracking().ToListAsync(ct);
        var skills = await academic.Skills.AsNoTracking().ToListAsync(ct);
        var types = await academic.QuestionTypes.AsNoTracking().ToListAsync(ct);
        var normalized = new List<NormalizedQuestion>();

        foreach (var row in rows)
        {
            var errors = new List<string>();
            var text = row.Text?.Trim() ?? "";
            var skillName = row.Skill?.Trim() ?? "";
            var categoryName = row.Category?.Trim() ?? "";
            var typeCode = NormalizeType(row.Type);
            var skillMatches = skills.Where(s => string.Equals(s.Name.Trim(), skillName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var category = categories.FirstOrDefault(c => string.Equals(c.Name.Trim(), categoryName, StringComparison.OrdinalIgnoreCase));
                skillMatches = category is null ? Enumerable.Empty<CanonicalSkill>() : skillMatches.Where(s => s.ExamCategoryId == category.Id);
            }
            var matchedSkills = skillMatches.Take(2).ToList();
            if (matchedSkills.Count != 1) errors.Add(matchedSkills.Count == 0 ? "مهارت پیدا نشد؛ نام مهارت را دقیق وارد کنید." : "نام مهارت تکراری است؛ ستون Category را هم تکمیل کنید.");
            var skill = matchedSkills.FirstOrDefault();

            var type = types.FirstOrDefault(t => string.Equals(t.Code, typeCode, StringComparison.OrdinalIgnoreCase));
            if (type is null || typeCode is not ("MultipleChoice" or "Matching"))
                errors.Add("نوع سؤال فقط باید MultipleChoice یا Matching باشد.");
            if (string.IsNullOrWhiteSpace(text)) errors.Add("متن سؤال خالی است.");
            if (text.Length > 4000) errors.Add("متن سؤال بیش از ۴۰۰۰ کاراکتر است.");
            if (row.Points <= 0 || row.Points > 9999) errors.Add("بارم باید بیشتر از صفر و حداکثر ۹۹۹۹ باشد.");
            var difficulty = ParseDifficulty(row.Difficulty);
            if (difficulty is null) errors.Add("سطح دشواری باید Easy، Medium یا Hard باشد.");

            var options = (row.Options ?? new List<string>()).Select(x => x?.Trim() ?? "").ToList();
            var pairs = row.MatchingPairs ?? new List<ImportMatchingPair>();
            if (typeCode == "MultipleChoice")
            {
                options = options.Where(x => x.Length > 0).ToList();
                if (options.Count is < 2 or > 4) errors.Add("سؤال چهارگزینه‌ای باید ۲ تا ۴ گزینه غیرخالی داشته باشد.");
                if (row.CorrectOption is null || row.CorrectOption < 1 || row.CorrectOption > options.Count)
                    errors.Add("CorrectOption باید شماره یکی از گزینه‌های غیرخالی (از ۱ تا تعداد گزینه‌ها) باشد.");
            }
            else if (typeCode == "Matching")
            {
                pairs = pairs.Where(p => !string.IsNullOrWhiteSpace(p.LeftItem) || !string.IsNullOrWhiteSpace(p.RightItem)).ToList();
                if (pairs.Count is < 2 or > 10 || pairs.Any(p => string.IsNullOrWhiteSpace(p.LeftItem) || string.IsNullOrWhiteSpace(p.RightItem)))
                    errors.Add("Matching باید بین ۲ تا ۱۰ جفت کامل داشته باشد.");
                if (pairs.Select(p => Normalize(p.LeftItem)).Distinct().Count() != pairs.Count ||
                    pairs.Select(p => Normalize(p.RightItem)).Distinct().Count() != pairs.Count)
                    errors.Add("عبارت‌های دو ستون Matching نباید تکراری باشند.");
            }

            if (errors.Count == 0 && skill is not null && type is not null)
            {
                normalized.Add(new NormalizedQuestion(row.RowNumber, skill.Id, type.Id, typeCode, text,
                    row.Points, difficulty!.Value, options, row.CorrectOption, pairs));
            }
            result.PreviewRows.Add(new
            {
                row.RowNumber, Category = categoryName, Skill = skillName,
                Type = typeCode, Text = text, row.Points, Difficulty = row.Difficulty,
                Options = options, row.CorrectOption, MatchingPairs = pairs,
                Errors = errors
            });
            if (errors.Count > 0)
                result.Errors.Add(new { row.RowNumber, Errors = errors });
        }

        result.ValidRows = normalized;
        return result;
    }

    private static List<ImportQuestionRow> ReadWorksheet(IXLWorksheet sheet)
    {
        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null) return new();
        var headers = headerRow.CellsUsed().ToDictionary(c => NormalizeHeader(c.GetString()), c => c.Address.ColumnNumber);
        return sheet.RowsUsed().Where(r => r.RowNumber() > headerRow.RowNumber())
            .Select(r => BuildRow(headers, key => headers.TryGetValue(key, out var column) ? r.Cell(column).GetFormattedString() : "", r.RowNumber()))
            .Where(HasContent).ToList();
    }

    private static List<ImportQuestionRow> ReadDelimited(string content)
    {
        var records = ParseDelimited(content);
        if (records.Count < 2) return new();
        var headers = records[0].Select((v, i) => new { Key = NormalizeHeader(v), Index = i }).Where(x => x.Key.Length > 0)
            .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.First().Index);
        return records.Skip(1).Select((cells, i) => BuildRow(headers, key => headers.TryGetValue(key, out var index) && index < cells.Count ? cells[index] : "", i + 2))
            .Where(HasContent).ToList();
    }

    private static ImportQuestionRow BuildRow(IReadOnlyDictionary<string, int> headers, Func<string, string> get, int rowNumber)
    {
        var row = new ImportQuestionRow { RowNumber = rowNumber, Category = get("category"), Skill = get("skill"), Type = get("type"), Text = get("text") };
        if (decimal.TryParse(get("points"), NumberStyles.Number, CultureInfo.InvariantCulture, out var points) ||
            decimal.TryParse(get("points"), NumberStyles.Number, CultureInfo.CurrentCulture, out points)) row.Points = points;
        if (string.IsNullOrWhiteSpace(get("points"))) row.Points = 1;
        row.Difficulty = string.IsNullOrWhiteSpace(get("difficulty")) ? "Medium" : get("difficulty");
        for (var i = 1; i <= 4; i++) { var option = get($"option{i}"); if (!string.IsNullOrWhiteSpace(option)) row.Options.Add(option); }
        if (int.TryParse(get("correctoption"), out var correct)) row.CorrectOption = correct;
        for (var i = 1; i <= 10; i++)
        {
            var left = get($"left{i}"); var right = get($"right{i}");
            if (!string.IsNullOrWhiteSpace(left) || !string.IsNullOrWhiteSpace(right))
                row.MatchingPairs.Add(new ImportMatchingPair { LeftItem = left, RightItem = right });
        }
        return row;
    }

    private static bool HasContent(ImportQuestionRow row) =>
        !string.IsNullOrWhiteSpace(row.Text) || !string.IsNullOrWhiteSpace(row.Skill);

    private static List<List<string>> ParseDelimited(string text)
    {
        var delimiter = text.Contains('\t') ? '\t' : ',';
        var records = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && ch == delimiter) { row.Add(cell.ToString().Trim()); cell.Clear(); }
            else if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString().Trim()); cell.Clear();
                if (row.Any(x => x.Length > 0)) records.Add(row);
                row = new List<string>();
            }
            else cell.Append(ch);
        }
        row.Add(cell.ToString().Trim());
        if (row.Any(x => x.Length > 0)) records.Add(row);
        return records;
    }

    private static string NormalizeHeader(string value)
    {
        var normalized = new string((value ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized switch
        {
            "categoryname" => "category", "skillname" => "skill", "questiontype" => "type",
            "question" or "questiontext" => "text", "score" or "mark" => "points",
            "level" => "difficulty", "correct" or "correctanswer" => "correctoption",
            _ => normalized
        };
    }

    private static string NormalizeType(string? value)
    {
        var type = (value ?? "").Trim();
        if (type.Equals("MCQ", StringComparison.OrdinalIgnoreCase) || type == "چهارگزینه‌ای") return "MultipleChoice";
        if (type == "وصل‌کردنی") return "Matching";
        return type;
    }

    private static byte? ParseDifficulty(string? value) => (value ?? "").Trim().ToLowerInvariant() switch
    {
        "easy" or "1" or "آسان" => 1,
        "medium" or "2" or "متوسط" => 2,
        "hard" or "3" or "سخت" => 3,
        _ => null
    };

    private static string Normalize(string value) =>
        string.Join(' ', (value ?? "").Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

    private sealed class ValidationResult
    {
        public List<NormalizedQuestion> ValidRows { get; set; } = new();
        public List<object> Errors { get; } = new();
        public List<object> PreviewRows { get; } = new();
    }

    private sealed record NormalizedQuestion(int RowNumber, long SkillId, long QuestionTypeId, string TypeCode,
        string Text, decimal Points, byte DifficultyLevel, List<string> Options, int? CorrectOption,
        List<ImportMatchingPair> MatchingPairs);
}

public sealed class ImportPasteRequest { public string Content { get; set; } = ""; }
public sealed class ImportConfirmRequest { public List<ImportQuestionRow> Rows { get; set; } = new(); }
public sealed class ImportQuestionRow
{
    public int RowNumber { get; set; }
    public string Category { get; set; } = "";
    public string Skill { get; set; } = "";
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public decimal Points { get; set; } = 1;
    public string Difficulty { get; set; } = "Medium";
    public List<string> Options { get; set; } = new();
    public int? CorrectOption { get; set; }
    public List<ImportMatchingPair> MatchingPairs { get; set; } = new();
}
public sealed class ImportMatchingPair { public string LeftItem { get; set; } = ""; public string RightItem { get; set; } = ""; }
