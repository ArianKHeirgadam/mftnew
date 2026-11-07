/*
  MFT schema alignment — additive support tables for the canonical bigint schema.

  IMPORTANT:
  - This script is NOT a substitute for the application code cutover.
  - Do not execute until the API/EF Core cutover to bigint IDs has been deployed.
  - Back up the database and test this script on a restored copy first.
  - Existing tables are never altered or dropped. If a same-named table has an
    incompatible key type, the script stops and requires a reviewed migration.
  - Core tables (Users, Departments, Exams, ExamAttempts, etc.) are not changed.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Departments', N'U') IS NULL
       OR OBJECT_ID(N'dbo.Exams', N'U') IS NULL
       OR OBJECT_ID(N'dbo.ExamAttempts', N'U') IS NULL
        THROW 52000, 'Canonical core tables are missing; no changes were made.', 1;

    IF OBJECT_ID(N'dbo.Classes', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Classes') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52001, 'dbo.Classes.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.ClassMembers', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ClassMembers') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52002, 'dbo.ClassMembers.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.Notifications', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.Notifications') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52003, 'dbo.Notifications.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AttendanceRecords') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52004, 'dbo.AttendanceRecords.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.ExamDocuments', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ExamDocuments') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52005, 'dbo.ExamDocuments.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.ExamSubmissionDocuments', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.ExamSubmissionDocuments') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52006, 'dbo.ExamSubmissionDocuments.Id is not bigint. Review/migrate this table separately.', 1;
    IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NOT NULL
       AND EXISTS (SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.AppSettings') AND name=N'Id' AND TYPE_NAME(user_type_id) <> N'bigint')
        THROW 52007, 'dbo.AppSettings.Id is not bigint. Review/migrate this table separately.', 1;

    IF OBJECT_ID(N'dbo.Classes', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Classes
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Classes PRIMARY KEY,
            Name nvarchar(200) NOT NULL,
            DepartmentId bigint NOT NULL,
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_Classes_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_Classes_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX UX_Classes_DepartmentId_Name ON dbo.Classes(DepartmentId, Name);
    END;

    IF OBJECT_ID(N'dbo.ClassMembers', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ClassMembers
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ClassMembers PRIMARY KEY,
            ClassId bigint NOT NULL,
            StudentUserId bigint NOT NULL,
            AddedAt datetime2 NOT NULL CONSTRAINT DF_ClassMembers_AddedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_ClassMembers_Classes FOREIGN KEY (ClassId) REFERENCES dbo.Classes(Id) ON DELETE CASCADE,
            CONSTRAINT FK_ClassMembers_Users FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX UX_ClassMembers_ClassId_StudentUserId ON dbo.ClassMembers(ClassId, StudentUserId);
    END;

    IF OBJECT_ID(N'dbo.Notifications', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.Notifications
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
            UserId bigint NOT NULL,
            Title nvarchar(250) NOT NULL,
            Message nvarchar(max) NOT NULL,
            IsRead bit NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT (0),
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
        );
        CREATE INDEX IX_Notifications_UserId_CreatedAt ON dbo.Notifications(UserId, CreatedAt DESC);
    END;

    IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AppSettings
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
            [Key] nvarchar(100) NOT NULL,
            [Value] nvarchar(max) NOT NULL,
            UpdatedAt datetime2 NOT NULL CONSTRAINT DF_AppSettings_UpdatedAt DEFAULT SYSUTCDATETIME()
        );
        CREATE UNIQUE INDEX UX_AppSettings_Key ON dbo.AppSettings([Key]);
    END;

    IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.AttendanceRecords
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
            StudentUserId bigint NOT NULL,
            DepartmentId bigint NOT NULL,
            RecordedByUserId bigint NOT NULL,
            AttendanceDate date NOT NULL,
            Status nvarchar(20) NOT NULL,
            CreatedAt datetime2 NOT NULL CONSTRAINT DF_AttendanceRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT CK_AttendanceRecords_Status CHECK (Status IN (N'Present', N'Absent', N'Deleted')),
            CONSTRAINT FK_AttendanceRecords_Student FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
            CONSTRAINT FK_AttendanceRecords_Department FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id),
            CONSTRAINT FK_AttendanceRecords_RecordedBy FOREIGN KEY (RecordedByUserId) REFERENCES dbo.Users(Id)
        );
        CREATE UNIQUE INDEX UX_AttendanceRecords_Student_Date ON dbo.AttendanceRecords(StudentUserId, AttendanceDate);
        CREATE INDEX IX_AttendanceRecords_Department_Date ON dbo.AttendanceRecords(DepartmentId, AttendanceDate);
    END;

    IF OBJECT_ID(N'dbo.ExamDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ExamDocuments
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamDocuments PRIMARY KEY,
            ExamId bigint NOT NULL,
            StoredFileName nvarchar(100) NOT NULL,
            OriginalFileName nvarchar(255) NOT NULL,
            FileSize bigint NOT NULL,
            UploadedByUserId bigint NOT NULL,
            UploadedAt datetime2 NOT NULL CONSTRAINT DF_ExamDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_ExamDocuments_Exams FOREIGN KEY (ExamId) REFERENCES dbo.Exams(Id) ON DELETE CASCADE,
            CONSTRAINT FK_ExamDocuments_Users FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id)
        );
        CREATE UNIQUE INDEX UX_ExamDocuments_ExamId ON dbo.ExamDocuments(ExamId);
    END;

    IF OBJECT_ID(N'dbo.ExamSubmissionDocuments', N'U') IS NULL
    BEGIN
        CREATE TABLE dbo.ExamSubmissionDocuments
        (
            Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamSubmissionDocuments PRIMARY KEY,
            AttemptId bigint NOT NULL,
            StoredFileName nvarchar(100) NOT NULL,
            OriginalFileName nvarchar(255) NOT NULL,
            FileSize bigint NOT NULL,
            UploadedByUserId bigint NOT NULL,
            UploadedAt datetime2 NOT NULL CONSTRAINT DF_ExamSubmissionDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
            CONSTRAINT FK_ExamSubmissionDocuments_ExamAttempts FOREIGN KEY (AttemptId) REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE,
            CONSTRAINT FK_ExamSubmissionDocuments_Users FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id)
        );
        CREATE UNIQUE INDEX UX_ExamSubmissionDocuments_AttemptId ON dbo.ExamSubmissionDocuments(AttemptId);
    END;

    COMMIT TRANSACTION;
    PRINT 'MFT bigint support tables created or already present.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
