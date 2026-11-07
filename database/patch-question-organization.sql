/*
  Canonical question organization: independent hierarchical folders and tags.
  Run against MftDb_Canonical. Safe to re-run; existing questions remain intact.
*/
IF DB_NAME() <> N'MftDb_Canonical'
    THROW 51430, 'Run this patch against MftDb_Canonical.', 1;
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'dbo.QuestionFolders', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.QuestionFolders
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionFolders PRIMARY KEY,
        OwnerUserId bigint NOT NULL,
        ParentFolderId bigint NULL,
        Name nvarchar(150) NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_QuestionFolders_CreatedAt DEFAULT(sysutcdatetime()),
        CONSTRAINT FK_QuestionFolders_Users_OwnerUserId FOREIGN KEY(OwnerUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_QuestionFolders_QuestionFolders_ParentFolderId FOREIGN KEY(ParentFolderId) REFERENCES dbo.QuestionFolders(Id)
    );
    CREATE UNIQUE INDEX UX_QuestionFolders_Owner_Parent_Name ON dbo.QuestionFolders(OwnerUserId, ParentFolderId, Name);
END;
IF COL_LENGTH(N'dbo.Questions', N'FolderId') IS NULL
    ALTER TABLE dbo.Questions ADD FolderId bigint NULL;
IF COL_LENGTH(N'dbo.Questions', N'TagsJson') IS NULL
    ALTER TABLE dbo.Questions ADD TagsJson nvarchar(max) NULL;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Questions_QuestionFolders_FolderId' AND parent_object_id=OBJECT_ID(N'dbo.Questions'))
    ALTER TABLE dbo.Questions ADD CONSTRAINT FK_Questions_QuestionFolders_FolderId FOREIGN KEY(FolderId) REFERENCES dbo.QuestionFolders(Id);
IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name=N'IX_Questions_FolderId' AND object_id=OBJECT_ID(N'dbo.Questions'))
    CREATE INDEX IX_Questions_FolderId ON dbo.Questions(FolderId);
COMMIT TRANSACTION;
