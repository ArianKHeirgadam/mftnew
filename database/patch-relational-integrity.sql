/*
  Relational integrity patch for the MFT database.
  BACK UP the database and test on a development copy before running.
  Idempotent: creates only missing constraints and preserves existing rows.
  All new relationships use NO ACTION on delete/update unless already defined otherwise.

  Important application contract:
  The API writes the authenticated Users.Id into ExamAttempts.StudentId.
  If the legacy FK points to Students.Id, this patch translates existing values
  through Students.UserId before changing the FK target to Users.Id.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Departments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
       OR OBJECT_ID(N'dbo.ExamAnswers', N'U') IS NULL
       OR OBJECT_ID(N'dbo.QuestionOptions', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Questions', N'U') IS NULL
       OR OBJECT_ID(N'dbo.ExamDocuments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.ExamSubmissionDocuments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Exams', N'U') IS NULL
       OR OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Students', N'U') IS NULL
        THROW 51300, 'One or more required MFT tables are missing.', 1;

    /* Validate every proposed FK before changing the schema. */
    IF EXISTS (SELECT 1 FROM dbo.Departments d LEFT JOIN dbo.Users u ON u.Id=d.ManagerUserId
               WHERE d.ManagerUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51301, 'Departments.ManagerUserId contains an orphaned user ID.', 1;

    IF EXISTS (SELECT 1 FROM dbo.AttendanceRecords a LEFT JOIN dbo.Users u ON u.Id=a.RecordedByUserId
               WHERE a.RecordedByUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51302, 'AttendanceRecords.RecordedByUserId contains an orphaned user ID.', 1;

    IF EXISTS (SELECT 1 FROM dbo.ExamAnswers a LEFT JOIN dbo.Questions q ON q.Id=a.QuestionId
               WHERE q.Id IS NULL)
        THROW 51303, 'ExamAnswers.QuestionId contains an orphaned question ID.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.ExamAnswers a
        LEFT JOIN dbo.QuestionOptions o
          ON o.Id=a.SelectedOptionId AND o.QuestionId=a.QuestionId
        WHERE a.SelectedOptionId IS NOT NULL AND o.Id IS NULL
    )
        THROW 51304, 'An ExamAnswers.SelectedOptionId is missing or belongs to a different question.', 1;

    IF EXISTS (SELECT 1 FROM dbo.ExamAnswers a LEFT JOIN dbo.Users u ON u.Id=a.GradedByUserId
               WHERE a.GradedByUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51305, 'ExamAnswers.GradedByUserId contains an orphaned user ID.', 1;

    IF EXISTS (SELECT 1 FROM dbo.ExamDocuments d LEFT JOIN dbo.Users u ON u.Id=d.UploadedByUserId
               WHERE d.UploadedByUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51306, 'ExamDocuments.UploadedByUserId contains an orphaned user ID.', 1;

    IF EXISTS (SELECT 1 FROM dbo.ExamSubmissionDocuments d LEFT JOIN dbo.Users u ON u.Id=d.UploadedByUserId
               WHERE d.UploadedByUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51307, 'ExamSubmissionDocuments.UploadedByUserId contains an orphaned user ID.', 1;

    IF EXISTS (SELECT 1 FROM dbo.Exams e LEFT JOIN dbo.Users u ON u.Id=e.ResultsPublishedByUserId
               WHERE e.ResultsPublishedByUserId IS NOT NULL AND u.Id IS NULL)
        THROW 51308, 'Exams.ResultsPublishedByUserId contains an orphaned user ID.', 1;

    /*
      Migrate ExamAttempts.StudentId from Students.Id to Users.Id only when
      the current FK actually targets Students. This matches the existing API,
      which compares and writes User.UserId().
    */
    DECLARE @studentFk sysname;
    DECLARE @studentTarget sysname;

    SELECT TOP (1)
        @studentFk = fk.name,
        @studentTarget = targetTable.name
    FROM sys.foreign_keys fk
    INNER JOIN sys.foreign_key_columns fkc
        ON fkc.constraint_object_id=fk.object_id
    INNER JOIN sys.columns sourceColumn
        ON sourceColumn.object_id=fkc.parent_object_id
       AND sourceColumn.column_id=fkc.parent_column_id
    INNER JOIN sys.tables targetTable
        ON targetTable.object_id=fk.referenced_object_id
    WHERE fk.parent_object_id=OBJECT_ID(N'dbo.ExamAttempts')
      AND sourceColumn.name=N'StudentId';

    IF @studentTarget = N'Students'
    BEGIN
        IF EXISTS (
            SELECT s.UserId
            FROM dbo.Students s
            GROUP BY s.UserId
            HAVING COUNT_BIG(*) > 1
        )
            THROW 51309, 'Multiple student profiles map to one user; cannot safely migrate ExamAttempts.StudentId.', 1;

        IF EXISTS (
            SELECT 1
            FROM dbo.ExamAttempts a
            LEFT JOIN dbo.Students s ON s.Id=a.StudentId
            LEFT JOIN dbo.Users u ON u.Id=s.UserId
            WHERE s.Id IS NULL OR u.Id IS NULL
        )
            THROW 51310, 'An existing exam attempt cannot be mapped from Students.Id to Users.Id.', 1;

        EXEC(N'ALTER TABLE dbo.ExamAttempts DROP CONSTRAINT ' + QUOTENAME(@studentFk) + N';');

        UPDATE a
           SET StudentId=s.UserId
        FROM dbo.ExamAttempts a
        INNER JOIN dbo.Students s ON s.Id=a.StudentId;
    END
    ELSE IF @studentTarget IS NOT NULL AND @studentTarget <> N'Users'
        THROW 51311, 'ExamAttempts.StudentId references an unexpected table; manual review is required.', 1;

    IF EXISTS (
        SELECT 1 FROM dbo.ExamAttempts a
        LEFT JOIN dbo.Users u ON u.Id=a.StudentId
        WHERE u.Id IS NULL
    )
        THROW 51312, 'ExamAttempts.StudentId does not consistently contain Users.Id values.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
        INNER JOIN sys.columns sc ON sc.object_id=fkc.parent_object_id AND sc.column_id=fkc.parent_column_id
        INNER JOIN sys.tables tt ON tt.object_id=fk.referenced_object_id
        INNER JOIN sys.columns tc ON tc.object_id=fkc.referenced_object_id AND tc.column_id=fkc.referenced_column_id
        WHERE fk.parent_object_id=OBJECT_ID(N'dbo.ExamAttempts')
          AND sc.name=N'StudentId' AND tt.name=N'Users' AND tc.name=N'Id'
    )
        ALTER TABLE dbo.ExamAttempts WITH CHECK
        ADD CONSTRAINT FK_ExamAttempts_Users_StudentId
            FOREIGN KEY (StudentId) REFERENCES dbo.Users(Id);

    /* Composite unique key ensures a selected option belongs to the answer's question. */
    IF NOT EXISTS (
        SELECT 1 FROM sys.key_constraints
        WHERE parent_object_id=OBJECT_ID(N'dbo.QuestionOptions')
          AND name=N'AK_QuestionOptions_QuestionId_Id'
    )
        ALTER TABLE dbo.QuestionOptions
        ADD CONSTRAINT AK_QuestionOptions_QuestionId_Id UNIQUE (QuestionId, Id);

    /* Departments.ManagerUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Departments_Users_ManagerUserId')
        ALTER TABLE dbo.Departments WITH CHECK
        ADD CONSTRAINT FK_Departments_Users_ManagerUserId
            FOREIGN KEY (ManagerUserId) REFERENCES dbo.Users(Id);

    /* AttendanceRecords.RecordedByUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_AttendanceRecords_Users_RecordedByUserId')
        ALTER TABLE dbo.AttendanceRecords WITH CHECK
        ADD CONSTRAINT FK_AttendanceRecords_Users_RecordedByUserId
            FOREIGN KEY (RecordedByUserId) REFERENCES dbo.Users(Id);

    /* ExamAnswers.QuestionId -> Questions.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExamAnswers_Questions_QuestionId')
        ALTER TABLE dbo.ExamAnswers WITH CHECK
        ADD CONSTRAINT FK_ExamAnswers_Questions_QuestionId
            FOREIGN KEY (QuestionId) REFERENCES dbo.Questions(Id);

    /* SelectedOptionId + QuestionId -> the matching option row */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExamAnswers_QuestionOptions_SelectedOption')
        ALTER TABLE dbo.ExamAnswers WITH CHECK
        ADD CONSTRAINT FK_ExamAnswers_QuestionOptions_SelectedOption
            FOREIGN KEY (QuestionId, SelectedOptionId)
            REFERENCES dbo.QuestionOptions(QuestionId, Id);

    /* ExamAnswers.GradedByUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExamAnswers_Users_GradedByUserId')
        ALTER TABLE dbo.ExamAnswers WITH CHECK
        ADD CONSTRAINT FK_ExamAnswers_Users_GradedByUserId
            FOREIGN KEY (GradedByUserId) REFERENCES dbo.Users(Id);

    /* ExamDocuments.UploadedByUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExamDocuments_Users_UploadedByUserId')
        ALTER TABLE dbo.ExamDocuments WITH CHECK
        ADD CONSTRAINT FK_ExamDocuments_Users_UploadedByUserId
            FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id);

    /* ExamSubmissionDocuments.UploadedByUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_ExamSubmissionDocuments_Users_UploadedByUserId')
        ALTER TABLE dbo.ExamSubmissionDocuments WITH CHECK
        ADD CONSTRAINT FK_ExamSubmissionDocuments_Users_UploadedByUserId
            FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id);

    /* Exams.ResultsPublishedByUserId -> Users.Id */
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name=N'FK_Exams_Users_ResultsPublishedByUserId')
        ALTER TABLE dbo.Exams WITH CHECK
        ADD CONSTRAINT FK_Exams_Users_ResultsPublishedByUserId
            FOREIGN KEY (ResultsPublishedByUserId) REFERENCES dbo.Users(Id);

    COMMIT TRANSACTION;
    PRINT 'MFT relational integrity constraints were created or verified.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
