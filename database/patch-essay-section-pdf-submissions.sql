/*
  Stage 3: per-section Essay PDF answer submissions.
  Additive and re-runnable. Existing exam-level ExamSubmissionDocuments are
  retained for legacy PDF-only exams and are not migrated or deleted.
  Back up and test against a restored database before production execution.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
    THROW 51300, 'dbo.ExamAttempts table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamSections', N'U') IS NULL
    THROW 51301, 'dbo.ExamSections table was not found.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 51302, 'dbo.Users table was not found.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.ExamSectionSubmissionDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ExamSectionSubmissionDocuments
        (
            Id bigint IDENTITY(1,1) NOT NULL
                CONSTRAINT PK_ExamSectionSubmissionDocuments_Mft PRIMARY KEY,
            AttemptId bigint NOT NULL,
            ExamSectionId bigint NOT NULL,
            StoredFileName nvarchar(100) NOT NULL,
            OriginalFileName nvarchar(255) NOT NULL,
            FileSize bigint NOT NULL,
            UploadedByUserId bigint NOT NULL,
            UploadedAt datetime2 NOT NULL
                CONSTRAINT DF_ExamSectionSubmissionDocuments_UploadedAt_Mft DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_ExamSectionSubmissionDocuments_Attempts_Mft
                FOREIGN KEY (AttemptId) REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE,
            /* NO ACTION avoids SQL Server multiple-cascade-path conflicts. */
            CONSTRAINT FK_ExamSectionSubmissionDocuments_Sections_Mft
                FOREIGN KEY (ExamSectionId) REFERENCES dbo.ExamSections(Id) ON DELETE NO ACTION,
            CONSTRAINT FK_ExamSectionSubmissionDocuments_Users_Mft
                FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION,
            CONSTRAINT CK_ExamSectionSubmissionDocuments_FileSize_Mft CHECK (FileSize > 0)
        );

        CREATE UNIQUE INDEX UX_ExamSectionSubmissionDocuments_Attempt_Section
            ON dbo.ExamSectionSubmissionDocuments(AttemptId, ExamSectionId);
        CREATE INDEX IX_ExamSectionSubmissionDocuments_UploadedByUserId
            ON dbo.ExamSectionSubmissionDocuments(UploadedByUserId);
    END;

    COMMIT TRANSACTION;
    PRINT 'Essay section PDF submission schema patch applied successfully.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
