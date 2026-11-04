using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mft.Application.Abstractions;
using Mft.Application.Contracts;
using Mft.Domain.Entities;
using Mft.Infrastructure.Persistence;
namespace Mft.API.Controllers;
/// <summary>Handles authentication and the current user password lifecycle.</summary>
[ApiController, Route("api/auth"), Authorize]
public class AuthController(IAuthService auth, AppDbContext db) : ControllerBase
{
    [HttpPost("login"), AllowAnonymous]
    public async Task<IActionResult> Login(LoginRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Username) || string.IsNullOrWhiteSpace(r.Password)) return BadRequest(new {
            message = "نام کاربری و رمز عبور الزامی است."
        }
        );
        var result = await auth.LoginAsync(r, ct);
        return result is null?Unauthorized(new{
            message = "نام کاربری یا رمز عبور نادرست است."
        }
        ):Ok(result);
    }
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct){
        var id = User.UserId();
        var u = await db.Users.Include(x => x.Department).FirstOrDefaultAsync(x => x.Id == id, ct);
        return u is null?NotFound():Ok(new{
            id = u.Id, username = u.Username, fullName = u.FullName, role = u.Role.ToString(), email = u.Email, mobile = u.Mobile, departmentId = u.DepartmentId, department = u.Department?.Name, mustChangePassword = u.MustChangePassword
        }
        );
    }
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest r, CancellationToken ct){
        var u = await db.Users.FindAsync(new object?[]{
            User.UserId()
        }
        ,  ct);
        if (u is null) return Unauthorized();
        if (!BCrypt.Net.BCrypt.Verify(r.CurrentPassword, u.PasswordHash)) return BadRequest(new {
            message = "رمز فعلی صحیح نیست."
        }
        );
        if (string.IsNullOrWhiteSpace(r.NewPassword) || r.NewPassword.Length < 8) return BadRequest(new {
            message = "رمز جدید باید حداقل ۸ کاراکتر باشد."
        }
        );
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.NewPassword);
        u.MustChangePassword = false;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/users"), Authorize]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> List([FromQuery]string? role, [FromQuery]string? search, CancellationToken ct){
        var q = db.Users.Include(x => x.Department).AsQueryable();
        if (User.IsInRole("Department"))q = q.Where(x => x.DepartmentId == User.DepartmentId());
        if (User.IsInRole("Teacher"))q = q.Where(x => x.Role == UserRole.Student && x.DepartmentId == User.DepartmentId());
        if (!string.IsNullOrWhiteSpace(role)){
            if (!Enum.TryParse<UserRole>(role, true, out var rr)) return BadRequest(new {
                message = "نقش نامعتبر است."
            }
            );
            q = q.Where(x => x.Role == rr);
        }
        if (!string.IsNullOrWhiteSpace(search))q = q.Where(x => x.FullName.Contains(search) || x.Username.Contains(search));
        return Ok(await q.OrderBy(x => x.FullName).Select(x => new{
            id = x.Id, username = x.Username, fullName = x.FullName, role = x.Role.ToString(), email = x.Email, mobile = x.Mobile, departmentId = x.DepartmentId, department = x.Department == null?null:x.Department.Name, isActive = x.IsActive, createdAt = x.CreatedAt
        }
        ).ToListAsync(ct));
    }
    [HttpGet("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var x = await db.Users.Include(x => x.Department).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (x is null) return NotFound();
        if (User.IsInRole("Department") && x.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && (x.Role != UserRole.Student || x.DepartmentId != User.DepartmentId())) return Forbid();
        return Ok(new {
            id = x.Id, username = x.Username, fullName = x.FullName, role = x.Role.ToString(), email = x.Email, mobile = x.Mobile, departmentId = x.DepartmentId, department = x.Department?.Name, isActive = x.IsActive, createdAt = x.CreatedAt
        }
        );
    }
    [HttpPost, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Create(CreateUserRequest r, CancellationToken ct){
        if (!Enum.TryParse<UserRole>(r.Role, true, out var role)) return BadRequest(new {
            message = "نقش نامعتبر است."
        }
        );
        if (role == UserRole.SuperAdmin && !User.IsInRole("SuperAdmin")) return Forbid();
        if (User.IsInRole("Teacher") && role != UserRole.Student) return Forbid();
        if (User.IsInRole("Department") && role is not(UserRole.Student or UserRole.Teacher)) return Forbid();
        var normalizedUsername = r.Username?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedUsername) || string.IsNullOrWhiteSpace(r.FullName)) return BadRequest(new {
            message = "نام کاربری و نام کامل الزامی است."
        });
        var password = r.Password?.Trim();
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return BadRequest(new { message = "رمز عبور باید حداقل ۸ کاراکتر باشد." });
        if (r.DepartmentId.HasValue && !await db.Departments.AnyAsync(d => d.Id == r.DepartmentId.Value, ct)) return BadRequest(new {
            message = "دپارتمان انتخاب‌شده معتبر نیست."
        }
        );
        if (await db.Users.AnyAsync(x => x.Username == normalizedUsername, ct)) return Conflict(new {
            message = "این نام کاربری قبلاً ثبت شده است."
        }
        );
        var u = new User{
            Username = normalizedUsername, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password), FullName = r.FullName.Trim(), Role = role, Email = r.Email, Mobile = r.Mobile, DepartmentId = User.IsInRole("Department") || User.IsInRole("Teacher")?User.DepartmentId():r.DepartmentId, MustChangePassword = false
        }
        ;
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new {
            id = u.Id
        }
        ,  new{
            id = u.Id, username = u.Username, fullName = u.FullName, role = u.Role.ToString()
        });
    }
    [HttpPut("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest r, CancellationToken ct){
        var u = await db.Users.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (u is null) return NotFound();
        if (string.IsNullOrWhiteSpace(r.FullName)) return BadRequest(new {
            message = "نام کامل الزامی است."
        }
        );
        if (User.IsInRole("SuperAdmin") && r.DepartmentId.HasValue && !await db.Departments.AnyAsync(d => d.Id == r.DepartmentId.Value, ct)) return BadRequest(new {
            message = "دپارتمان انتخاب‌شده معتبر نیست."
        }
        );
        if (User.IsInRole("Department") && (u.DepartmentId != User.DepartmentId() || u.Role is not (UserRole.Student or UserRole.Teacher))) return Forbid();
        if (User.IsInRole("Teacher") && (u.Role != UserRole.Student || u.DepartmentId != User.DepartmentId())) return Forbid();
        if (u.Role == UserRole.SuperAdmin && !User.IsInRole("SuperAdmin")) return Forbid();
        u.FullName = r.FullName;
        u.Email = r.Email;
        u.Mobile = r.Mobile;
        u.IsActive = r.IsActive;
        if (User.IsInRole("SuperAdmin")) u.DepartmentId = r.DepartmentId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpDelete("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var u = await db.Users.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (u is null) return NotFound();
        if (u.Id == User.UserId() || u.Role == UserRole.SuperAdmin && !User.IsInRole("SuperAdmin")) return Forbid();
        if (User.IsInRole("Department") && (u.DepartmentId != User.DepartmentId() || u.Role is not (UserRole.Student or UserRole.Teacher))) return Forbid();
        if (User.IsInRole("Teacher") && (u.Role != UserRole.Student || u.DepartmentId != User.DepartmentId())) return Forbid();
        db.Users.Remove(u);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpGet("me/profile")]
    public async Task<IActionResult> Profile(CancellationToken ct){
        var u = await db.Users.Include(x => x.Department).FirstAsync(x => x.Id == User.UserId(), ct);
        return Ok(new {
            id = u.Id, username = u.Username, fullName = u.FullName, email = u.Email, mobile = u.Mobile, role = u.Role.ToString(), department = u.Department?.Name
        }
        );
    }
    [HttpPut("me/profile")]
    public async Task<IActionResult> UpdateProfile(UpdateProfileRequest r, CancellationToken ct){
        var u = await db.Users.FindAsync(new object?[]{
            User.UserId()
        }
        ,  ct);
        if (u is null) return Unauthorized();
        u.FullName = r.FullName;
        u.Email = r.Email;
        u.Mobile = r.Mobile;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/departments"), Authorize]
public class DepartmentsController(AppDbContext db) : ControllerBase
{
    [HttpGet, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> List([FromQuery]string? search, CancellationToken ct){
        var q = db.Departments.Include(d => d.Users).AsQueryable();
        if (User.IsInRole("Department") || User.IsInRole("Teacher"))q = q.Where(d => d.Id == User.DepartmentId());
        if (!string.IsNullOrWhiteSpace(search))q = q.Where(d => d.Name.Contains(search));
        return Ok(await q.OrderBy(d => d.Name).Select(d => new{
            id = d.Id, name = d.Name, managerUserId = d.ManagerUserId, manager = d.Users.Where(u => u.Id == d.ManagerUserId).Select(u => u.FullName).FirstOrDefault(), students = d.Users.Count(u => u.Role == UserRole.Student), teachers = d.Users.Count(u => u.Role == UserRole.Teacher), isActive = d.IsActive
        }
        ).ToListAsync(ct));
    }
    [HttpGet("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var d = await db.Departments.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (d is null) return NotFound();
        if ((User.IsInRole("Department") || User.IsInRole("Teacher")) && d.Id != User.DepartmentId()) return Forbid();
        return Ok(new {
            id = d.Id, name = d.Name, managerUserId = d.ManagerUserId, isActive = d.IsActive, createdAt = d.CreatedAt
        }
        );
    }
    [HttpPost, Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Create(DepartmentRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new {
            message = "نام دپارتمان الزامی است."
        }
        );
        if (r.ManagerUserId.HasValue && !await db.Users.AnyAsync(u => u.Id == r.ManagerUserId.Value && u.Role == UserRole.Department, ct)) return BadRequest(new {
            message = "مدیر انتخاب‌شده باید حسابی با نقش دپارتمان داشته باشد."
        }
        );
        if (await db.Departments.AnyAsync(x => x.Name == r.Name.Trim(), ct)) return Conflict(new {
            message = "نام دپارتمان تکراری است."
        }
        );
        var d = new Department{
            Name = r.Name.Trim(), ManagerUserId = r.ManagerUserId, IsActive = r.IsActive
        }
        ;
        db.Departments.Add(d);
        await db.SaveChangesAsync(ct);
        if (d.ManagerUserId.HasValue)
        {
            var manager = await db.Users.FirstAsync(u => u.Id == d.ManagerUserId.Value, ct);
            manager.DepartmentId = d.Id;
            await db.SaveChangesAsync(ct);
        }
        return CreatedAtAction(nameof(Get), new { id = d.Id }, new { id = d.Id, name = d.Name, managerUserId = d.ManagerUserId, isActive = d.IsActive });
    }
    [HttpPut("{id:guid}"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Update(Guid id, DepartmentRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Name)) return BadRequest(new {
            message = "نام دپارتمان الزامی است."
        }
        );
        if (r.ManagerUserId.HasValue && !await db.Users.AnyAsync(u => u.Id == r.ManagerUserId.Value && u.Role == UserRole.Department, ct)) return BadRequest(new {
            message = "مدیر انتخاب‌شده باید حسابی با نقش دپارتمان داشته باشد."
        }
        );
        if (await db.Departments.AnyAsync(x => x.Id != id && x.Name == r.Name.Trim(), ct)) return Conflict(new {
            message = "نام دپارتمان تکراری است."
        }
        );
        var d = await db.Departments.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (d is null) return NotFound();
        d.Name = r.Name.Trim();
        d.ManagerUserId = r.ManagerUserId;
        d.IsActive = r.IsActive;
        await db.SaveChangesAsync(ct);
        if (d.ManagerUserId.HasValue)
        {
            var manager = await db.Users.FirstAsync(u => u.Id == d.ManagerUserId.Value, ct);
            manager.DepartmentId = d.Id;
            await db.SaveChangesAsync(ct);
        }
        return NoContent();
    }
    [HttpDelete("{id:guid}"), Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var d = await db.Departments.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (d is null) return NotFound();
        db.Departments.Remove(d);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/questions"), Authorize(Policy = "TeacherOrAbove")]
public class QuestionsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]string? search, [FromQuery]string? type, [FromQuery]string? category, CancellationToken ct){
        var q = db.Questions.AsQueryable();
        if (User.IsInRole("Teacher"))q = q.Where(x => x.CreatedByUserId == User.UserId());
        if (User.IsInRole("Department"))q = q.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == User.DepartmentId()));
        if (!string.IsNullOrWhiteSpace(search))q = q.Where(x => x.Text.Contains(search));
        if (!string.IsNullOrWhiteSpace(type))q = q.Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(category))q = q.Where(x => x.Category == category);
        // CreatedAt is not mapped in the supplied SQL schema; never query it in SQL.
        var rows = await q
            .OrderBy(question => question.Text)
            .Select(question => new
            {
                Id = question.Id,
                Text = question.Text,
                Category = question.Category,
                Type = question.Type,
                Difficulty = question.Difficulty,
                OptionsJson = question.OptionsJson,
                CorrectAnswer = question.CorrectAnswer,
                Score = question.Score
            })
            .ToListAsync(ct);

        var response = rows.Select(question => new
        {
            id = question.Id,
            text = question.Text,
            category = question.Category,
            type = question.Type,
            difficulty = question.Difficulty,
            options = string.IsNullOrWhiteSpace(question.OptionsJson)
                ? Array.Empty<string>()
                : JsonSerializer.Deserialize<string[]>(question.OptionsJson) ?? Array.Empty<string>(),
            correctAnswer = question.CorrectAnswer,
            score = question.Score,
        });

        return Ok(response);
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var q = await db.Questions.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (q is null) return NotFound();
        if (User.IsInRole("Teacher") && q.CreatedByUserId != User.UserId()) return Forbid();
        if (User.IsInRole("Department") && !await db.Users.AnyAsync(user => user.Id == q.CreatedByUserId && user.DepartmentId == User.DepartmentId(), ct)) return Forbid();
        return Ok(new {
            id = q.Id, text = q.Text, category = q.Category, type = q.Type, difficulty = q.Difficulty, options = q.OptionsJson == null?Array.Empty<string>():JsonSerializer.Deserialize<string[]>(q.OptionsJson)!, correctAnswer = q.CorrectAnswer, score = q.Score
        }
        );
    }
    [HttpPost]
    public async Task<IActionResult> Create(QuestionRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Text) || string.IsNullOrWhiteSpace(r.Category) || r.Score <= 0 || r.Type is not ("MultipleChoice" or "Essay" or "Matching") || r.Difficulty is not ("Easy" or "Medium" or "Hard")) return BadRequest(new {
            message = "متن، دسته‌بندی، نوع، سختی و نمره معتبر برای سؤال الزامی است."
        });
        if (r.Type =="MultipleChoice" && (r.Options == null || r.Options.Length < 2 || string.IsNullOrWhiteSpace(r.CorrectAnswer) || !r.Options.Contains(r.CorrectAnswer))) return BadRequest(new {
            message = "سؤال چهارگزینه‌ای باید حداقل دو گزینه و پاسخ صحیح معتبر داشته باشد."
        }
        );
        if (r.Type == "Matching" && (r.Options == null || r.Options.Length < 2 || string.IsNullOrWhiteSpace(r.CorrectAnswer) || !IsValidMatchingKey(r.Options, r.CorrectAnswer)))
            return BadRequest(new { message = "سؤال وصل‌کردنی باید حداقل دو جفت معتبر داشته باشد." });
        if (r.Type == "Matching" && (r.Options == null || r.Options.Length < 2 || string.IsNullOrWhiteSpace(r.CorrectAnswer) || !IsValidMatchingKey(r.Options, r.CorrectAnswer)))
            return BadRequest(new { message = "سؤال وصل‌کردنی باید حداقل دو جفت معتبر داشته باشد." });
        var creator = await db.Users.AsNoTracking().Where(user => user.Id == User.UserId()).Select(user => new { user.DepartmentId }).SingleAsync(ct);
        var questionDepartmentId = r.DepartmentId ?? creator.DepartmentId;
        if (!questionDepartmentId.HasValue || !await db.Departments.AnyAsync(department => department.Id == questionDepartmentId.Value, ct))
            return BadRequest(new { message = "برای ثبت سؤال باید دپارتمان معتبر مشخص باشد." });
        if ((User.IsInRole("Teacher") || User.IsInRole("Department")) && creator.DepartmentId != questionDepartmentId)
            return Forbid();
        var q = new Question{
            Text = r.Text, DepartmentId = questionDepartmentId.Value, Category = r.Category, Type = r.Type, Difficulty = r.Difficulty, OptionsJson = JsonSerializer.Serialize(r.Options??Array.Empty<string>()), CorrectAnswer = r.CorrectAnswer, Score = r.Score, CreatedByUserId = User.UserId()
        }
        ;
        db.Questions.Add(q);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new {
            id = q.Id
        }
        ,  new{
            id = q.Id
        }
        );
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, QuestionRequest r, CancellationToken ct){
        var q = await db.Questions.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (q is null) return NotFound();
        if (User.IsInRole("Teacher") && q.CreatedByUserId != User.UserId()) return Forbid();
        if (User.IsInRole("Department") && !await db.Users.AnyAsync(user => user.Id == q.CreatedByUserId && user.DepartmentId == User.DepartmentId(), ct)) return Forbid();
        if (r.Type != q.Type && (await db.ExamQuestions.AnyAsync(item => item.QuestionId == id, ct) || await db.ExamAnswers.AnyAsync(item => item.QuestionId == id, ct))) return Conflict(new { message = "نوع سؤالی که در آزمون استفاده شده قابل تغییر نیست." });
        if (string.IsNullOrWhiteSpace(r.Text) || string.IsNullOrWhiteSpace(r.Category) || r.Score <= 0 || r.Type is not ("MultipleChoice" or "Essay" or "Matching") || r.Difficulty is not ("Easy" or "Medium" or "Hard")) return BadRequest(new {
            message = "متن، دسته‌بندی، نوع، سختی و نمره معتبر برای سؤال الزامی است."
        });
        if (r.Type =="MultipleChoice" && (r.Options == null || r.Options.Length < 2 || string.IsNullOrWhiteSpace(r.CorrectAnswer) || !r.Options.Contains(r.CorrectAnswer))) return BadRequest(new {
            message = "سؤال چهارگزینه‌ای باید حداقل دو گزینه و پاسخ صحیح معتبر داشته باشد."
        }
        );
        q.Text = r.Text;
        q.Category = r.Category;
        q.Type = r.Type;
        q.Difficulty = r.Difficulty;
        q.OptionsJson = JsonSerializer.Serialize(r.Options??Array.Empty<string>());
        q.CorrectAnswer = r.CorrectAnswer;
        q.Score = r.Score;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var q = await db.Questions.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (q is null) return NotFound();
        if (User.IsInRole("Teacher") && q.CreatedByUserId != User.UserId()) return Forbid();
        if (User.IsInRole("Department") && !await db.Users.AnyAsync(user => user.Id == q.CreatedByUserId && user.DepartmentId == User.DepartmentId(), ct)) return Forbid();
        if (await db.ExamQuestions.AnyAsync(item => item.QuestionId == id, ct) || await db.ExamAnswers.AnyAsync(item => item.QuestionId == id, ct)) return Conflict(new { message = "این سؤال در آزمون یا پاسخ ثبت‌شده استفاده شده و قابل حذف نیست." });
        db.Questions.Remove(q);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    private static bool IsValidMatchingKey(string[] options, string json)
    {
        try
        {
            var pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return pairs is not null && pairs.Count == options.Length &&
                options.All(left => pairs.TryGetValue(left, out var right) && !string.IsNullOrWhiteSpace(right)) &&
                pairs.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == pairs.Count;
        }
        catch (JsonException) { return false; }
    }
}
[ApiController, Route("api/exams"), Authorize]
public class ExamsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct){
        var q = db.Exams.Include(x => x.Questions).AsQueryable();
        if (User.IsInRole("Student"))q = q.Where(x => x.IsPublished && (x.DepartmentId == null || x.DepartmentId == User.DepartmentId()));
        if (User.IsInRole("Department"))q = q.Where(x => x.DepartmentId == User.DepartmentId());
        if (User.IsInRole("Teacher"))q = q.Where(x => x.CreatedByUserId == User.UserId());
        // Subject, Description, EndsAt and CreatedAt are not mapped in the supplied database schema.
        // Return explicit nulls rather than asking EF Core to translate ignored properties.
        return Ok(await q.OrderBy(x => x.Title).Select(x => new{
            id = x.Id, title = x.Title, subject = (string?)null, description = (string?)null, durationMinutes = x.DurationMinutes, passingScore = x.PassingScore, allowQuestionNavigation = x.AllowQuestionNavigation, startsAt = x.StartsAt, endsAt = (DateTime?)null, isPublished = x.IsPublished, departmentId = x.DepartmentId, questionCount = x.Questions.Count, createdAt = (DateTime?)null
        }
        ).ToListAsync(ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null || User.IsInRole("Student") && (!e.IsPublished || e.DepartmentId.HasValue && e.DepartmentId != User.DepartmentId())) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        return Ok(new {
            id = e.Id, title = e.Title, subject = e.Subject, description = e.Description, durationMinutes = e.DurationMinutes, passingScore = e.PassingScore, allowQuestionNavigation = e.AllowQuestionNavigation, startsAt = e.StartsAt, endsAt = e.EndsAt, isPublished = e.IsPublished, questionIds = e.Questions.OrderBy(x => x.Order).Select(x => x.QuestionId)
        }
        );
    }
    [HttpPost, Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Create(ExamRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Title) || r.DurationMinutes < 1 || r.DurationMinutes>600 || r.PassingScore < 0 || r.PassingScore>100) return BadRequest(new {
            message = "عنوان آزمون، مدت زمان و نمره قبولی معتبر الزامی است."
        }
        );
        if (r.StartsAt.HasValue && r.EndsAt.HasValue && r.EndsAt <= r.StartsAt) return BadRequest(new {
            message = "زمان پایان باید بعد از زمان شروع باشد."
        }
        );
        var ownerId = User.UserId();
        if ((User.IsInRole("SuperAdmin") || User.IsInRole("Department")) && r.CreatedByUserId.HasValue){
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == r.CreatedByUserId.Value && u.Role == UserRole.Teacher, ct);
            if (owner is null) return BadRequest(new {
                message = "استاد انتخاب‌شده معتبر نیست."
            }
            );
            if (User.IsInRole("Department") && owner.DepartmentId != User.DepartmentId()) return Forbid();
            if (r.DepartmentId.HasValue && owner.DepartmentId.HasValue && r.DepartmentId != owner.DepartmentId) return BadRequest(new {
                message = "دپارتمان انتخاب‌شده با دپارتمان استاد مطابقت ندارد."
            }
            );
            ownerId = owner.Id;
        }
        var effectiveDepartmentId = User.IsInRole("Department") || User.IsInRole("Teacher") ? User.DepartmentId() : r.DepartmentId;
        if (!effectiveDepartmentId.HasValue || !await db.Departments.AnyAsync(department => department.Id == effectiveDepartmentId.Value, ct))
            return BadRequest(new { message = "برای ساخت آزمون باید دپارتمان معتبر مشخص باشد." });
        var e = new Exam{
            Title = r.Title, Subject = r.Subject, Description = r.Description, DurationMinutes = r.DurationMinutes, PassingScore = r.PassingScore, AllowQuestionNavigation = r.AllowQuestionNavigation, StartsAt = r.StartsAt ?? DateTime.UtcNow, EndsAt = r.EndsAt, IsPublished = r.IsPublished, DepartmentId = effectiveDepartmentId, CreatedByUserId = ownerId
        }
        ;
        var ids = (r.QuestionIds??Array.Empty<Guid>()).Distinct().ToArray();
        if (ids.Length == 0) return BadRequest(new {
            message = "برای ساخت آزمون حداقل یک سؤال لازم است."
        }
        );
        if (ids.Length>0){
            var validQuery = db.Questions.Where(q => ids.Contains(q.Id));
            if (User.IsInRole("Teacher"))validQuery = validQuery.Where(q => q.CreatedByUserId == User.UserId());
            if (User.IsInRole("Department"))validQuery = validQuery.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == User.DepartmentId()));
            var valid = await validQuery.Select(q => q.Id).ToListAsync(ct);
            if (valid.Count != ids.Length) return BadRequest(new {
                message = "یک یا چند سؤال وجود ندارد یا به شما تعلق ندارد."
            }
            );
            var baseScore = Math.Floor(100m/ids.Length*100)/100;
            for (int i = 0;i<ids.Length;i++) e.Questions.Add(new ExamQuestion{
                QuestionId = ids[i], Order = i+1, Score = i == ids.Length-1?100m-baseScore*(ids.Length-1):baseScore
            }
            );
        }
        db.Exams.Add(e);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new {
            id = e.Id
        }
        ,  new{
            id = e.Id
        }
        );
    }
    [HttpPut("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Update(Guid id, ExamRequest r, CancellationToken ct){
        if (string.IsNullOrWhiteSpace(r.Title) || r.DurationMinutes < 1 || r.DurationMinutes>600 || r.PassingScore < 0 || r.PassingScore>100) return BadRequest(new {
            message = "عنوان آزمون، مدت زمان و نمره قبولی معتبر الزامی است."
        }
        );
        if (r.StartsAt.HasValue && r.EndsAt.HasValue && r.EndsAt <= r.StartsAt) return BadRequest(new {
            message = "زمان پایان باید بعد از زمان شروع باشد."
        }
        );
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        var ownerId = e.CreatedByUserId;
        if ((User.IsInRole("SuperAdmin") || User.IsInRole("Department")) && r.CreatedByUserId.HasValue){
            var owner = await db.Users.FirstOrDefaultAsync(u => u.Id == r.CreatedByUserId.Value && u.Role == UserRole.Teacher, ct);
            if (owner is null) return BadRequest(new {
                message = "استاد انتخاب‌شده معتبر نیست."
            }
            );
            if (User.IsInRole("Department") && owner.DepartmentId != User.DepartmentId()) return Forbid();
            if (r.DepartmentId.HasValue && owner.DepartmentId.HasValue && r.DepartmentId != owner.DepartmentId) return BadRequest(new {
                message = "دپارتمان انتخاب‌شده با دپارتمان استاد مطابقت ندارد."
            }
            );
            ownerId = owner.Id;
        }
        e.CreatedByUserId = ownerId;
        e.Title = r.Title;
        e.Subject = r.Subject;
        e.Description = r.Description;
        e.DurationMinutes = r.DurationMinutes;
        e.PassingScore = r.PassingScore;
        e.AllowQuestionNavigation = r.AllowQuestionNavigation;
        e.StartsAt = r.StartsAt;
        e.EndsAt = r.EndsAt;
        e.IsPublished = r.IsPublished;
        e.DepartmentId = User.IsInRole("Department") || User.IsInRole("Teacher")?User.DepartmentId():r.DepartmentId;
        var ids = (r.QuestionIds??Array.Empty<Guid>()).Distinct().ToArray();
        if (ids.Length == 0) return BadRequest(new {
            message = "برای آزمون حداقل یک سؤال لازم است."
        }
        );
        var validQuery = db.Questions.Where(q => ids.Contains(q.Id));
        if (User.IsInRole("Teacher"))validQuery = validQuery.Where(q => q.CreatedByUserId == User.UserId());
        if (User.IsInRole("Department"))validQuery = validQuery.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == User.DepartmentId()));
        var validIds = await validQuery.Select(q => q.Id).ToListAsync(ct);
        if (validIds.Count != ids.Length) return BadRequest(new {
            message = "یک یا چند سؤال وجود ندارد یا به شما تعلق ندارد."
        }
        );
        var existingQuestions = e.Questions.ToDictionary(question => question.QuestionId);
        foreach (var existing in e.Questions.Where(question => !ids.Contains(question.QuestionId)).ToList()){
            e.Questions.Remove(existing);
            db.ExamQuestions.Remove(existing);
        }
        var baseScore = Math.Floor(100m/ids.Length*100)/100;
        for (int i = 0;i<ids.Length;i++){
            var score = i == ids.Length-1?100m-baseScore*(ids.Length-1):baseScore;
            if (existingQuestions.TryGetValue(ids[i], out var existing)){
                existing.Order = i+1;
                existing.Score = score;
            }
            else e.Questions.Add(new ExamQuestion{
                ExamId = id, QuestionId = ids[i], Order = i+1, Score = score
            }
            );
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpDelete("{id:guid}"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var e = await db.Exams.FindAsync(new object?[]{
            id
        }
        ,  ct);
        if (e is null) return NotFound();
        if (User.IsInRole("Department") && e.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && e.CreatedByUserId != User.UserId()) return Forbid();
        db.Exams.Remove(e);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPost("{id:guid}/start"), Authorize(Roles ="Student")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct){
        var e = await db.Exams.Include(x => x.Questions).FirstOrDefaultAsync(x => x.Id == id && x.IsPublished, ct);
        if (e is null) return NotFound();
        if (e.DepartmentId.HasValue && e.DepartmentId != User.DepartmentId()) return NotFound();
        var now = DateTime.UtcNow;
        if (e.StartsAt.HasValue && now<e.StartsAt.Value) return BadRequest(new {
            message = "زمان شروع آزمون نرسیده است."
        }
        );
        if (e.EndsAt.HasValue && now>e.EndsAt.Value) return BadRequest(new {
            message = "مهلت شرکت در آزمون پایان یافته است."
        }
        );
        var student = User.UserId();
        var active = await db.ExamAttempts.FirstOrDefaultAsync(x => x.ExamId == id && x.StudentId == student && x.Status =="InProgress",  ct);
        if (active is null){
            active = new ExamAttempt{
                ExamId = id, StudentId = student
            }
            ;
            db.ExamAttempts.Add(active);
            await db.SaveChangesAsync(ct);
        }
        var questionIds = e.Questions.OrderBy(x => x.Order).Select(x => x.QuestionId).ToArray();
        var questionRows = await db.Questions.Where(q => questionIds.Contains(q.Id)).Select(q => new{
            id = q.Id, text = q.Text, type = q.Type, optionsJson = q.OptionsJson, correctAnswer = q.CorrectAnswer
        }
        ).ToListAsync(ct);
        var qs = questionRows.Select(q =>
        {
            var options = string.IsNullOrWhiteSpace(q.optionsJson) ? Array.Empty<string>() : JsonSerializer.Deserialize<string[]>(q.optionsJson) ?? Array.Empty<string>();
            var matchingOptions = Array.Empty<string>();
            if (q.type == "Matching" && !string.IsNullOrWhiteSpace(q.correctAnswer))
            {
                try { matchingOptions = JsonSerializer.Deserialize<Dictionary<string, string>>(q.correctAnswer)?.Values.OrderBy(_ => Guid.NewGuid()).ToArray() ?? Array.Empty<string>(); }
                catch (JsonException) { matchingOptions = Array.Empty<string>(); }
            }
            return new { id = q.id, text = q.text, type = q.type, options, matchingOptions };
        }).OrderBy(q => Array.IndexOf(questionIds, q.id)).ToList();
        var savedAnswers = await db.ExamAnswers.Where(x => x.ExamAttemptId == active.Id).Select(x => new{
            questionId = x.QuestionId, answer = x.Answer
        }
        ).ToListAsync(ct);
        return Ok(new {
            attemptId = active.Id, startedAt = active.StartedAt, exam = new{
                id = e.Id, title = e.Title, durationMinutes = e.DurationMinutes, allowQuestionNavigation = e.AllowQuestionNavigation
            }
            ,  questions = qs, answers = savedAnswers
        }
        );
    }
    [HttpPut("attempts/{attemptId:guid}/answers"), Authorize(Roles ="Student")]
    public async Task<IActionResult> SaveAnswers(Guid attemptId, SubmitAttemptRequest r, CancellationToken ct){
        var a = await db.ExamAttempts.Include(x => x.Exam).ThenInclude(x => x.Questions).FirstOrDefaultAsync(x => x.Id == attemptId && x.StudentId == User.UserId() && x.Status =="InProgress",  ct);
        if (a is null) return NotFound();
        if (DateTime.UtcNow>a.StartedAt.AddMinutes(a.Exam.DurationMinutes)) return Conflict(new {
            message = "زمان آزمون به پایان رسیده است؛ پاسخ‌ها را نهایی کنید."
        }
        );
        foreach (var answer in r.Answers){
            if (!a.Exam.Questions.Any(x => x.QuestionId == answer.QuestionId))continue;
            var existing = await db.ExamAnswers.FirstOrDefaultAsync(x => x.ExamAttemptId == attemptId && x.QuestionId == answer.QuestionId, ct);
            if (existing is null)db.ExamAnswers.Add(new ExamAnswer{
                ExamAttemptId = attemptId, QuestionId = answer.QuestionId, Answer = answer.Answer
            }
            );
            else{
                existing.Answer = answer.Answer;
                existing.UpdatedAt = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPost("attempts/{attemptId:guid}/submit"), Authorize(Roles ="Student")]
    public async Task<IActionResult> Submit(Guid attemptId, SubmitAttemptRequest r, CancellationToken ct)
    {
        var attempt = await db.ExamAttempts
            .Include(x => x.Exam)
            .ThenInclude(x => x.Questions)
            .FirstOrDefaultAsync(
                x => x.Id == attemptId &&
                     x.StudentId == User.UserId() &&
                     x.Status == "InProgress", ct);

        if (attempt is null) return NotFound();

        var withinTime = DateTime.UtcNow <=
            attempt.StartedAt.AddMinutes(attempt.Exam.DurationMinutes);

        // Accept the final payload only while the server-side timer is still valid.
        // Answers autosaved before expiry remain eligible for grading after expiry.
        if (withinTime)
        {
            foreach (var answer in r.Answers
                .GroupBy(x => x.QuestionId)
                .Select(group => group.Last()))
            {
                if (!attempt.Exam.Questions.Any(
                    item => item.QuestionId == answer.QuestionId))
                    continue;

                var saved = await db.ExamAnswers.FirstOrDefaultAsync(
                    item => item.ExamAttemptId == attemptId &&
                            item.QuestionId == answer.QuestionId, ct);

                if (saved is null)
                {
                    saved = new ExamAnswer
                    {
                        ExamAttemptId = attemptId,
                        QuestionId = answer.QuestionId
                    };
                    db.ExamAnswers.Add(saved);
                }

                saved.Answer = answer.Answer;
                saved.UpdatedAt = DateTime.UtcNow;
            }
        }

        // Grade the complete persisted answer set, not only the final request body.
        var savedAnswers = await db.ExamAnswers
            .Where(item => item.ExamAttemptId == attemptId)
            .ToListAsync(ct);

        var answeredQuestionIds = savedAnswers
            .Select(item => item.QuestionId)
            .Distinct()
            .ToArray();

        var questionLookup = await db.Questions
            .Where(question => answeredQuestionIds.Contains(question.Id))
            .ToDictionaryAsync(question => question.Id, ct);

        var examQuestionLookup = attempt.Exam.Questions
            .ToDictionary(item => item.QuestionId);

        foreach (var saved in savedAnswers)
        {
            if (!questionLookup.TryGetValue(saved.QuestionId, out var question) ||
                !examQuestionLookup.TryGetValue(saved.QuestionId, out var examQuestion))
                continue;

            if (question.Type == "Matching")
            {
                try
                {
                    var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(question.CorrectAnswer ?? "") ?? new();
                    var submitted = JsonSerializer.Deserialize<Dictionary<string, string>>(saved.Answer ?? "") ?? new();
                    var correctCount = expected.Count(pair => submitted.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));
                    var questionCount = attempt.Exam.Questions.Count;
                    var baseQuestionScore = questionCount == 0 ? 0m : Math.Floor(100m / questionCount * 100m) / 100m;
                    var isLastQuestion = examQuestion.Order == attempt.Exam.Questions.Max(item => item.Order);
                    var normalizedQuestionScore = isLastQuestion ? 100m - baseQuestionScore * (questionCount - 1) : baseQuestionScore;
                    saved.IsCorrect = expected.Count > 0 && correctCount == expected.Count;
                    saved.AwardedScore = expected.Count == 0 ? 0m : Math.Round(normalizedQuestionScore * correctCount / expected.Count, 2);
                }
                catch (JsonException) { saved.IsCorrect = false; saved.AwardedScore = 0m; }
            }
            else if (question.Type == "MultipleChoice")
            {
                if (string.IsNullOrWhiteSpace(question.CorrectAnswer))
                {
                    saved.IsCorrect = null;
                    saved.AwardedScore = 0m;
                    continue;
                }

                saved.IsCorrect = string.Equals(
                    question.CorrectAnswer.Trim(),
                    saved.Answer?.Trim(),
                    StringComparison.OrdinalIgnoreCase);

                var questionCount = attempt.Exam.Questions.Count;
                var baseQuestionScore = questionCount == 0
                    ? 0m
                    : Math.Floor(100m / questionCount * 100m) / 100m;
                var isLastQuestion = examQuestion.Order ==
                    attempt.Exam.Questions.Max(item => item.Order);
                var normalizedQuestionScore = isLastQuestion
                    ? 100m - baseQuestionScore * (questionCount - 1)
                    : baseQuestionScore;

                saved.AwardedScore = saved.IsCorrect == true
                    ? normalizedQuestionScore
                    : 0m;
            }
            else
            {
                // Essay answers require manual grading.
                saved.IsCorrect = null;
                saved.AwardedScore = 0m;
            }
        }

        attempt.Score = savedAnswers.Sum(item => item.AwardedScore);
        attempt.Status = "Submitted";
        attempt.SubmittedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        var pendingManualCount = savedAnswers.Count(item => item.IsCorrect is null);
        return Ok(new
        {
            id = attempt.Id,
            score = pendingManualCount > 0 ? (decimal?)null : attempt.Score,
            autoScore = attempt.Score,
            pendingManualCount,
            status = attempt.Status,
            submittedAt = attempt.SubmittedAt
        });
    }
}
[ApiController, Route("api/results"), Authorize]
public class ResultsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]Guid? examId, [FromQuery]Guid? studentId, CancellationToken ct){
        var q = db.ExamAttempts.Include(x => x.Exam).AsQueryable();
        if (User.IsInRole("Student"))q = q.Where(x => x.StudentId == User.UserId());
        if (User.IsInRole("Teacher"))q = q.Where(x => x.Exam.CreatedByUserId == User.UserId());
        if (User.IsInRole("Department"))q = q.Where(x => x.Exam.DepartmentId == User.DepartmentId());
        if (examId.HasValue)q = q.Where(x => x.ExamId == examId);
        if (studentId.HasValue)q = q.Where(x => x.StudentId == studentId);
        return Ok(await q.OrderByDescending(x => x.SubmittedAt).Select(x => new{
            id = x.Id, examId = x.ExamId, examTitle = x.Exam.Title, studentId = x.StudentId, studentName = db.Users.Where(u => u.Id == x.StudentId).Select(u => u.FullName).FirstOrDefault(), startedAt = x.StartedAt, submittedAt = x.SubmittedAt, score = x.Status == "Graded" || !x.Answers.Any(answer => answer.IsCorrect == null) ? x.Score : null, status = x.Status
        }
        ).ToListAsync(ct));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct){
        var a = await db.ExamAttempts.Include(x => x.Exam).Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (User.IsInRole("Student") && a.StudentId != User.UserId()) return Forbid();
        if (User.IsInRole("Student") && a.Status == "InProgress") return Forbid();
        if (User.IsInRole("Teacher") && a.Exam.CreatedByUserId != User.UserId()) return Forbid();
        if (User.IsInRole("Department") && a.Exam.DepartmentId != User.DepartmentId()) return Forbid();
        var maxScore = 100m;
        var questionIds = a.Answers.Select(answer => answer.QuestionId).Distinct().ToArray();
        var questionDetails = await db.Questions.Where(question => questionIds.Contains(question.Id)).Select(question => new{
            question.Id, question.Text, question.CorrectAnswer
        }
        ).ToDictionaryAsync(question => question.Id, ct);
        return Ok(new {
            id = a.Id, exam = a.Exam.Title, score = a.Status == "Graded" || !a.Answers.Any(answer => answer.IsCorrect == null) ? a.Score : null, autoScore = a.Score, pendingManualCount = a.Answers.Count(answer => answer.IsCorrect == null), maxScore, passingScore = a.Exam.PassingScore, status = a.Status, answers = a.Answers.Select(answer => new{
                questionId = answer.QuestionId, question = questionDetails.TryGetValue(answer.QuestionId, out var detail)?detail.Text:null, correctAnswer = a.Status == "InProgress"?null:questionDetails.TryGetValue(answer.QuestionId, out var correct)?correct.CorrectAnswer:null, answer = answer.Answer, isCorrect = answer.IsCorrect, awardedScore = answer.AwardedScore
            }
            )
        }
        );
    }
    [HttpPut("{id:guid}/grade"), Authorize(Policy = "TeacherOrAbove")]
    public async Task<IActionResult> Grade(Guid id, [FromBody]decimal score, CancellationToken ct){
        if (score < 0) return BadRequest(new {
            message = "نمره نمی‌تواند منفی باشد."
        }
        );
        var a = await db.ExamAttempts.Include(x => x.Exam).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return NotFound();
        if (a.Status == "InProgress") return BadRequest(new { message = "تا زمانی که دانشجو آزمون را ارسال نکرده، امکان ثبت نمره نیست." });
        if (User.IsInRole("Department") && a.Exam.DepartmentId != User.DepartmentId()) return Forbid();
        if (User.IsInRole("Teacher") && a.Exam.CreatedByUserId != User.UserId()) return Forbid();
        var maxScore = 100m;
        if (score>maxScore) return BadRequest(new {
            message =$"حداکثر نمره این آزمون {maxScore} است."
        }
        );
        a.Score = score;
        a.Status ="Graded";
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/notifications"), Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery]bool? unread, CancellationToken ct){
        var q = db.Notifications.Where(x => x.UserId == User.UserId()).AsQueryable();
        if (unread == true)q = q.Where(x => !x.IsRead);
        if (unread == false)q = q.Where(x => x.IsRead);
        return Ok(await q.OrderByDescending(x => x.CreatedAt).ToListAsync(ct));
    }
    [HttpPut("{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct){
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.UserId(), ct);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPut("read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct){
        await db.Notifications.Where(x => x.UserId == User.UserId() && !x.IsRead).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true), ct);
        return NoContent();
    }
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct){
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == User.UserId(), ct);
        if (n is null) return NotFound();
        db.Notifications.Remove(n);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/dashboard"), Authorize]
public class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct){
        var users = db.Users.AsQueryable();
        var departments = db.Departments.AsQueryable();
        var questions = db.Questions.AsQueryable();
        var exams = db.Exams.AsQueryable();
        var attempts = db.ExamAttempts.AsQueryable();
        var departmentId = User.DepartmentId();
        if (User.IsInRole("Department")){
            users = users.Where(user => user.DepartmentId == departmentId);
            departments = departments.Where(department => department.Id == departmentId);
            questions = questions.Where(question => db.Users.Any(user => user.Id == question.CreatedByUserId && user.DepartmentId == departmentId));
            exams = exams.Where(exam => exam.DepartmentId == departmentId);
            attempts = attempts.Where(attempt => attempt.Exam.DepartmentId == departmentId);
        }
        if (User.IsInRole("Teacher")){
            users = users.Where(user => user.Role == UserRole.Student && user.DepartmentId == departmentId);
            departments = departments.Where(department => department.Id == departmentId);
            questions = questions.Where(question => question.CreatedByUserId == User.UserId());
            exams = exams.Where(exam => exam.CreatedByUserId == User.UserId());
            attempts = attempts.Where(attempt => attempt.Exam.CreatedByUserId == User.UserId());
        }
        if (User.IsInRole("Student")){
            attempts = attempts.Where(attempt => attempt.StudentId == User.UserId());
            var availableExams = db.Exams.Where(exam => exam.IsPublished && (exam.DepartmentId == null || exam.DepartmentId == departmentId));
            return Ok(new {
                exams = await availableExams.CountAsync(ct), attempts = await attempts.CountAsync(ct), completed = await attempts.CountAsync(attempt => attempt.Status !="InProgress",  ct), averageScore = await attempts.Where(attempt => attempt.Score != null).Select(attempt => (double?)attempt.Score).AverageAsync(ct)??0
            }
            );
        }
        return Ok(new DashboardSummary( await users.CountAsync(ct), await users.CountAsync(user => user.Role == UserRole.Student, ct), await users.CountAsync(user => user.Role == UserRole.Teacher, ct), await departments.CountAsync(ct), await questions.CountAsync(ct), await exams.CountAsync(ct), await attempts.CountAsync(ct)));
    }
    [HttpGet("activity")]
    public async Task<IActionResult> Activity(CancellationToken ct){
        var since = DateTime.UtcNow.AddDays(-7);
        var q = db.ExamAttempts.Where(x => x.StartedAt >= since).AsQueryable();
        if (User.IsInRole("Department"))q = q.Where(x => x.Exam.DepartmentId == User.DepartmentId());
        if (User.IsInRole("Teacher"))q = q.Where(x => x.Exam.CreatedByUserId == User.UserId());
        if (User.IsInRole("Student"))q = q.Where(x => x.StudentId == User.UserId());
        var data = await q.GroupBy(x => x.StartedAt.Date).Select(g => new{
            date = g.Key, count = g.Count()
        }
        ).ToListAsync(ct);
        return Ok(data);
    }
}
[ApiController, Route("api/settings"), Authorize(Policy = "SuperAdmin")]
public class SettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.Settings.OrderBy(x => x.Key).ToListAsync(ct));
    [HttpPut]
    public async Task<IActionResult> Save(SaveSettingRequest r, CancellationToken ct){
        var s = await db.Settings.SingleOrDefaultAsync(x => x.Key == r.Key, ct);
        if (s is null){
            s = new AppSetting{
                Key = r.Key, Value = r.Value
            }
            ;
            db.Settings.Add(s);
        }
        else{
            s.Value = r.Value;
            s.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
[ApiController, Route("api/reports"), Authorize(Policy = "DepartmentOrAbove")]
public class ReportsController(AppDbContext db) : ControllerBase
{
    [HttpGet("departments")]
    public async Task<IActionResult> Departments(CancellationToken ct){
        var q = db.Departments.AsQueryable();
        if (User.IsInRole("Department"))q = q.Where(d => d.Id == User.DepartmentId());
        return Ok(await q.Select(d => new{
            department = d.Name, students = db.Users.Count(u => u.DepartmentId == d.Id && u.Role == UserRole.Student), teachers = db.Users.Count(u => u.DepartmentId == d.Id && u.Role == UserRole.Teacher), exams = db.Exams.Count(e => e.DepartmentId == d.Id), averageScore = db.ExamAttempts.Where(a => a.Exam.DepartmentId == d.Id && a.Score != null).Select(a => (double?) a.Score).Average()??0, passRate = db.ExamAttempts.Where(a => a.Exam.DepartmentId == d.Id && a.Score != null).Count(a => a.Score >= a.Exam.PassingScore)*100.0/Math.Max(1, db.ExamAttempts.Count(a => a.Exam.DepartmentId == d.Id && a.Score != null)), participation = db.ExamAttempts.Count(a => a.Exam.DepartmentId == d.Id)
        }
        ).ToListAsync(ct));
    }
}
static class ClaimsExtensions{
    public static Guid UserId(this ClaimsPrincipal u) => Guid.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id)?id:Guid.Empty;
    public static Guid? DepartmentId(this ClaimsPrincipal u) => Guid.TryParse(u.FindFirst("departmentId")?.Value, out var id)?id:null;
}


[ApiController, Route("api/attendance"), Authorize(Policy = "TeacherOrAbove")]
public sealed class AttendanceController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateOnly? date, [FromQuery] Guid? departmentId, CancellationToken ct)
    {
        var effectiveDepartmentId = User.IsInRole("SuperAdmin") ? departmentId : User.DepartmentId();
        if (!effectiveDepartmentId.HasValue)
            return BadRequest(new { message = "دپارتمان را انتخاب کنید." });

        var departmentExists = await db.Departments.AnyAsync(x => x.Id == effectiveDepartmentId.Value, ct);
        if (!departmentExists) return NotFound(new { message = "دپارتمان پیدا نشد." });

        var targetDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var students = await db.Users.AsNoTracking()
            .Where(x => x.Role == UserRole.Student && x.DepartmentId == effectiveDepartmentId.Value)
            .OrderBy(x => x.FullName)
            .Select(x => new { id = x.Id, fullName = x.FullName, username = x.Username })
            .ToListAsync(ct);

        var ids = students.Select(x => x.id).ToArray();
        var marks = await db.AttendanceRecords.AsNoTracking()
            .Where(x => x.DepartmentId == effectiveDepartmentId.Value && x.AttendanceDate == targetDate && ids.Contains(x.StudentUserId))
            .ToDictionaryAsync(x => x.StudentUserId, x => x.Status, ct);

        return Ok(new {
            date = targetDate,
            departmentId = effectiveDepartmentId,
            students = students.Select(x => new {
                x.id, x.fullName, x.username,
                status = marks.TryGetValue(x.id, out var status) ? status : (string?)null
            })
        });
    }

    [HttpPut]
    public async Task<IActionResult> Save(SaveAttendanceRequest request, CancellationToken ct)
    {
        var effectiveDepartmentId = User.IsInRole("SuperAdmin") ? request.DepartmentId : User.DepartmentId();
        if (!effectiveDepartmentId.HasValue)
            return BadRequest(new { message = "دپارتمان را انتخاب کنید." });
        if (request.Records is null || request.Records.Count == 0)
            return BadRequest(new { message = "حداقل وضعیت یک دانشجو را مشخص کنید." });
        if (request.Records.Count > 1000)
            return BadRequest(new { message = "در هر بار حداکثر ۱۰۰۰ دانشجو قابل ثبت است." });

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Present", "Absent", "Deleted" };
        if (request.Records.Any(x => !allowed.Contains(x.Status)))
            return BadRequest(new { message = "وضعیت حضور و غیاب نامعتبر است." });

        var ids = request.Records.Select(x => x.StudentId).Distinct().ToArray();
        if (ids.Length != request.Records.Count)
            return BadRequest(new { message = "برای یک دانشجو وضعیت تکراری ارسال شده است." });

        var validIds = await db.Users
            .Where(x => ids.Contains(x.Id) && x.Role == UserRole.Student && x.DepartmentId == effectiveDepartmentId.Value)
            .Select(x => x.Id).ToListAsync(ct);
        if (validIds.Count != ids.Length)
            return BadRequest(new { message = "یک یا چند دانشجو به این دپارتمان تعلق ندارند." });

        var existing = await db.AttendanceRecords
            .Where(x => x.DepartmentId == effectiveDepartmentId.Value && x.AttendanceDate == request.Date && ids.Contains(x.StudentUserId))
            .ToDictionaryAsync(x => x.StudentUserId, ct);

        foreach (var item in request.Records)
        {
            if (existing.TryGetValue(item.StudentId, out var record))
            {
                record.Status = item.Status;
                record.RecordedByUserId = User.UserId();
                record.CreatedAt = DateTime.UtcNow;
            }
            else
            {
                db.AttendanceRecords.Add(new AttendanceRecord {
                    StudentUserId = item.StudentId,
                    DepartmentId = effectiveDepartmentId.Value,
                    RecordedByUserId = User.UserId(),
                    AttendanceDate = request.Date,
                    Status = item.Status
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "حضور و غیاب ذخیره شد.", saved = request.Records.Count, date = request.Date });
    }
}
