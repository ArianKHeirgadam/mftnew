/*
  Additive data-model patch for section-based exam workflow.
  - Safe to re-run.
  - Does not delete, rename, rewrite, or re-parent existing exam/question/answer rows.
  - Existing sections keep SectionType = NULL and are treated as legacy sections.
  - Existing text Essay questions remain untouched for backward compatibility.
  BACK UP the target database and test on a development copy before execution.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 51200, 'dbo.Exams table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamSections', N'U') IS NULL
    THROW 51201, 'dbo.ExamSections table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamQuestions', N'U') IS NULL
    THROW 51202, 'dbo.ExamQuestions table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
    THROW 51203, 'dbo.ExamAttempts table was not found.', 1;
IF OBJECT_ID(N'dbo.StudentAnswers', N'U') IS NULL
    THROW 51204, 'dbo.StudentAnswers table was not found.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51205, 'dbo.Users table was not found.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    /* NULL SectionType deliberately identifies sections created before this patch. */
    IF COL_LENGTH(N'dbo.ExamSections', N'SectionType') IS NULL
        ALTER TABLE dbo.ExamSections ADD SectionType nvarchar(20) NULL;
    IF COL_LENGTH(N'dbo.ExamSections', N'TotalPoints') IS NULL
        ALTER TABLE dbo.ExamSections ADD TotalPoints decimal(7,2) NULL;
    IF COL_LENGTH(N'dbo.ExamSections', N'NegativeMarkingEnabled') IS NULL
    BEGIN
        ALTER TABLE dbo.ExamSections ADD NegativeMarkingEnabled bit NULL
            CONSTRAINT DF_ExamSections_NegativeMarkingEnabled_Mft DEFAULT (0) WITH VALUES;
    END;
    IF COL_LENGTH(N'dbo.ExamSections', N'NegativeMarkingPoints') IS NULL
        ALTER TABLE dbo.ExamSections ADD NegativeMarkingPoints decimal(6,2) NULL;

    /* Normalize only the newly introduced flag; legacy sections remain legacy (SectionType NULL). */
    /* Dynamic SQL defers column binding until after the conditional ADD has run. */
    EXEC sys.sp_executesql N'UPDATE dbo.ExamSections
       SET NegativeMarkingEnabled = 0
     WHERE NegativeMarkingEnabled IS NULL;';
    IF EXISTS (
        SELECT 1 FROM sys.columns
        WHERE object_id = OBJECT_ID(N'dbo.ExamSections')
          AND name = N'NegativeMarkingEnabled' AND is_nullable = 1
    )
        ALTER TABLE dbo.ExamSections ALTER COLUMN NegativeMarkingEnabled bit NOT NULL;

    IF NOT EXISTS (
        SELECT 1 FROM sys.default_constraints dc
        JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
        WHERE dc.parent_object_id = OBJECT_ID(N'dbo.ExamSections')
          AND c.name = N'NegativeMarkingEnabled'
    )
        ALTER TABLE dbo.ExamSections ADD CONSTRAINT DF_ExamSections_NegativeMarkingEnabled_Mft
            DEFAULT (0) FOR NegativeMarkingEnabled;

    /* Exam-level upload limit is nullable: application setting remains the fallback. */
    IF COL_LENGTH(N'dbo.Exams', N'MaxPdfFileSizeBytes') IS NULL
        ALTER TABLE dbo.Exams ADD MaxPdfFileSizeBytes bigint NULL;

    /* Keep existing Score as the compatibility/result field; new score components are nullable. */
    IF COL_LENGTH(N'dbo.StudentAnswers', N'AutoScore') IS NULL
        ALTER TABLE dbo.StudentAnswers ADD AutoScore decimal(6,2) NULL;
    IF COL_LENGTH(N'dbo.StudentAnswers', N'ManualScore') IS NULL
        ALTER TABLE dbo.StudentAnswers ADD ManualScore decimal(6,2) NULL;
    IF COL_LENGTH(N'dbo.StudentAnswers', N'GradingStatus') IS NULL
        ALTER TABLE dbo.StudentAnswers ADD GradingStatus nvarchar(30) NULL;
    IF COL_LENGTH(N'dbo.StudentAnswers', N'ManualFeedback') IS NULL
        ALTER TABLE dbo.StudentAnswers ADD ManualFeedback nvarchar(2000) NULL;

    IF COL_LENGTH(N'dbo.ExamAttempts', N'ManualGradedScore') IS NULL
        ALTER TABLE dbo.ExamAttempts ADD ManualGradedScore decimal(7,2) NULL;
    IF COL_LENGTH(N'dbo.ExamAttempts', N'GradingStatus') IS NULL
        ALTER TABLE dbo.ExamAttempts ADD GradingStatus nvarchar(30) NULL;

    /* One private PDF question paper per Essay section. Binary files stay outside wwwroot. */
    IF OBJECT_ID(N'dbo.ExamSectionDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ExamSectionDocuments
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamSectionDocuments_Mft PRIMARY KEY,
            ExamSectionId bigint NOT NULL,
            StoredFileName nvarchar(100) NOT NULL,
            OriginalFileName nvarchar(255) NOT NULL,
            FileSize bigint NOT NULL,
            UploadedByUserId bigint NOT NULL,
            UploadedAt datetime2 NOT NULL
                CONSTRAINT DF_ExamSectionDocuments_UploadedAt_Mft DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_ExamSectionDocuments_ExamSections_Mft
                FOREIGN KEY (ExamSectionId) REFERENCES dbo.ExamSections(Id) ON DELETE CASCADE,
            CONSTRAINT FK_ExamSectionDocuments_Users_Mft
                FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UX_ExamSectionDocuments_ExamSectionId
            ON dbo.ExamSectionDocuments(ExamSectionId);
        CREATE INDEX IX_ExamSectionDocuments_UploadedByUserId
            ON dbo.ExamSectionDocuments(UploadedByUserId);
    END;

    /* Persist randomized order per attempt so refresh/autosave cannot reshuffle the exam. */
    IF OBJECT_ID(N'dbo.ExamAttemptQuestionOrders', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ExamAttemptQuestionOrders
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAttemptQuestionOrders_Mft PRIMARY KEY,
            ExamAttemptId bigint NOT NULL,
            ExamQuestionId bigint NOT NULL,
            QuestionOrder int NOT NULL,
            OptionOrderJson nvarchar(max) NULL,
            MatchingRightOrderJson nvarchar(max) NULL,
            CONSTRAINT FK_ExamAttemptQuestionOrders_Attempts_Mft
                FOREIGN KEY (ExamAttemptId) REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE,
            CONSTRAINT FK_ExamAttemptQuestionOrders_ExamQuestions_Mft
                FOREIGN KEY (ExamQuestionId) REFERENCES dbo.ExamQuestions(Id) ON DELETE NO ACTION
        );
        CREATE UNIQUE INDEX UX_ExamAttemptQuestionOrders_Attempt_Question
            ON dbo.ExamAttemptQuestionOrders(ExamAttemptId, ExamQuestionId);
        CREATE INDEX IX_ExamAttemptQuestionOrders_Attempt_Order
            ON dbo.ExamAttemptQuestionOrders(ExamAttemptId, QuestionOrder);
    END;

    COMMIT TRANSACTION;
    PRINT 'Section-based exam workflow data model patch applied successfully.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
