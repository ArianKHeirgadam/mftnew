-- Additive, idempotent persistence for teacher grading of whole-exam PDF-only submissions.
-- Run against the canonical database after the existing ExamAttempts and Users tables exist.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.ExamAttemptManualGrades', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAttemptManualGrades
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAttemptManualGrades PRIMARY KEY,
        AttemptId bigint NOT NULL,
        Score decimal(9,2) NOT NULL,
        Feedback nvarchar(2000) NULL,
        GradedByUserId bigint NOT NULL,
        GradedAt datetime2 NOT NULL,
        RowVersion rowversion NOT NULL,
        CONSTRAINT CK_ExamAttemptManualGrades_Score CHECK (Score >= 0 AND Score <= 100),
        CONSTRAINT FK_ExamAttemptManualGrades_ExamAttempts FOREIGN KEY (AttemptId)
            REFERENCES dbo.ExamAttempts(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_ExamAttemptManualGrades_Users FOREIGN KEY (GradedByUserId)
            REFERENCES dbo.Users(Id) ON DELETE NO ACTION
    );
    CREATE UNIQUE INDEX UX_ExamAttemptManualGrades_AttemptId
        ON dbo.ExamAttemptManualGrades(AttemptId);
    CREATE INDEX IX_ExamAttemptManualGrades_GradedByUserId
        ON dbo.ExamAttemptManualGrades(GradedByUserId);
END;

IF OBJECT_ID(N'dbo.ExamAttemptManualGradeHistory', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ExamAttemptManualGradeHistory
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAttemptManualGradeHistory PRIMARY KEY,
        ManualGradeId bigint NOT NULL,
        AttemptId bigint NOT NULL,
        PreviousScore decimal(9,2) NULL,
        NewScore decimal(9,2) NOT NULL,
        PreviousFeedback nvarchar(2000) NULL,
        NewFeedback nvarchar(2000) NULL,
        ChangedByUserId bigint NOT NULL,
        ChangedAt datetime2 NOT NULL,
        CONSTRAINT CK_ExamAttemptManualGradeHistory_NewScore CHECK (NewScore >= 0 AND NewScore <= 100),
        CONSTRAINT FK_ExamAttemptManualGradeHistory_Grades FOREIGN KEY (ManualGradeId)
            REFERENCES dbo.ExamAttemptManualGrades(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_ExamAttemptManualGradeHistory_Attempts FOREIGN KEY (AttemptId)
            REFERENCES dbo.ExamAttempts(Id) ON DELETE NO ACTION,
        CONSTRAINT FK_ExamAttemptManualGradeHistory_Users FOREIGN KEY (ChangedByUserId)
            REFERENCES dbo.Users(Id) ON DELETE NO ACTION
    );
    CREATE INDEX IX_ExamAttemptManualGradeHistory_AttemptId_ChangedAt
        ON dbo.ExamAttemptManualGradeHistory(AttemptId, ChangedAt);
END;

COMMIT TRANSACTION;
