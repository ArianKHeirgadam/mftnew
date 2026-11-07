/*
  Exam/question folder-view query indexes.
  - Additive only: no tables, columns, constraints, or existing rows are changed.
  - Safe to re-run; each index is created only when its named index is absent.
  - Supports class/department/owner date ordering, section counts, and reverse
    question-to-exam lookups for the virtual folder tree.
  - Run against a restored development database first and back up before use.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 51400, 'dbo.Exams table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamSections', N'U') IS NULL
    THROW 51401, 'dbo.ExamSections table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamQuestions', N'U') IS NULL
    THROW 51402, 'dbo.ExamQuestions table was not found.', 1;

IF COL_LENGTH(N'dbo.Exams', N'ClassId') IS NULL
   OR COL_LENGTH(N'dbo.Exams', N'DepartmentId') IS NULL
   OR COL_LENGTH(N'dbo.Exams', N'CreatedByUserId') IS NULL
   OR COL_LENGTH(N'dbo.Exams', N'StartAt') IS NULL
   OR COL_LENGTH(N'dbo.Exams', N'IsPublished') IS NULL
    THROW 51403, 'dbo.Exams is missing one or more columns required by the folder indexes.', 1;

IF COL_LENGTH(N'dbo.ExamSections', N'ExamId') IS NULL
   OR COL_LENGTH(N'dbo.ExamSections', N'DisplayOrder') IS NULL
   OR COL_LENGTH(N'dbo.ExamSections', N'SkillId') IS NULL
   OR COL_LENGTH(N'dbo.ExamSections', N'SectionType') IS NULL
   OR COL_LENGTH(N'dbo.ExamSections', N'WeightPercent') IS NULL
    THROW 51404, 'dbo.ExamSections is missing one or more columns required by the folder indexes.', 1;

IF COL_LENGTH(N'dbo.ExamQuestions', N'ExamSectionId') IS NULL
   OR COL_LENGTH(N'dbo.ExamQuestions', N'QuestionId') IS NULL
   OR COL_LENGTH(N'dbo.ExamQuestions', N'DisplayOrder') IS NULL
   OR COL_LENGTH(N'dbo.ExamQuestions', N'PointsOverride') IS NULL
    THROW 51405, 'dbo.ExamQuestions is missing one or more columns required by the folder indexes.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Exams') AND name = N'IX_Exams_ClassId_StartAt_Id')
        CREATE NONCLUSTERED INDEX IX_Exams_ClassId_StartAt_Id
            ON dbo.Exams(ClassId ASC, StartAt DESC, Id ASC)
            INCLUDE (DepartmentId, Title, IsPublished, CreatedByUserId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Exams') AND name = N'IX_Exams_DepartmentId_StartAt_Id')
        CREATE NONCLUSTERED INDEX IX_Exams_DepartmentId_StartAt_Id
            ON dbo.Exams(DepartmentId ASC, StartAt DESC, Id ASC)
            INCLUDE (ClassId, Title, IsPublished, CreatedByUserId);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.Exams') AND name = N'IX_Exams_CreatedByUserId_StartAt_Id')
        CREATE NONCLUSTERED INDEX IX_Exams_CreatedByUserId_StartAt_Id
            ON dbo.Exams(CreatedByUserId ASC, StartAt DESC, Id ASC)
            INCLUDE (ClassId, DepartmentId, Title, IsPublished);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ExamSections') AND name = N'IX_ExamSections_ExamId_DisplayOrder_Id')
        CREATE NONCLUSTERED INDEX IX_ExamSections_ExamId_DisplayOrder_Id
            ON dbo.ExamSections(ExamId ASC, DisplayOrder ASC, Id ASC)
            INCLUDE (SkillId, SectionType, WeightPercent);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ExamQuestions') AND name = N'IX_ExamQuestions_ExamSectionId_DisplayOrder_Id')
        CREATE NONCLUSTERED INDEX IX_ExamQuestions_ExamSectionId_DisplayOrder_Id
            ON dbo.ExamQuestions(ExamSectionId ASC, DisplayOrder ASC, Id ASC)
            INCLUDE (QuestionId, PointsOverride);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.ExamQuestions') AND name = N'IX_ExamQuestions_QuestionId_ExamSectionId')
        CREATE NONCLUSTERED INDEX IX_ExamQuestions_QuestionId_ExamSectionId
            ON dbo.ExamQuestions(QuestionId ASC, ExamSectionId ASC)
            INCLUDE (DisplayOrder);

    COMMIT TRANSACTION;
    PRINT 'Exam/question folder-view indexes created or already present.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
