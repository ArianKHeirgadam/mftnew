using Microsoft.EntityFrameworkCore;

namespace Mft.Infrastructure.Persistence;

/// <summary>
/// Canonical academic schema mappings. This context deliberately maps only
/// tables and columns present in the supplied SQL Server schema. It does not
/// create, migrate, or alter the database.
/// </summary>
public sealed class CanonicalAcademicDbContext(
    DbContextOptions<CanonicalAcademicDbContext> options) : DbContext(options)
{
    public DbSet<CanonicalExamCategory> ExamCategories => Set<CanonicalExamCategory>();
    public DbSet<CanonicalSkill> Skills => Set<CanonicalSkill>();
    public DbSet<CanonicalQuestionType> QuestionTypes => Set<CanonicalQuestionType>();
    public DbSet<CanonicalQuestion> Questions => Set<CanonicalQuestion>();
    public DbSet<CanonicalMultipleChoiceOption> MultipleChoiceOptions => Set<CanonicalMultipleChoiceOption>();
    public DbSet<CanonicalMatchingPair> MatchingPairs => Set<CanonicalMatchingPair>();
    public DbSet<CanonicalEssayGradingCriterion> EssayGradingCriteria => Set<CanonicalEssayGradingCriterion>();
    public DbSet<CanonicalClass> Classes => Set<CanonicalClass>();
    public DbSet<CanonicalClassMember> ClassMembers => Set<CanonicalClassMember>();
    public DbSet<CanonicalExam> Exams => Set<CanonicalExam>();
    public DbSet<CanonicalExamSection> ExamSections => Set<CanonicalExamSection>();
    public DbSet<CanonicalExamQuestion> ExamQuestions => Set<CanonicalExamQuestion>();
    public DbSet<CanonicalExamAttempt> ExamAttempts => Set<CanonicalExamAttempt>();
    public DbSet<CanonicalExamAttemptStatus> ExamAttemptStatuses => Set<CanonicalExamAttemptStatus>();
    public DbSet<CanonicalStudentAnswer> StudentAnswers => Set<CanonicalStudentAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CanonicalExamCategory>(e =>
        {
            e.ToTable("ExamCategories"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<CanonicalSkill>(e =>
        {
            e.ToTable("Skills"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.ExamCategoryId, x.Name }).IsUnique();
            e.HasOne(x => x.ExamCategory).WithMany(x => x.Skills)
                .HasForeignKey(x => x.ExamCategoryId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalQuestionType>(e =>
        {
            e.ToTable("QuestionTypes"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("tinyint").ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<CanonicalQuestion>(e =>
        {
            e.ToTable("Questions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Text).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Points).HasPrecision(6, 2).HasDefaultValue(1m);
            e.Property(x => x.DifficultyLevel).HasColumnType("tinyint");
            e.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.CreatedByUserId); e.HasIndex(x => x.QuestionTypeId); e.HasIndex(x => x.SkillId);
            e.HasOne<CanonicalSkill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalQuestionType>().WithMany().HasForeignKey(x => x.QuestionTypeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalMultipleChoiceOption>(e =>
        {
            e.ToTable("MultipleChoiceOptions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.OptionText).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.IsCorrect).HasDefaultValue(false);
            e.HasIndex(x => x.QuestionId);
            e.HasOne<CanonicalQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalMatchingPair>(e =>
        {
            e.ToTable("MatchingPairs"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.LeftItem).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.RightItem).HasColumnType("nvarchar(max)").IsRequired();
            e.HasIndex(x => x.QuestionId);
            e.HasOne<CanonicalQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalEssayGradingCriterion>(e =>
        {
            e.ToTable("EssayGradingCriteria"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.CriteriaText).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.MaxPoints).HasPrecision(6, 2);
            e.HasIndex(x => x.QuestionId);
            e.HasOne<CanonicalQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalClass>(e =>
        {
            e.ToTable("CanonicalClasses"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => new { x.DepartmentId, x.Name }).IsUnique();
            e.HasIndex(x => x.DepartmentId);
        });
        modelBuilder.Entity<CanonicalClassMember>(e =>
        {
            e.ToTable("CanonicalClassMembers"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.AddedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => new { x.ClassId, x.StudentUserId }).IsUnique();
            e.HasIndex(x => x.StudentUserId);
            e.HasOne<CanonicalClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<CanonicalExam>(e =>
        {
            e.ToTable("Exams"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.StartAt).HasColumnType("datetime2");
            e.Property(x => x.EndAt).HasColumnType("datetime2");
            e.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.CreatedByUserId); e.HasIndex(x => x.DepartmentId); e.HasIndex(x => x.ExamCategoryId); e.HasIndex(x => x.ClassId);
            e.HasOne<CanonicalExamCategory>().WithMany().HasForeignKey(x => x.ExamCategoryId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamSection>(e =>
        {
            e.ToTable("ExamSections"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.WeightPercent).HasPrecision(5, 2);
            e.HasIndex(x => x.ExamId); e.HasIndex(x => x.SkillId);
            e.HasOne<CanonicalExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalSkill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamQuestion>(e =>
        {
            e.ToTable("ExamQuestions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.PointsOverride).HasPrecision(6, 2);
            e.HasIndex(x => x.ExamSectionId); e.HasIndex(x => x.QuestionId);
            e.HasOne<CanonicalExamSection>().WithMany().HasForeignKey(x => x.ExamSectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamAttemptStatus>(e =>
        {
            e.ToTable("ExamAttemptStatuses"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("tinyint").ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
        modelBuilder.Entity<CanonicalExamAttempt>(e =>
        {
            e.ToTable("ExamAttempts"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.StartedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.Property(x => x.SubmittedAt).HasColumnType("datetime2");
            e.Property(x => x.AutoGradedScore).HasPrecision(7, 2);
            e.Property(x => x.FinalScore).HasPrecision(7, 2);
            e.HasIndex(x => new { x.ExamId, x.StudentUserId }).IsUnique();
            e.HasIndex(x => x.StudentUserId);
            e.HasOne<CanonicalExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.StudentUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalExamAttemptStatus>().WithMany().HasForeignKey(x => x.StatusId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalStudentAnswer>(e =>
        {
            e.ToTable("StudentAnswers"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.AnswerJson).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.Score).HasPrecision(6, 2);
            e.Property(x => x.GradedAt).HasColumnType("datetime2");
            e.HasIndex(x => x.ExamAttemptId); e.HasIndex(x => x.ExamQuestionId);
            e.HasIndex(x => new { x.ExamAttemptId, x.ExamQuestionId }).IsUnique();
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalExamQuestion>().WithMany().HasForeignKey(x => x.ExamQuestionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.GradedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}

public sealed class CanonicalExamCategory { public long Id { get; set; } public string Name { get; set; } = ""; public ICollection<CanonicalSkill> Skills { get; set; } = new List<CanonicalSkill>(); }
public sealed class CanonicalSkill { public long Id { get; set; } public long ExamCategoryId { get; set; } public string Name { get; set; } = ""; public CanonicalExamCategory ExamCategory { get; set; } = null!; }
public sealed class CanonicalQuestionType { public byte Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class CanonicalQuestion { public long Id { get; set; } public long SkillId { get; set; } public byte QuestionTypeId { get; set; } public string Text { get; set; } = ""; public decimal Points { get; set; } = 1m; public byte? DifficultyLevel { get; set; } public long CreatedByUserId { get; set; } public string? MetadataJson { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CanonicalMultipleChoiceOption { public long Id { get; set; } public long QuestionId { get; set; } public string OptionText { get; set; } = ""; public bool IsCorrect { get; set; } public int DisplayOrder { get; set; } }
public sealed class CanonicalMatchingPair { public long Id { get; set; } public long QuestionId { get; set; } public string LeftItem { get; set; } = ""; public string RightItem { get; set; } = ""; public int DisplayOrder { get; set; } }
public sealed class CanonicalEssayGradingCriterion { public long Id { get; set; } public long QuestionId { get; set; } public string CriteriaText { get; set; } = ""; public decimal MaxPoints { get; set; } }
public sealed class CanonicalClass { public long Id { get; set; } public string Name { get; set; } = ""; public long DepartmentId { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CanonicalClassMember { public long Id { get; set; } public long ClassId { get; set; } public long StudentUserId { get; set; } public DateTime AddedAt { get; set; } }
public sealed class CanonicalExam { public long Id { get; set; } public string Title { get; set; } = ""; public long ExamCategoryId { get; set; } public long DepartmentId { get; set; } public long? ClassId { get; set; } public long CreatedByUserId { get; set; } public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } public int DurationMinutes { get; set; } public bool IsPublished { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CanonicalExamSection { public long Id { get; set; } public long ExamId { get; set; } public long SkillId { get; set; } public int DurationMinutes { get; set; } public int DisplayOrder { get; set; } public decimal WeightPercent { get; set; } }
public sealed class CanonicalExamQuestion { public long Id { get; set; } public long ExamSectionId { get; set; } public long QuestionId { get; set; } public int DisplayOrder { get; set; } public decimal? PointsOverride { get; set; } }

public sealed class CanonicalExamAttemptStatus { public byte Id { get; set; } public string Code { get; set; } = ""; }
public sealed class CanonicalExamAttempt { public long Id { get; set; } public long ExamId { get; set; } public long StudentUserId { get; set; } public DateTime StartedAt { get; set; } public DateTime? SubmittedAt { get; set; } public byte StatusId { get; set; } public decimal? AutoGradedScore { get; set; } public decimal? FinalScore { get; set; } }
public sealed class CanonicalStudentAnswer { public long Id { get; set; } public long ExamAttemptId { get; set; } public long ExamQuestionId { get; set; } public string AnswerJson { get; set; } = ""; public bool IsAutoGraded { get; set; } public decimal? Score { get; set; } public long? GradedByUserId { get; set; } public DateTime? GradedAt { get; set; } }
