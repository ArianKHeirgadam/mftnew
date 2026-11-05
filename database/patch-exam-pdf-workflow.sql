/*
  Additive schema patch for the PDF-based exam workflow.
  BACK UP the database and test on a development copy before execution.
  Safe to re-run. Does not drop tables, columns, or existing rows.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 51100, 'dbo.Exams table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamQuestions', N'U') IS NULL
    THROW 51100, 'dbo.ExamQuestions table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamQuestions', N'U') IS NULL
    THROW 51100, 'dbo.ExamQuestions table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
    THROW 51100, 'dbo.ExamAttempts table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamAnswers', N'U') IS NULL
    THROW 51100, 'dbo.ExamAnswers table was not found.', 1;

IF COL_LENGTH('dbo.Exams', 'ResultsPublished') IS NULL
    ALTER TABLE dbo.Exams ADD ResultsPublished bit NOT NULL
        CONSTRAINT DF_Exams_ResultsPublished_Mft DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.Exams', 'ResultsPublishedAt') IS NULL
    ALTER TABLE dbo.Exams ADD ResultsPublishedAt datetime2 NULL;
IF COL_LENGTH('dbo.Exams', 'ResultsPublishedByUserId') IS NULL
    ALTER TABLE dbo.Exams ADD ResultsPublishedByUserId uniqueidentifier NULL;

IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Exams') AND name = N'ResultsPublished' AND is_nullable = 1)
BEGIN
    UPDATE dbo.Exams SET ResultsPublished = 0 WHERE ResultsPublished IS NULL;
    ALTER TABLE dbo.Exams ALTER COLUMN ResultsPublished bit NOT NULL;
END;
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.Exams') AND c.name = N'ResultsPublished'
)
    ALTER TABLE dbo.Exams ADD CONSTRAINT DF_Exams_ResultsPublished_Mft DEFAULT (0) FOR ResultsPublished;

IF COL_LENGTH('dbo.ExamQuestions', 'Score') IS NULL
    ALTER TABLE dbo.ExamQuestions ADD Score decimal(18,2) NOT NULL
        CONSTRAINT DF_ExamQuestions_Score_Mft DEFAULT (1) WITH VALUES;
ELSE
BEGIN
    UPDATE dbo.ExamQuestions SET Score = 1 WHERE Score IS NULL;
    IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ExamQuestions') AND name = N'Score' AND is_nullable = 1)
        ALTER TABLE dbo.ExamQuestions ALTER COLUMN Score decimal(18,2) NOT NULL;
END;
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.ExamQuestions') AND c.name = N'Score'
)
    ALTER TABLE dbo.ExamQuestions ADD CONSTRAINT DF_ExamQuestions_Score_Mft DEFAULT (1) FOR Score;

IF COL_LENGTH('dbo.ExamAnswers', 'IsManuallyGraded') IS NULL
    ALTER TABLE dbo.ExamAnswers ADD IsManuallyGraded bit NOT NULL
        CONSTRAINT DF_ExamAnswers_IsManuallyGraded_Mft DEFAULT (0) WITH VALUES;
IF COL_LENGTH('dbo.ExamAnswers', 'GradingComment') IS NULL
    ALTER TABLE dbo.ExamAnswers ADD GradingComment nvarchar(2000) NULL;
IF COL_LENGTH('dbo.ExamAnswers', 'GradedByUserId') IS NULL
    ALTER TABLE dbo.ExamAnswers ADD GradedByUserId uniqueidentifier NULL;
IF COL_LENGTH('dbo.ExamAnswers', 'GradedAt') IS NULL
    ALTER TABLE dbo.ExamAnswers ADD GradedAt datetime2 NULL;
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.ExamAnswers') AND name = N'IsManuallyGraded' AND is_nullable = 1)
BEGIN
    UPDATE dbo.ExamAnswers SET IsManuallyGraded = 0 WHERE IsManuallyGraded IS NULL;
    ALTER TABLE dbo.ExamAnswers ALTER COLUMN IsManuallyGraded bit NOT NULL;
END;
IF NOT EXISTS (
    SELECT 1 FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID(N'dbo.ExamAnswers') AND c.name = N'IsManuallyGraded'
)
    ALTER TABLE dbo.ExamAnswers ADD CONSTRAINT DF_ExamAnswers_IsManuallyGraded_Mft DEFAULT (0) FOR IsManuallyGraded;

IF OBJECT_ID(N'dbo.ExamDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamDocuments
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ExamDocuments PRIMARY KEY,
        ExamId uniqueidentifier NOT NULL,
        StoredFileName nvarchar(100) NOT NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        FileSize bigint NOT NULL,
        UploadedByUserId uniqueidentifier NOT NULL,
        UploadedAt datetime2 NOT NULL,
        CONSTRAINT FK_ExamDocuments_Exams FOREIGN KEY (ExamId)
            REFERENCES dbo.Exams(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_ExamDocuments_ExamId ON dbo.ExamDocuments(ExamId);
END;

IF OBJECT_ID(N'dbo.ExamSubmissionDocuments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamSubmissionDocuments
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ExamSubmissionDocuments PRIMARY KEY,
        AttemptId uniqueidentifier NOT NULL,
        StoredFileName nvarchar(100) NOT NULL,
        OriginalFileName nvarchar(255) NOT NULL,
        FileSize bigint NOT NULL,
        UploadedByUserId uniqueidentifier NOT NULL,
        UploadedAt datetime2 NOT NULL,
        CONSTRAINT FK_ExamSubmissionDocuments_ExamAttempts FOREIGN KEY (AttemptId)
            REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_ExamSubmissionDocuments_AttemptId ON dbo.ExamSubmissionDocuments(AttemptId);
END;

COMMIT TRANSACTION;
