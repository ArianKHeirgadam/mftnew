namespace Mft.Domain.Entities;

public enum UserRole
{
    Student,
    Teacher,
    Department,
    SuperAdmin
}

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public bool MustChangePassword { get; set; }
    public Guid? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Department? Department { get; set; }
}

public class Department
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public Guid? ManagerUserId { get; set; }
    public string? ManagerName { get; set; }
    public string DegreeLevel { get; set; } = "";
    public bool IsActive { get; set; } = true;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User? Manager { get; set; }
    public ICollection<User> Users { get; set; } = new List<User>();
}

public class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Text { get; set; } = "";
    public Guid DepartmentId { get; set; }
    public string Category { get; set; } = "عمومی";
    public string Type { get; set; } = "MultipleChoice";
    public string Difficulty { get; set; } = "Medium";
    public string? OptionsJson { get; set; }
    public string? CorrectAnswer { get; set; }
    public decimal Score { get; set; } = 1;
    public bool IsActive { get; set; } = true;
    public Guid CreatedByUserId { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class QuestionOption
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuestionId { get; set; }
    public bool IsCorrect { get; set; }
}

public class Exam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? Subject { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? Description { get; set; }
    public int DurationMinutes { get; set; } = 45;
    public decimal PassingScore { get; set; } = 50;
    public bool AllowQuestionNavigation { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime? EndsAt { get; set; }
    public bool IsPublished { get; set; }
    public bool ResultsPublished { get; set; }
    public DateTime? ResultsPublishedAt { get; set; }
    public Guid? ResultsPublishedByUserId { get; set; }
    public Guid CreatedByUserId { get; set; }
    public Guid? DepartmentId { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<ExamQuestion> Questions { get; set; } = new List<ExamQuestion>();
}

public class ExamQuestion
{
    public Guid ExamId { get; set; }
    public Exam Exam { get; set; } = null!;
    public Guid QuestionId { get; set; }
    public int Order { get; set; }
    public decimal Score { get; set; } = 1;
}

public class ExamAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public Guid StudentId { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public decimal? Score { get; set; }
    public string Status { get; set; } = "InProgress";
    public Exam Exam { get; set; } = null!;
    public ICollection<ExamAnswer> Answers { get; set; } = new List<ExamAnswer>();
}

public class ExamAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamAttemptId { get; set; }
    public Guid QuestionId { get; set; }
    public Guid? SelectedOptionId { get; set; }
    public string? Answer { get; set; }
    public QuestionOption? SelectedOption { get; set; }
    public bool? IsCorrect { get; set; }
    public decimal AwardedScore { get; set; }
    public bool IsManuallyGraded { get; set; }
    public string? GradingComment { get; set; }
    public Guid? GradedByUserId { get; set; }
    public DateTime? GradedAt { get; set; }
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public ExamAttempt Attempt { get; set; } = null!;
}

public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string Type { get; set; } = "Info";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


public class AttendanceRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid StudentUserId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public string Status { get; set; } = "Present";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}


public class ExamDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ExamId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

public class ExamSubmissionDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AttemptId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public Guid UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
