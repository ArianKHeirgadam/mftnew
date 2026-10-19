using Microsoft.EntityFrameworkCore;
using Mft.Domain.Entities;

namespace Mft.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<ExamAnswer> ExamAnswers => Set<ExamAnswer>();
    public DbSet<Notification> Notifications => Set<Notification>();
    // The supplied database schema has no settings table. Keep the entity available for API compatibility,
    // but do not assume that EF can persist it to a table that does not exist.
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(30);
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();
            entity.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(200);
            entity.Property(x => x.Mobile).HasMaxLength(30);
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasOne(x => x.Department).WithMany(x => x.Users)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.ToTable("Departments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ManagerName).HasMaxLength(200);
            entity.Property(x => x.DegreeLevel).HasMaxLength(100).IsRequired();
            entity.Ignore(x => x.ManagerUserId);
            entity.Ignore(x => x.CreatedAt);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Question>(entity =>
        {
            entity.ToTable("Questions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.Score).HasColumnName("Points").HasPrecision(18, 2);
            entity.Property(x => x.DepartmentId).IsRequired();
            entity.Property(x => x.CreatedByUserId).HasColumnName("CreatedByTeacherId");
            entity.Property(x => x.Text).IsRequired();
            entity.Property(x => x.IsActive).IsRequired();
            entity.Property(x => x.Type).HasMaxLength(30).IsRequired();
            entity.Ignore(x => x.Category);
            entity.Ignore(x => x.Difficulty);
            entity.Ignore(x => x.OptionsJson);
            entity.Ignore(x => x.CorrectAnswer);
            entity.Ignore(x => x.CreatedAt);
        });

        modelBuilder.Entity<Exam>(entity =>
        {
            entity.ToTable("Exams");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.CreatedByUserId).HasColumnName("TeacherId");
            entity.Property(x => x.StartsAt).HasColumnName("ScheduledAt");
            entity.Property(x => x.IsPublished).HasColumnName("Status")
                .HasConversion(value => value ? "Published" : "Draft", value => value == "Published")
                .HasMaxLength(30);
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.PassingScore).HasPrecision(18, 2);
            entity.Ignore(x => x.Subject);
            entity.Ignore(x => x.Description);
            entity.Ignore(x => x.EndsAt);
            entity.Ignore(x => x.CreatedAt);
            entity.Property(x => x.AllowQuestionNavigation).HasColumnName("AllowNavigation");
        });

        modelBuilder.Entity<ExamQuestion>(entity =>
        {
            entity.ToTable("ExamQuestions");
            entity.HasKey(x => new { x.ExamId, x.QuestionId });
            entity.Property(x => x.Order).HasColumnName("SortOrder");
            entity.Ignore(x => x.Score);
            entity.HasOne(x => x.Exam).WithMany(x => x.Questions).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamAttempt>(entity =>
        {
            entity.ToTable("ExamAttempts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Ignore(x => x.Status);
            entity.Property(x => x.Score).HasPrecision(18, 2);
            entity.HasOne(x => x.Exam).WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ExamAnswer>(entity =>
        {
            entity.ToTable("ExamAnswers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Property(x => x.ExamAttemptId).HasColumnName("AttemptId");
            entity.Property(x => x.Answer).HasColumnName("EssayText");
            entity.Property(x => x.AwardedScore).HasColumnName("Score").HasPrecision(18, 2);
            entity.Ignore(x => x.IsCorrect);
            entity.Ignore(x => x.UpdatedAt);
            entity.HasOne(x => x.Attempt).WithMany(x => x.Answers).HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("uniqueidentifier").ValueGeneratedNever();
            entity.Ignore(x => x.Type);
            entity.Property(x => x.Title).HasMaxLength(250).IsRequired();
            entity.Property(x => x.Message).IsRequired();
        });

        // This database has no AppSettings table. Excluding it prevents accidental queries against a
        // nonexistent table; Settings endpoints still need a storage implementation before use.
        modelBuilder.Ignore<AppSetting>();
    }
}
