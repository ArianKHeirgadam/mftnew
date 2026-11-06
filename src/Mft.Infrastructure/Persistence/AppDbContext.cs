using Microsoft.EntityFrameworkCore;
using Mft.Domain.Entities;

namespace Mft.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();
    public DbSet<ExamDocument> ExamDocuments => Set<ExamDocument>();
    public DbSet<ExamSubmissionDocument> ExamSubmissionDocuments => Set<ExamSubmissionDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(510).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(400).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(400);
            entity.Property(x => x.Mobile).HasMaxLength(60);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasOne(x => x.Department).WithMany(x => x.Users)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ManagerName).HasMaxLength(200);
            entity.Property(x => x.DegreeLevel).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ManagerUserId).HasColumnType("uniqueidentifier");
            entity.Ignore(x => x.CreatedAt);
            entity.HasIndex(x => x.Name).IsUnique();
            entity.HasIndex(x => x.ManagerUserId);
            entity.HasOne(x => x.Manager).WithMany()
                .HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("Questions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Score).HasColumnName("Points").HasPrecision(5, 2);
            entity.Property(x => x.DepartmentId).IsRequired();
            entity.Property(x => x.CreatedByUserId).HasColumnName("CreatedByUserId");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Text).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(200).IsRequired(false);
            entity.Property(x => x.Difficulty).HasMaxLength(100).IsRequired(false);
            entity.Property(x => x.OptionsJson).HasColumnType("nvarchar(max)");
            entity.Property(x => x.CorrectAnswer).HasColumnType("nvarchar(max)");
            entity.Ignore(x => x.CreatedAt);
        });

        modelBuilder.Entity<QuestionOption>(entity =>
        {
            entity.ToTable("QuestionOptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.HasAlternateKey(x => new { x.QuestionId, x.Id })
                .HasName("AK_QuestionOptions_QuestionId_Id");
            entity.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.ToTable("Exams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.CreatedByUserId).HasColumnName("CreatedByUserId");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ResultsPublishedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId)
                .IsRequired().OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.StartsAt).HasColumnName("ScheduledAt");
            entity.Property(x => x.IsPublished).HasColumnName("Status")
                .HasConversion(value => value ? "Published" : "Draft", value => value == "Published")
                .HasMaxLength(60).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(500).IsRequired();
            entity.Property(x => x.PassingScore).HasPrecision(5, 2);
            entity.Ignore(x => x.Subject);
            entity.Ignore(x => x.Description);
            entity.Ignore(x => x.EndsAt);
            entity.Ignore(x => x.CreatedAt);
            entity.Property(x => x.ResultsPublished).HasDefaultValue(false);
            entity.Property(x => x.ResultsPublishedAt);
            entity.Property(x => x.ResultsPublishedByUserId);
            entity.Property(x => x.AllowQuestionNavigation).HasColumnName("AllowNavigation");
        });

        modelBuilder.Entity<ExamQuestion>(entity =>
        {
            entity.ToTable("ExamQuestions");
            entity.HasKey(x => new { x.ExamId, x.QuestionId });
            entity.Property(x => x.Order).HasColumnName("SortOrder");
            entity.Property(x => x.Score).HasPrecision(18, 2).HasDefaultValue(1m);
            entity.HasOne(x => x.Exam).WithMany(x => x.Questions)
                .HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamAttempt>(entity =>
        {
            entity.ToTable("ExamAttempts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Status).HasMaxLength(60).IsRequired();
            entity.Property(x => x.Score).HasPrecision(5, 2);
            entity.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Restrict);
            // The API consistently stores the authenticated Users.Id in StudentId.
            // The accompanying SQL patch safely migrates the legacy Students.Id FK.
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamAnswer>(entity =>
        {
            entity.ToTable("ExamAnswers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.ExamAttemptId).HasColumnName("AttemptId");
            entity.Property(x => x.Answer).HasColumnName("EssayText").HasColumnType("nvarchar(max)");
            entity.Property(x => x.AwardedScore).HasColumnName("Score").HasPrecision(18, 2);
            entity.Property(x => x.IsCorrect);
            entity.Property(x => x.IsManuallyGraded).HasDefaultValue(false);
            entity.Property(x => x.GradingComment).HasMaxLength(2000);
            entity.Property(x => x.GradedByUserId);
            entity.Property(x => x.GradedAt);
            entity.Property(x => x.SelectedOptionId);
            entity.Ignore(x => x.UpdatedAt);
            entity.HasOne(x => x.Attempt).WithMany(x => x.Answers)
                .HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.SelectedOption).WithMany()
                .HasForeignKey(x => new { x.QuestionId, x.SelectedOptionId })
                .HasPrincipalKey(x => new { x.QuestionId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.GradedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamDocument>(entity =>
        {
            entity.ToTable("ExamDocuments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.HasIndex(x => x.ExamId).IsUnique();
            entity.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamSubmissionDocument>(entity =>
        {
            entity.ToTable("ExamSubmissionDocuments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.HasIndex(x => x.AttemptId).IsUnique();
            entity.HasOne<ExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Ignore(x => x.Type);
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Message).HasColumnType("nvarchar(max)").IsRequired();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AttendanceRecord>(entity =>
        {
            entity.ToTable("AttendanceRecords");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.AttendanceDate).HasColumnType("date");
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.StudentUserId, x.AttendanceDate }).IsUnique();
            entity.HasIndex(x => new { x.DepartmentId, x.AttendanceDate });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.StudentUserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("AppSettings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Key).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Value).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.UpdatedAt).IsRequired();
            entity.HasIndex(x => x.Key).IsUnique();
        });
    }
}
