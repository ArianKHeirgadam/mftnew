/*
  MFT additive schema patch for inline department management and reliable exam grading.
  Run against a BACKUP / development copy first, then against the application database.
  This script only adds nullable/defaulted columns; it does not drop or rewrite existing rows.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL
    THROW 51000, 'dbo.Departments table was not found.', 1;
IF OBJECT_ID(N'dbo.Questions', N'U') IS NULL
    THROW 51000, 'dbo.Questions table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
    THROW 51000, 'dbo.ExamAttempts table was not found.', 1;
IF OBJECT_ID(N'dbo.ExamAnswers', N'U') IS NULL
    THROW 51000, 'dbo.ExamAnswers table was not found.', 1;

IF COL_LENGTH('dbo.Departments', 'ManagerUserId') IS NULL
    ALTER TABLE dbo.Departments ADD ManagerUserId uniqueidentifier NULL;

IF COL_LENGTH('dbo.Questions', 'Category') IS NULL
    ALTER TABLE dbo.Questions ADD Category nvarchar(200) NOT NULL
        CONSTRAINT DF_Questions_Category_Mft DEFAULT N'عمومی' WITH VALUES;
IF COL_LENGTH('dbo.Questions', 'Difficulty') IS NULL
    ALTER TABLE dbo.Questions ADD Difficulty nvarchar(30) NOT NULL
        CONSTRAINT DF_Questions_Difficulty_Mft DEFAULT N'Medium' WITH VALUES;
IF COL_LENGTH('dbo.Questions', 'OptionsJson') IS NULL
    ALTER TABLE dbo.Questions ADD OptionsJson nvarchar(max) NULL;
IF COL_LENGTH('dbo.Questions', 'CorrectAnswer') IS NULL
    ALTER TABLE dbo.Questions ADD CorrectAnswer nvarchar(max) NULL;

IF COL_LENGTH('dbo.ExamAttempts', 'Status') IS NULL
    ALTER TABLE dbo.ExamAttempts ADD Status nvarchar(30) NOT NULL
        CONSTRAINT DF_ExamAttempts_Status_Mft DEFAULT N'InProgress' WITH VALUES;
-- Existing attempts with a submission timestamp are already submitted.
IF COL_LENGTH('dbo.ExamAttempts', 'SubmittedAt') IS NOT NULL
    UPDATE dbo.ExamAttempts
       SET Status = N'Submitted'
     WHERE SubmittedAt IS NOT NULL AND Status = N'InProgress';

IF COL_LENGTH('dbo.ExamAnswers', 'IsCorrect') IS NULL
    ALTER TABLE dbo.ExamAnswers ADD IsCorrect bit NULL;

COMMIT TRANSACTION;
