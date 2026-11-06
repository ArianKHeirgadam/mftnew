/*
  Canonical exam-to-class targeting.
  Run only after database/patch-bigint-feature-tables.sql has created
  dbo.Classes and dbo.ClassMembers and the canonical bigint Exams table exists.
  This patch is additive and preserves existing exams with ClassId = NULL.
*/
IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 52030, 'dbo.Exams table was not found.', 1;
IF OBJECT_ID(N'dbo.Classes', N'U') IS NULL
    THROW 52031, 'dbo.Classes table was not found. Run the canonical bigint feature-table patch first.', 1;
IF COL_LENGTH(N'dbo.Exams', N'ClassId') IS NULL
    ALTER TABLE dbo.Exams ADD ClassId bigint NULL;
IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_Exams_Classes_ClassId'
      AND parent_object_id = OBJECT_ID(N'dbo.Exams')
)
    ALTER TABLE dbo.Exams WITH CHECK
        ADD CONSTRAINT FK_Exams_Classes_ClassId
        FOREIGN KEY (ClassId) REFERENCES dbo.Classes(Id) ON DELETE NO ACTION;
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Exams_ClassId' AND object_id = OBJECT_ID(N'dbo.Exams')
)
    CREATE INDEX IX_Exams_ClassId ON dbo.Exams(ClassId);
