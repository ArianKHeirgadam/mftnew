using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
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


    [HttpPost("preview/word")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> PreviewWord(
        [FromForm] IFormFile? file,
        [FromForm] long? categoryId,
        [FromForm] long? skillId,
        [FromForm] string? difficulty,
        [FromForm] decimal? points,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل Word انتخاب نشده یا خالی است." });
        if (file.Length > MaxFileBytes)
            return BadRequest(new { message = "حداکثر حجم فایل ۵ مگابایت است." });
        if (!Path.GetExtension(file.FileName).Equals(".docx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "فقط فایل Word با پسوند .docx پذیرفته می‌شود؛ فایل‌های .doc پشتیبانی نمی‌شوند." });
        if (!categoryId.HasValue || categoryId.Value <= 0 || !skillId.HasValue || skillId.Value <= 0)
            return BadRequest(new { message = "دسته‌بندی و مهارت را از فهرست انتخاب کنید." });

        var selectedCategory = await academic.ExamCategories.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == categoryId.Value, ct);
        var selectedSkill = await academic.Skills.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == skillId.Value, ct);
        if (selectedCategory is null || selectedSkill is null ||
            selectedSkill.ExamCategoryId != selectedCategory.Id)
            return BadRequest(new { message = "مهارت انتخاب‌شده متعلق به دسته‌بندی انتخاب‌شده نیست یا دیگر وجود ندارد. فهرست را تازه‌سازی و دوباره انتخاب کنید." });

        try
        {
            await using var stream = file.OpenReadStream();
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            var documentEntry = archive.GetEntry("word/document.xml");
            if (documentEntry is null || documentEntry.Length == 0 || documentEntry.Length > 20 * 1024 * 1024)
                return BadRequest(new { message = "ساختار فایل Word معتبر نیست یا محتوای آن بیش از حد بزرگ است." });

            XDocument document;
            await using (var documentStream = documentEntry.Open())
            using (var reader = XmlReader.Create(documentStream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 20 * 1024 * 1024
            }))
            {
                document = XDocument.Load(reader);
            }

            var paragraphs = ReadWordParagraphs(document);
            // Use the canonical names loaded by ID from the database; never rely on
            // manually typed labels for Word imports.
            var rows = ParseWordQuestions(paragraphs, selectedCategory.Name, selectedSkill.Name,
                string.IsNullOrWhiteSpace(difficulty) ? "Medium" : difficulty.Trim(),
                points.GetValueOrDefault(1));
            return await BuildPreview(rows, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return BadRequest(new { message = "خواندن فایل Word ناموفق بود. ساختار سند را بررسی کنید.", detail = ex.Message });
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

    [HttpGet("batches")]
    public async Task<IActionResult> ListBatches(CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        var query = academic.QuestionImportBatches.AsNoTracking().AsQueryable();
        if (User.IsInRole("Teacher"))
            query = query.Where(x => x.TeacherUserId == CurrentUserId.Value);
        else if (User.IsInRole("Department"))
        {
            var userIds = await identity.Users.AsNoTracking()
                .Where(x => x.DepartmentId == CurrentDepartmentId)
                .Select(x => x.Id).ToListAsync(ct);
            query = query.Where(x => userIds.Contains(x.TeacherUserId));
        }

        var batches = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
            .Select(x => new { x.Id, x.Title, x.TeacherUserId, x.CreatedAt,
                QuestionCount = academic.Questions.Count(q => q.ImportBatchId == x.Id) })
            .ToListAsync(ct);
        var teacherIds = batches.Select(x => x.TeacherUserId).Distinct().ToArray();
        var teacherNames = await identity.Users.AsNoTracking()
            .Where(x => teacherIds.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName }).ToDictionaryAsync(x => x.Id, x => x.FullName, ct);
        return Ok(batches.Select(x => new {
            x.Id, x.Title, x.TeacherUserId, x.CreatedAt, x.QuestionCount,
            TeacherName = teacherNames.GetValueOrDefault(x.TeacherUserId, "استاد")
        }));
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromBody] ImportConfirmRequest request, CancellationToken ct)
    {
        if (CurrentUserId is null) return Unauthorized();
        var batchTitle = request.BatchTitle?.Trim();
        if (string.IsNullOrWhiteSpace(batchTitle) || batchTitle.Length > 200)
            return BadRequest(new { message = "عنوان آزمون برای پوشه‌بندی الزامی و حداکثر ۲۰۰ کاراکتر است." });
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
        var batch = await academic.QuestionImportBatches
            .FirstOrDefaultAsync(x => x.TeacherUserId == CurrentUserId.Value && x.Title == batchTitle, ct);
        if (batch is null)
        {
            batch = new CanonicalQuestionImportBatch
            {
                TeacherUserId = CurrentUserId.Value,
                Title = batchTitle
            };
            academic.QuestionImportBatches.Add(batch);
            await academic.SaveChangesAsync(ct);
        }

        var insertions = validation.ValidRows.Select(row => new
        {
            Row = row,
            Question = new CanonicalQuestion
            {
                SkillId = row.SkillId,
                ImportBatchId = batch.Id,
                QuestionTypeId = row.QuestionTypeId,
                Text = row.Text.Trim(),
                Points = row.Points,
                DifficultyLevel = row.DifficultyLevel,
                CreatedByUserId = CurrentUserId.Value
            }
        }).ToList();
        academic.Questions.AddRange(insertions.Select(x => x.Question));
        await academic.SaveChangesAsync(ct);

        foreach (var insertion in insertions)
        {
            var row = insertion.Row;
            var questionId = insertion.Question.Id;
            if (row.TypeCode == "MultipleChoice")
            {
                for (var i = 0; i < row.Options.Count; i++)
                    academic.MultipleChoiceOptions.Add(new CanonicalMultipleChoiceOption
                    {
                        QuestionId = questionId, OptionText = row.Options[i],
                        IsCorrect = i + 1 == row.CorrectOption, DisplayOrder = i + 1
                    });
            }
            else
            {
                for (var i = 0; i < row.MatchingPairs.Count; i++)
                    academic.MatchingPairs.Add(new CanonicalMatchingPair
                    {
                        QuestionId = questionId, LeftItem = row.MatchingPairs[i].LeftItem,
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
            validRows = result.PreviewRows.Count(x => x.Errors.Count == 0),
            invalidRows = result.PreviewRows.Count(x => x.Errors.Count > 0),
            canImport = result.PreviewRows.All(x => x.Errors.Count == 0),
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
            var normalizedSkillName = NormalizeTaxonomyName(skillName);
            var normalizedCategoryName = NormalizeTaxonomyName(categoryName);
            var skillMatches = skills.Where(s =>
                NormalizeTaxonomyName(s.Name) == normalizedSkillName);
            if (!string.IsNullOrWhiteSpace(categoryName))
            {
                var category = categories.FirstOrDefault(c =>
                    NormalizeTaxonomyName(c.Name) == normalizedCategoryName);
                skillMatches = category is null
                    ? Enumerable.Empty<CanonicalSkill>()
                    : skillMatches.Where(s => s.ExamCategoryId == category.Id);
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
                if (row.RequireFourOptions && options.Count != 4) errors.Add("سؤال Word باید دقیقاً چهار گزینه غیرخالی داشته باشد.");
                else if (options.Count is < 2 or > 4) errors.Add("سؤال چهارگزینه‌ای باید ۲ تا ۴ گزینه غیرخالی داشته باشد.");
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
            result.PreviewRows.Add(new ImportPreviewRow
            {
                RowNumber = row.RowNumber, Category = categoryName, Skill = skillName,
                Type = typeCode, Text = text, Points = row.Points, Difficulty = row.Difficulty,
                Options = options, CorrectOption = row.CorrectOption, MatchingPairs = pairs,
                RequireFourOptions = row.RequireFourOptions,
                Errors = errors
            });
            if (errors.Count > 0)
                result.Errors.Add(new { row.RowNumber, Errors = errors });
        }

        result.ValidRows = normalized;

        // Surface duplicates during preview, not only after the teacher presses Confirm.
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in normalized)
        {
            var key = $"{item.SkillId}|{Normalize(item.Text)}";
            if (!seenKeys.Add(key))
                AddPreviewError(result, item.RowNumber, "این سؤال در همین فایل بیش از یک بار تکرار شده است.");
        }

        var candidateSkillIds = normalized.Select(x => x.SkillId).Distinct().ToArray();
        if (candidateSkillIds.Length > 0)
        {
            var existingQuestions = await academic.Questions.AsNoTracking()
                .Where(q => candidateSkillIds.Contains(q.SkillId))
                .Select(q => new { q.SkillId, q.Text }).ToListAsync(ct);
            var existingKeys = existingQuestions
                .Select(x => $"{x.SkillId}|{Normalize(x.Text)}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in normalized)
                if (existingKeys.Contains($"{item.SkillId}|{Normalize(item.Text)}"))
                    AddPreviewError(result, item.RowNumber, "این سؤال قبلاً در بانک سؤال ثبت شده است.");
        }

        return result;
    }

    private static void AddPreviewError(ValidationResult result, int rowNumber, string message)
    {
        var preview = result.PreviewRows.FirstOrDefault(x => x.RowNumber == rowNumber);
        if (preview is null) return;
        preview.Errors.Add(message);
        result.Errors.Add(new { rowNumber, Errors = new[] { message } });
    }


    private static List<string> ReadWordParagraphs(XDocument document)
    {
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        return document.Descendants(word + "p")
            .Select(paragraph => string.Concat(paragraph.Descendants()
                .Where(element => element.Name == word + "t" || element.Name == word + "tab" || element.Name == word + "br")
                .Select(element => element.Name == word + "tab" ? " " : element.Name == word + "br" ? "\n" : element.Value)))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();
    }

    private static List<ImportQuestionRow> ParseWordQuestions(
        IReadOnlyList<string> paragraphs, string category, string skill, string difficulty, decimal points)
    {
        var rows = new List<ImportQuestionRow>();
        ImportQuestionRow? current = null;
        var correctCount = 0;

        void FinishCurrent()
        {
            if (current is null) return;
            if (correctCount != 1) current.CorrectOption = null;
            rows.Add(current);
            current = null;
            correctCount = 0;
        }

        foreach (var rawParagraph in paragraphs)
        {
            foreach (var rawLine in rawParagraph.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;

                var questionMatch = Regex.Match(line, @"^\s*[0-9۰-۹٠-٩]+\s*[.٫)\-ـ:]\s*(.+?)\s*$");
                if (questionMatch.Success)
                {
                    FinishCurrent();
                    current = new ImportQuestionRow
                    {
                        RowNumber = rows.Count + 1,
                        Category = category,
                        Skill = skill,
                        Type = "MultipleChoice",
                        RequireFourOptions = true,
                        Text = questionMatch.Groups[1].Value.Trim(),
                        Points = points,
                        Difficulty = difficulty
                    };
                    continue;
                }

                if (current is null) continue;

                var isCorrect = Regex.IsMatch(line, @"\(\s*صحیح\s*\)\s*$", RegexOptions.IgnoreCase);
                if (isCorrect)
                {
                    line = Regex.Replace(line, @"\s*\(\s*صحیح\s*\)\s*$", "").Trim();
                    correctCount++;
                }

                // Optional label support: "گزینه یک: متن گزینه". If no colon exists,
                // the whole line is treated as the option text (e.g. "گزینه یک").
                line = Regex.Replace(line,
                    @"^گزینه\s*(?:یک|اول|دوم|سوم|چهارم|1|2|3|4|۱|۲|۳|۴)\s*[:：]\s*",
                    "", RegexOptions.IgnoreCase).Trim();

                if (line.Length > 0)
                {
                    current.Options.Add(line);
                    if (isCorrect && correctCount == 1)
                        current.CorrectOption = current.Options.Count;
                    else if (isCorrect)
                        current.CorrectOption = 0;
                }
            }
        }

        FinishCurrent();
        return rows;
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
        var pointsText = get("points");
        if (string.IsNullOrWhiteSpace(pointsText)) row.Points = 1;
        else if (decimal.TryParse(pointsText, NumberStyles.Number, CultureInfo.InvariantCulture, out var points) ||
                 decimal.TryParse(pointsText, NumberStyles.Number, CultureInfo.CurrentCulture, out points)) row.Points = points;
        else row.Points = 0;
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

    // Persian/Arabic keyboard variants and invisible formatting characters were
    // causing valid taxonomy names to fail exact string matching.
    private static string NormalizeTaxonomyName(string? value)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormKC)
            .Replace('ي', 'ی')
            .Replace('ى', 'ی')
            .Replace('ك', 'ک')
            .Replace("\u0640", "")
            .Replace("\u200c", " ")
            .Replace("\u200f", "")
            .Replace("\u200e", "");

        normalized = Regex.Replace(normalized, @"[\\u064B-\\u065F\\u0670]", "");
        return string.Join(' ', normalized.Trim()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }

    private sealed class ValidationResult
    {
        public List<NormalizedQuestion> ValidRows { get; set; } = new();
        public List<object> Errors { get; } = new();
        public List<ImportPreviewRow> PreviewRows { get; } = new();
    }

    private sealed record NormalizedQuestion(int RowNumber, long SkillId, long QuestionTypeId, string TypeCode,
        string Text, decimal Points, byte DifficultyLevel, List<string> Options, int? CorrectOption,
        List<ImportMatchingPair> MatchingPairs);
}

public sealed class ImportPasteRequest { public string Content { get; set; } = ""; }
public sealed class ImportConfirmRequest { public string BatchTitle { get; set; } = ""; public List<ImportQuestionRow> Rows { get; set; } = new(); }
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
    public bool RequireFourOptions { get; set; }
    public List<ImportMatchingPair> MatchingPairs { get; set; } = new();
}
public sealed class ImportMatchingPair { public string LeftItem { get; set; } = ""; public string RightItem { get; set; } = ""; }

public sealed class ImportPreviewRow
{
    public int RowNumber { get; set; }
    public string Category { get; set; } = "";
    public string Skill { get; set; } = "";
    public string Type { get; set; } = "";
    public string Text { get; set; } = "";
    public decimal Points { get; set; }
    public string Difficulty { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int? CorrectOption { get; set; }
    public bool RequireFourOptions { get; set; }
    public List<ImportMatchingPair> MatchingPairs { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
