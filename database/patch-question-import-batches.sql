/*
  Canonical question import folders.
  Run once against the configured canonical database after reviewing the target.
  Safe to re-run; existing questions remain unassigned (ImportBatchId = NULL).
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.QuestionImportBatches', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QuestionImportBatches
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionImportBatches PRIMARY KEY,
        TeacherUserId bigint NOT NULL,
        Title nvarchar(200) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_QuestionImportBatches_CreatedAt DEFAULT (sysutcdatetime()),
        CONSTRAINT FK_QuestionImportBatches_Users_TeacherUserId
            FOREIGN KEY (TeacherUserId) REFERENCES dbo.Users(Id)
    );
END;

IF COL_LENGTH(N'dbo.Questions', N'ImportBatchId') IS NULL
    ALTER TABLE dbo.Questions ADD ImportBatchId bigint NULL;

IF NOT EXISTS
(
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_Questions_QuestionImportBatches_ImportBatchId'
      AND parent_object_id = OBJECT_ID(N'dbo.Questions')
)
BEGIN
    ALTER TABLE dbo.Questions WITH CHECK
        ADD CONSTRAINT FK_Questions_QuestionImportBatches_ImportBatchId
        FOREIGN KEY (ImportBatchId) REFERENCES dbo.QuestionImportBatches(Id);
END;

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Questions_ImportBatchId'
      AND object_id = OBJECT_ID(N'dbo.Questions')
)
    CREATE INDEX IX_Questions_ImportBatchId ON dbo.Questions(ImportBatchId);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_QuestionImportBatches_TeacherUserId_CreatedAt'
      AND object_id = OBJECT_ID(N'dbo.QuestionImportBatches')
)
    CREATE INDEX IX_QuestionImportBatches_TeacherUserId_CreatedAt
        ON dbo.QuestionImportBatches(TeacherUserId, CreatedAt DESC, Id DESC);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE name = N'UX_QuestionImportBatches_TeacherUserId_Title'
      AND object_id = OBJECT_ID(N'dbo.QuestionImportBatches')
)
    CREATE UNIQUE INDEX UX_QuestionImportBatches_TeacherUserId_Title
        ON dbo.QuestionImportBatches(TeacherUserId, Title);

COMMIT TRANSACTION;
