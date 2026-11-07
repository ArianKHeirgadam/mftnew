namespace Mft.Application.Contracts;

public record LoginRequest(string Username, string Password);

public record LoginResponse(
    string AccessToken,
    DateTime ExpiresAt,
    Guid UserId,
    string Username,
    string FullName,
    string Role,
    bool MustChangePassword,
    Guid? DepartmentId);

public record CreateUserRequest(
    string Username,
    string Password,
    string FullName,
    string Role,
    string? Email,
    string? Mobile,
    Guid? DepartmentId,
    bool MustChangePassword = false);

public record UpdateUserRequest(
    string FullName,
    string? Email,
    string? Mobile,
    Guid? DepartmentId,
    bool IsActive);

public record DepartmentRequest(string Name, Guid? ManagerUserId, bool IsActive = true);

public record QuestionRequest(
    string Text,
    string Category,
    string Type,
    string Difficulty,
    string[]? Options,
    string? CorrectAnswer,
    decimal Score,
    Guid? DepartmentId = null);

public record ExamRequest(
    string Title,
    string? Subject,
    string? Description,
    int DurationMinutes,
    DateTime? StartsAt,
    DateTime? EndsAt,
    bool IsPublished,
    Guid? DepartmentId,
    Guid[]? QuestionIds,
    Guid? CreatedByUserId = null,
    decimal PassingScore = 50,
    bool AllowQuestionNavigation = true);

public record AnswerRequest(Guid QuestionId, string? Answer);
public record SubmitAttemptRequest(IReadOnlyList<AnswerRequest> Answers);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UpdateProfileRequest(string FullName, string? Email, string? Mobile);
public record SaveSettingRequest(string Key, string Value);

public record DashboardSummary(
    int Users,
    int Students,
    int Teachers,
    int Departments,
    int Questions,
    int Exams,
    int Attempts);


public record AttendanceEntryRequest(Guid StudentId, string Status);
public record SaveAttendanceRequest(DateOnly Date, Guid? DepartmentId, List<AttendanceEntryRequest> Records);
