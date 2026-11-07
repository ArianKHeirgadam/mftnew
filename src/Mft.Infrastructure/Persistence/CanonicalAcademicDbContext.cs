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
    public DbSet<CanonicalQuestionImportBatch> QuestionImportBatches => Set<CanonicalQuestionImportBatch>();
    public DbSet<CanonicalMultipleChoiceOption> MultipleChoiceOptions => Set<CanonicalMultipleChoiceOption>();
    public DbSet<CanonicalMatchingPair> MatchingPairs => Set<CanonicalMatchingPair>();
    public DbSet<CanonicalEssayGradingCriterion> EssayGradingCriteria => Set<CanonicalEssayGradingCriterion>();
    public DbSet<CanonicalClass> Classes => Set<CanonicalClass>();
    public DbSet<CanonicalClassMember> ClassMembers => Set<CanonicalClassMember>();
    public DbSet<CanonicalClassTeacher> ClassTeachers => Set<CanonicalClassTeacher>();
    public DbSet<CanonicalExam> Exams => Set<CanonicalExam>();
    public DbSet<CanonicalExamSection> ExamSections => Set<CanonicalExamSection>();
    public DbSet<CanonicalExamQuestion> ExamQuestions => Set<CanonicalExamQuestion>();
    public DbSet<CanonicalExamAttempt> ExamAttempts => Set<CanonicalExamAttempt>();
    public DbSet<CanonicalExamAttemptStatus> ExamAttemptStatuses => Set<CanonicalExamAttemptStatus>();
    public DbSet<CanonicalStudentAnswer> StudentAnswers => Set<CanonicalStudentAnswer>();
    public DbSet<CanonicalExamDocument> ExamDocuments => Set<CanonicalExamDocument>();
    public DbSet<CanonicalExamSubmissionDocument> ExamSubmissionDocuments => Set<CanonicalExamSubmissionDocument>();
    public DbSet<CanonicalExamSectionDocument> ExamSectionDocuments => Set<CanonicalExamSectionDocument>();
    public DbSet<CanonicalExamSectionSubmissionDocument> ExamSectionSubmissionDocuments => Set<CanonicalExamSectionSubmissionDocument>();
    public DbSet<CanonicalExamAttemptQuestionOrder> ExamAttemptQuestionOrders => Set<CanonicalExamAttemptQuestionOrder>();
    public DbSet<CanonicalExamSectionManualGrade> ExamSectionManualGrades => Set<CanonicalExamSectionManualGrade>();
    public DbSet<CanonicalExamSectionManualGradeHistory> ExamSectionManualGradeHistory => Set<CanonicalExamSectionManualGradeHistory>();
    public DbSet<CanonicalExamAttemptManualGrade> ExamAttemptManualGrades => Set<CanonicalExamAttemptManualGrade>();
    public DbSet<CanonicalExamAttemptManualGradeHistory> ExamAttemptManualGradeHistory => Set<CanonicalExamAttemptManualGradeHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // This context references users for academic foreign keys, but does not own identity-role mappings.
        // Exclude the identity join entities entirely; their composite key is configured only by
        // CanonicalIdentityDbContext and they are not part of this context's model.
        modelBuilder.Entity<CanonicalUser>().Ignore(x => x.UserRoles);
        modelBuilder.Ignore<CanonicalUserRole>();
        modelBuilder.Ignore<CanonicalRole>();

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
            e.Property(x => x.Id).HasColumnType("bigint").ValueGeneratedNever();
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
            e.HasIndex(x => x.ImportBatchId);
            e.HasOne<CanonicalSkill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalQuestionType>().WithMany().HasForeignKey(x => x.QuestionTypeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalQuestionImportBatch>().WithMany().HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalQuestionImportBatch>(e =>
        {
            e.ToTable("QuestionImportBatches"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => new { x.TeacherUserId, x.CreatedAt });
            e.HasIndex(x => new { x.TeacherUserId, x.Title }).IsUnique();
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.NoAction);
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

        modelBuilder.Entity<CanonicalClassTeacher>(e =>
        {
            e.ToTable("CanonicalClassTeachers"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.AddedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => new { x.ClassId, x.TeacherUserId }).IsUnique();
            e.HasIndex(x => x.TeacherUserId);
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
            e.Property(x => x.ResultsPublished).HasDefaultValue(false);
            e.Property(x => x.ResultsPublishedAt).HasColumnType("datetime2");
            e.Property(x => x.MaxPdfFileSizeBytes).HasColumnType("bigint");
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.ResultsPublishedByUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => x.CreatedByUserId); e.HasIndex(x => x.DepartmentId); e.HasIndex(x => x.ExamCategoryId); e.HasIndex(x => x.ClassId);
            e.HasIndex(x => new { x.ClassId, x.StartAt, x.Id }).IsDescending(false, true, false)
                .HasDatabaseName("IX_Exams_ClassId_StartAt_Id")
                .IncludeProperties(x => new { x.DepartmentId, x.Title, x.IsPublished, x.CreatedByUserId });
            e.HasIndex(x => new { x.DepartmentId, x.StartAt, x.Id }).IsDescending(false, true, false)
                .HasDatabaseName("IX_Exams_DepartmentId_StartAt_Id")
                .IncludeProperties(x => new { x.ClassId, x.Title, x.IsPublished, x.CreatedByUserId });
            e.HasIndex(x => new { x.CreatedByUserId, x.StartAt, x.Id }).IsDescending(false, true, false)
                .HasDatabaseName("IX_Exams_CreatedByUserId_StartAt_Id")
                .IncludeProperties(x => new { x.ClassId, x.DepartmentId, x.Title, x.IsPublished });
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
            e.Property(x => x.SectionType).HasMaxLength(20);
            e.Property(x => x.TotalPoints).HasPrecision(7, 2);
            e.Property(x => x.NegativeMarkingPoints).HasPrecision(6, 2);
            e.Property(x => x.NegativeMarkingEnabled).HasDefaultValue(false);
            e.HasIndex(x => x.ExamId); e.HasIndex(x => x.SkillId);
            e.HasIndex(x => new { x.ExamId, x.DisplayOrder, x.Id })
                .HasDatabaseName("IX_ExamSections_ExamId_DisplayOrder_Id")
                .IncludeProperties(x => new { x.SkillId, x.SectionType, x.WeightPercent });
            e.HasOne<CanonicalExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalSkill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamQuestion>(e =>
        {
            e.ToTable("ExamQuestions"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.PointsOverride).HasPrecision(6, 2);
            e.HasIndex(x => x.ExamSectionId); e.HasIndex(x => x.QuestionId);
            e.HasIndex(x => new { x.ExamSectionId, x.DisplayOrder, x.Id })
                .HasDatabaseName("IX_ExamQuestions_ExamSectionId_DisplayOrder_Id")
                .IncludeProperties(x => new { x.QuestionId, x.PointsOverride });
            e.HasIndex(x => new { x.QuestionId, x.ExamSectionId })
                .HasDatabaseName("IX_ExamQuestions_QuestionId_ExamSectionId")
                .IncludeProperties(x => x.DisplayOrder);
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
            e.Property(x => x.ManualGradedScore).HasPrecision(7, 2);
            e.Property(x => x.GradingStatus).HasMaxLength(30);
            e.Property(x => x.ManualReviewRequired).HasColumnType("bit");
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
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
            e.Property(x => x.AutoScore).HasPrecision(6, 2);
            e.Property(x => x.ManualScore).HasPrecision(6, 2);
            e.Property(x => x.GradingStatus).HasMaxLength(30);
            e.Property(x => x.ManualFeedback).HasMaxLength(2000);
            e.Property(x => x.GradedAt).HasColumnType("datetime2");
            e.HasIndex(x => x.ExamAttemptId); e.HasIndex(x => x.ExamQuestionId);
            e.HasIndex(x => new { x.ExamAttemptId, x.ExamQuestionId }).IsUnique();
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalExamQuestion>().WithMany().HasForeignKey(x => x.ExamQuestionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.GradedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CanonicalExamSectionDocument>(e =>
        {
            e.ToTable("ExamSectionDocuments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.UploadedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.ExamSectionId).IsUnique();
            e.HasIndex(x => x.UploadedByUserId);
            e.HasOne<CanonicalExamSection>().WithMany().HasForeignKey(x => x.ExamSectionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamSectionSubmissionDocument>(e =>
        {
            e.ToTable("ExamSectionSubmissionDocuments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.UploadedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => new { x.AttemptId, x.ExamSectionId }).IsUnique();
            e.HasIndex(x => x.UploadedByUserId);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalExamSection>().WithMany().HasForeignKey(x => x.ExamSectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamAttemptQuestionOrder>(e =>
        {
            e.ToTable("ExamAttemptQuestionOrders"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.OptionOrderJson).HasColumnType("nvarchar(max)");
            e.Property(x => x.MatchingRightOrderJson).HasColumnType("nvarchar(max)");
            e.HasIndex(x => new { x.ExamAttemptId, x.ExamQuestionId }).IsUnique();
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.ExamAttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalExamQuestion>().WithMany().HasForeignKey(x => x.ExamQuestionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CanonicalExamSectionManualGrade>(e =>
        {
            e.ToTable("ExamSectionManualGrades"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Score).HasPrecision(9, 2);
            e.Property(x => x.Feedback).HasMaxLength(2000);
            e.Property(x => x.GradedAt).HasColumnType("datetime2");
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => new { x.AttemptId, x.ExamSectionId }).IsUnique();
            e.HasIndex(x => x.GradedByUserId);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalExamSection>().WithMany().HasForeignKey(x => x.ExamSectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.GradedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamSectionManualGradeHistory>(e =>
        {
            e.ToTable("ExamSectionManualGradeHistory"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.PreviousScore).HasPrecision(9, 2);
            e.Property(x => x.NewScore).HasPrecision(9, 2);
            e.Property(x => x.PreviousFeedback).HasMaxLength(2000);
            e.Property(x => x.NewFeedback).HasMaxLength(2000);
            e.Property(x => x.ChangedAt).HasColumnType("datetime2");
            e.HasIndex(x => new { x.AttemptId, x.ExamSectionId, x.ChangedAt });
            e.HasOne(x => x.ManualGrade).WithMany().HasForeignKey(x => x.ManualGradeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalExamSection>().WithMany().HasForeignKey(x => x.ExamSectionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CanonicalExamAttemptManualGrade>(e =>
        {
            e.ToTable("ExamAttemptManualGrades"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.Score).HasPrecision(9, 2);
            e.Property(x => x.Feedback).HasMaxLength(2000);
            e.Property(x => x.GradedAt).HasColumnType("datetime2");
            e.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();
            e.HasIndex(x => x.AttemptId).IsUnique();
            e.HasIndex(x => x.GradedByUserId);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.GradedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamAttemptManualGradeHistory>(e =>
        {
            e.ToTable("ExamAttemptManualGradeHistory"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.PreviousScore).HasPrecision(9, 2);
            e.Property(x => x.NewScore).HasPrecision(9, 2);
            e.Property(x => x.PreviousFeedback).HasMaxLength(2000);
            e.Property(x => x.NewFeedback).HasMaxLength(2000);
            e.Property(x => x.ChangedAt).HasColumnType("datetime2");
            e.HasIndex(x => new { x.AttemptId, x.ChangedAt });
            e.HasOne(x => x.ManualGrade).WithMany().HasForeignKey(x => x.ManualGradeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.ChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CanonicalExamDocument>(e =>
        {
            e.ToTable("ExamDocuments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.UploadedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.ExamId).IsUnique();
            e.HasIndex(x => x.UploadedByUserId);
            e.HasOne<CanonicalExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<CanonicalExamSubmissionDocument>(e =>
        {
            e.ToTable("ExamSubmissionDocuments"); e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnType("bigint").UseIdentityColumn();
            e.Property(x => x.StoredFileName).HasMaxLength(100).IsRequired();
            e.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
            e.Property(x => x.UploadedAt).HasColumnType("datetime2").HasDefaultValueSql("sysutcdatetime()");
            e.HasIndex(x => x.AttemptId).IsUnique();
            e.HasIndex(x => x.UploadedByUserId);
            e.HasOne<CanonicalExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<CanonicalUser>().WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.NoAction);
        });
    }
}

public sealed class CanonicalExamCategory { public long Id { get; set; } public string Name { get; set; } = ""; public ICollection<CanonicalSkill> Skills { get; set; } = new List<CanonicalSkill>(); }
public sealed class CanonicalSkill { public long Id { get; set; } public long ExamCategoryId { get; set; } public string Name { get; set; } = ""; public CanonicalExamCategory ExamCategory { get; set; } = null!; }
public sealed class CanonicalQuestionType { public long Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; }
public sealed class CanonicalQuestion { public long Id { get; set; } public long SkillId { get; set; } public long QuestionTypeId { get; set; } public string Text { get; set; } = ""; public decimal Points { get; set; } = 1m; public byte? DifficultyLevel { get; set; } public long CreatedByUserId { get; set; } public long? ImportBatchId { get; set; } public string? MetadataJson { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CanonicalQuestionImportBatch { public long Id { get; set; } public long TeacherUserId { get; set; } public string Title { get; set; } = ""; public DateTime CreatedAt { get; set; } }
public sealed class CanonicalMultipleChoiceOption { public long Id { get; set; } public long QuestionId { get; set; } public string OptionText { get; set; } = ""; public bool IsCorrect { get; set; } public int DisplayOrder { get; set; } }
public sealed class CanonicalMatchingPair { public long Id { get; set; } public long QuestionId { get; set; } public string LeftItem { get; set; } = ""; public string RightItem { get; set; } = ""; public int DisplayOrder { get; set; } }
public sealed class CanonicalEssayGradingCriterion { public long Id { get; set; } public long QuestionId { get; set; } public string CriteriaText { get; set; } = ""; public decimal MaxPoints { get; set; } }
public sealed class CanonicalClass { public long Id { get; set; } public string Name { get; set; } = ""; public long DepartmentId { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class CanonicalClassMember { public long Id { get; set; } public long ClassId { get; set; } public long StudentUserId { get; set; } public DateTime AddedAt { get; set; } }
public sealed class CanonicalClassTeacher { public long Id { get; set; } public long ClassId { get; set; } public long TeacherUserId { get; set; } public DateTime AddedAt { get; set; } }
public sealed class CanonicalExam { public long Id { get; set; } public string Title { get; set; } = ""; public long ExamCategoryId { get; set; } public long DepartmentId { get; set; } public long? ClassId { get; set; } public long CreatedByUserId { get; set; } public DateTime StartAt { get; set; } public DateTime EndAt { get; set; } public int DurationMinutes { get; set; } public bool IsPublished { get; set; } public bool ResultsPublished { get; set; } public DateTime? ResultsPublishedAt { get; set; } public long? ResultsPublishedByUserId { get; set; } public DateTime CreatedAt { get; set; } public long? MaxPdfFileSizeBytes { get; set; } }
public sealed class CanonicalExamSection { public long Id { get; set; } public long ExamId { get; set; } public long SkillId { get; set; } public int DurationMinutes { get; set; } public int DisplayOrder { get; set; } public decimal WeightPercent { get; set; } public string? SectionType { get; set; } public decimal? TotalPoints { get; set; } public bool NegativeMarkingEnabled { get; set; } public decimal? NegativeMarkingPoints { get; set; } }
public sealed class CanonicalExamQuestion { public long Id { get; set; } public long ExamSectionId { get; set; } public long QuestionId { get; set; } public int DisplayOrder { get; set; } public decimal? PointsOverride { get; set; } }

public sealed class CanonicalExamAttemptStatus { public byte Id { get; set; } public string Code { get; set; } = ""; }
public sealed class CanonicalExamAttempt { public long Id { get; set; } public long ExamId { get; set; } public long StudentUserId { get; set; } public DateTime StartedAt { get; set; } public DateTime? SubmittedAt { get; set; } public byte StatusId { get; set; } public decimal? AutoGradedScore { get; set; } public decimal? FinalScore { get; set; } public decimal? ManualGradedScore { get; set; } public string? GradingStatus { get; set; } public bool? ManualReviewRequired { get; set; } public byte[] RowVersion { get; set; } = Array.Empty<byte>(); }
public sealed class CanonicalStudentAnswer { public long Id { get; set; } public long ExamAttemptId { get; set; } public long ExamQuestionId { get; set; } public string AnswerJson { get; set; } = ""; public bool IsAutoGraded { get; set; } public decimal? Score { get; set; } public long? GradedByUserId { get; set; } public DateTime? GradedAt { get; set; } public decimal? AutoScore { get; set; } public decimal? ManualScore { get; set; } public string? GradingStatus { get; set; } public string? ManualFeedback { get; set; } }

public sealed class CanonicalExamDocument
{
    public long Id { get; set; }
    public long ExamId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed class CanonicalExamSubmissionDocument
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}


public sealed class CanonicalExamSectionDocument
{
    public long Id { get; set; }
    public long ExamSectionId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}

public sealed class CanonicalExamSectionManualGrade
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public long ExamSectionId { get; set; }
    public decimal Score { get; set; }
    public string? Feedback { get; set; }
    public long GradedByUserId { get; set; }
    public DateTime GradedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class CanonicalExamSectionManualGradeHistory
{
    public long Id { get; set; }
    public long ManualGradeId { get; set; }
    public CanonicalExamSectionManualGrade ManualGrade { get; set; } = null!;
    public long AttemptId { get; set; }
    public long ExamSectionId { get; set; }
    public decimal? PreviousScore { get; set; }
    public decimal NewScore { get; set; }
    public string? PreviousFeedback { get; set; }
    public string? NewFeedback { get; set; }
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class CanonicalExamAttemptManualGrade
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public decimal Score { get; set; }
    public string? Feedback { get; set; }
    public long GradedByUserId { get; set; }
    public DateTime GradedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public sealed class CanonicalExamAttemptManualGradeHistory
{
    public long Id { get; set; }
    public long ManualGradeId { get; set; }
    public CanonicalExamAttemptManualGrade ManualGrade { get; set; } = null!;
    public long AttemptId { get; set; }
    public decimal? PreviousScore { get; set; }
    public decimal NewScore { get; set; }
    public string? PreviousFeedback { get; set; }
    public string? NewFeedback { get; set; }
    public long ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; }
}

public sealed class CanonicalExamAttemptQuestionOrder
{
    public long Id { get; set; }
    public long ExamAttemptId { get; set; }
    public long ExamQuestionId { get; set; }
    public int QuestionOrder { get; set; }
    public string? OptionOrderJson { get; set; }
    public string? MatchingRightOrderJson { get; set; }
}

public sealed class CanonicalExamSectionSubmissionDocument
{
    public long Id { get; set; }
    public long AttemptId { get; set; }
    public long ExamSectionId { get; set; }
    public string StoredFileName { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public long FileSize { get; set; }
    public long UploadedByUserId { get; set; }
    public DateTime UploadedAt { get; set; }
}
