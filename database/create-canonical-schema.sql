/*
  MFT canonical SQL Server schema bootstrap.
  Run ONLY against a newly created, empty database after reviewing the target.
  This script intentionally refuses to overwrite existing core tables.
  It creates the tables mapped by CanonicalIdentityDbContext and
  CanonicalAcademicDbContext. Legacy AppDbContext endpoints are not covered.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
    THROW 52100, 'Select a dedicated application database before running this script.', 1;

IF EXISTS (
    SELECT 1 FROM sys.tables
    WHERE name IN (N'Users',N'Departments',N'Roles',N'UserRoles',N'Questions',N'Exams',N'ExamAttempts')
      AND schema_id = SCHEMA_ID(N'dbo')
)
    THROW 52101, 'Existing core tables detected. Use a newly created empty database; this script will not drop or alter tables.', 1;

BEGIN TRY
BEGIN TRANSACTION;

CREATE TABLE dbo.Departments (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Departments_IsActive DEFAULT (1),
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Departments_CreatedAt DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_Departments_Name ON dbo.Departments(Name);

CREATE TABLE dbo.Users (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
    Username nvarchar(200) NOT NULL,
    PasswordHash nvarchar(600) NOT NULL,
    FullName nvarchar(400) NOT NULL,
    PhoneNumber nvarchar(40) NULL,
    Email nvarchar(512) NULL,
    DepartmentId bigint NULL,
    IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Users_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Users_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users(Username);
EXEC(N'CREATE UNIQUE INDEX UX_Users_PhoneNumber_NotNull ON dbo.Users(PhoneNumber) WHERE PhoneNumber IS NOT NULL;');
CREATE INDEX IX_Users_DepartmentId ON dbo.Users(DepartmentId);

CREATE TABLE dbo.Roles (
    Id int NOT NULL CONSTRAINT PK_Roles PRIMARY KEY,
    Name nvarchar(50) NOT NULL
);
CREATE UNIQUE INDEX UX_Roles_Name ON dbo.Roles(Name);

CREATE TABLE dbo.UserRoles (
    UserId bigint NOT NULL,
    RoleId int NOT NULL,
    CONSTRAINT PK_UserRoles PRIMARY KEY (UserId, RoleId),
    CONSTRAINT FK_UserRoles_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_UserRoles_Roles FOREIGN KEY (RoleId) REFERENCES dbo.Roles(Id) ON DELETE NO ACTION
);

CREATE TABLE dbo.ExamCategories (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamCategories PRIMARY KEY,
    Name nvarchar(150) NOT NULL
);
CREATE UNIQUE INDEX UX_ExamCategories_Name ON dbo.ExamCategories(Name);

CREATE TABLE dbo.Skills (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Skills PRIMARY KEY,
    ExamCategoryId bigint NOT NULL,
    Name nvarchar(100) NOT NULL,
    CONSTRAINT FK_Skills_ExamCategories FOREIGN KEY (ExamCategoryId) REFERENCES dbo.ExamCategories(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_Skills_Category_Name ON dbo.Skills(ExamCategoryId, Name);

CREATE TABLE dbo.QuestionTypes (
    Id bigint NOT NULL CONSTRAINT PK_QuestionTypes PRIMARY KEY,
    Code nvarchar(50) NOT NULL,
    Name nvarchar(100) NOT NULL
);
CREATE UNIQUE INDEX UX_QuestionTypes_Code ON dbo.QuestionTypes(Code);

CREATE TABLE dbo.Questions (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Questions PRIMARY KEY,
    SkillId bigint NOT NULL,
    QuestionTypeId bigint NOT NULL,
    Text nvarchar(max) NOT NULL,
    Points decimal(6,2) NOT NULL CONSTRAINT DF_Questions_Points DEFAULT (1),
    DifficultyLevel tinyint NULL,
    CreatedByUserId bigint NOT NULL,
    MetadataJson nvarchar(max) NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Questions_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Questions_Skills FOREIGN KEY (SkillId) REFERENCES dbo.Skills(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Questions_QuestionTypes FOREIGN KEY (QuestionTypeId) REFERENCES dbo.QuestionTypes(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Questions_Users FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_Questions_CreatedByUserId ON dbo.Questions(CreatedByUserId);
CREATE INDEX IX_Questions_QuestionTypeId ON dbo.Questions(QuestionTypeId);
CREATE INDEX IX_Questions_SkillId ON dbo.Questions(SkillId);

CREATE TABLE dbo.MultipleChoiceOptions (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MultipleChoiceOptions PRIMARY KEY,
    QuestionId bigint NOT NULL,
    OptionText nvarchar(max) NOT NULL,
    IsCorrect bit NOT NULL CONSTRAINT DF_MultipleChoiceOptions_IsCorrect DEFAULT (0),
    DisplayOrder int NOT NULL,
    CONSTRAINT FK_MultipleChoiceOptions_Questions FOREIGN KEY (QuestionId) REFERENCES dbo.Questions(Id) ON DELETE CASCADE
);
CREATE INDEX IX_MultipleChoiceOptions_QuestionId ON dbo.MultipleChoiceOptions(QuestionId);

CREATE TABLE dbo.MatchingPairs (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MatchingPairs PRIMARY KEY,
    QuestionId bigint NOT NULL,
    LeftItem nvarchar(max) NOT NULL,
    RightItem nvarchar(max) NOT NULL,
    DisplayOrder int NOT NULL,
    CONSTRAINT FK_MatchingPairs_Questions FOREIGN KEY (QuestionId) REFERENCES dbo.Questions(Id) ON DELETE CASCADE
);
CREATE INDEX IX_MatchingPairs_QuestionId ON dbo.MatchingPairs(QuestionId);

CREATE TABLE dbo.EssayGradingCriteria (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_EssayGradingCriteria PRIMARY KEY,
    QuestionId bigint NOT NULL,
    CriteriaText nvarchar(max) NOT NULL,
    MaxPoints decimal(6,2) NOT NULL,
    CONSTRAINT FK_EssayGradingCriteria_Questions FOREIGN KEY (QuestionId) REFERENCES dbo.Questions(Id) ON DELETE CASCADE
);
CREATE INDEX IX_EssayGradingCriteria_QuestionId ON dbo.EssayGradingCriteria(QuestionId);

CREATE TABLE dbo.CanonicalClasses (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CanonicalClasses PRIMARY KEY,
    Name nvarchar(200) NOT NULL,
    DepartmentId bigint NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_CanonicalClasses_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CanonicalClasses_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_CanonicalClasses_Department_Name ON dbo.CanonicalClasses(DepartmentId, Name);
CREATE INDEX IX_CanonicalClasses_DepartmentId ON dbo.CanonicalClasses(DepartmentId);

CREATE TABLE dbo.CanonicalClassMembers (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CanonicalClassMembers PRIMARY KEY,
    ClassId bigint NOT NULL,
    StudentUserId bigint NOT NULL,
    AddedAt datetime2 NOT NULL CONSTRAINT DF_CanonicalClassMembers_AddedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_CanonicalClassMembers_Classes FOREIGN KEY (ClassId) REFERENCES dbo.CanonicalClasses(Id) ON DELETE CASCADE,
    CONSTRAINT FK_CanonicalClassMembers_Users FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_CanonicalClassMembers_Class_Student ON dbo.CanonicalClassMembers(ClassId, StudentUserId);
CREATE INDEX IX_CanonicalClassMembers_StudentUserId ON dbo.CanonicalClassMembers(StudentUserId);

CREATE TABLE dbo.Exams (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Exams PRIMARY KEY,
    Title nvarchar(300) NOT NULL,
    ExamCategoryId bigint NOT NULL,
    DepartmentId bigint NOT NULL,
    ClassId bigint NULL,
    CreatedByUserId bigint NOT NULL,
    StartAt datetime2 NOT NULL,
    EndAt datetime2 NOT NULL,
    DurationMinutes int NOT NULL,
    IsPublished bit NOT NULL,
    ResultsPublished bit NOT NULL CONSTRAINT DF_Exams_ResultsPublished DEFAULT 0,
    ResultsPublishedAt datetime2 NULL,
    ResultsPublishedByUserId bigint NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Exams_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Exams_ExamCategories FOREIGN KEY (ExamCategoryId) REFERENCES dbo.ExamCategories(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Exams_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Exams_Users FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Exams_Classes FOREIGN KEY (ClassId) REFERENCES dbo.CanonicalClasses(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_Exams_ResultsPublishedBy FOREIGN KEY (ResultsPublishedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_Exams_CreatedByUserId ON dbo.Exams(CreatedByUserId);
CREATE INDEX IX_Exams_DepartmentId ON dbo.Exams(DepartmentId);
CREATE INDEX IX_Exams_ExamCategoryId ON dbo.Exams(ExamCategoryId);
CREATE INDEX IX_Exams_ClassId ON dbo.Exams(ClassId);

CREATE TABLE dbo.ExamSections (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamSections PRIMARY KEY,
    ExamId bigint NOT NULL,
    SkillId bigint NOT NULL,
    DurationMinutes int NOT NULL,
    DisplayOrder int NOT NULL,
    WeightPercent decimal(5,2) NOT NULL,
    CONSTRAINT FK_ExamSections_Exams FOREIGN KEY (ExamId) REFERENCES dbo.Exams(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ExamSections_Skills FOREIGN KEY (SkillId) REFERENCES dbo.Skills(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_ExamSections_ExamId ON dbo.ExamSections(ExamId);
CREATE INDEX IX_ExamSections_SkillId ON dbo.ExamSections(SkillId);

CREATE TABLE dbo.ExamQuestions (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamQuestions PRIMARY KEY,
    ExamSectionId bigint NOT NULL,
    QuestionId bigint NOT NULL,
    DisplayOrder int NOT NULL,
    PointsOverride decimal(6,2) NULL,
    CONSTRAINT FK_ExamQuestions_ExamSections FOREIGN KEY (ExamSectionId) REFERENCES dbo.ExamSections(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ExamQuestions_Questions FOREIGN KEY (QuestionId) REFERENCES dbo.Questions(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_ExamQuestions_ExamSectionId ON dbo.ExamQuestions(ExamSectionId);
CREATE INDEX IX_ExamQuestions_QuestionId ON dbo.ExamQuestions(QuestionId);

CREATE TABLE dbo.ExamAttemptStatuses (
    Id tinyint NOT NULL CONSTRAINT PK_ExamAttemptStatuses PRIMARY KEY,
    Code nvarchar(30) NOT NULL
);
CREATE UNIQUE INDEX UX_ExamAttemptStatuses_Code ON dbo.ExamAttemptStatuses(Code);

CREATE TABLE dbo.ExamAttempts (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamAttempts PRIMARY KEY,
    ExamId bigint NOT NULL,
    StudentUserId bigint NOT NULL,
    StartedAt datetime2 NOT NULL CONSTRAINT DF_ExamAttempts_StartedAt DEFAULT SYSUTCDATETIME(),
    SubmittedAt datetime2 NULL,
    StatusId tinyint NOT NULL,
    AutoGradedScore decimal(7,2) NULL,
    FinalScore decimal(7,2) NULL,
    CONSTRAINT FK_ExamAttempts_Exams FOREIGN KEY (ExamId) REFERENCES dbo.Exams(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_ExamAttempts_Users FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_ExamAttempts_Statuses FOREIGN KEY (StatusId) REFERENCES dbo.ExamAttemptStatuses(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_ExamAttempts_Exam_Student ON dbo.ExamAttempts(ExamId, StudentUserId);
CREATE INDEX IX_ExamAttempts_StudentUserId ON dbo.ExamAttempts(StudentUserId);

CREATE TABLE dbo.StudentAnswers (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_StudentAnswers PRIMARY KEY,
    ExamAttemptId bigint NOT NULL,
    ExamQuestionId bigint NOT NULL,
    AnswerJson nvarchar(max) NOT NULL,
    IsAutoGraded bit NOT NULL,
    Score decimal(6,2) NULL,
    GradedByUserId bigint NULL,
    GradedAt datetime2 NULL,
    CONSTRAINT FK_StudentAnswers_ExamAttempts FOREIGN KEY (ExamAttemptId) REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE,
    CONSTRAINT FK_StudentAnswers_ExamQuestions FOREIGN KEY (ExamQuestionId) REFERENCES dbo.ExamQuestions(Id) ON DELETE NO ACTION,
    CONSTRAINT FK_StudentAnswers_GradedByUsers FOREIGN KEY (GradedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE INDEX IX_StudentAnswers_ExamAttemptId ON dbo.StudentAnswers(ExamAttemptId);
CREATE INDEX IX_StudentAnswers_ExamQuestionId ON dbo.StudentAnswers(ExamQuestionId);
CREATE UNIQUE INDEX UX_StudentAnswers_Attempt_Question ON dbo.StudentAnswers(ExamAttemptId, ExamQuestionId);

-- Operational tables used by the canonical notifications, settings and attendance APIs.
CREATE TABLE dbo.Notifications (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
    UserId bigint NOT NULL,
    Title nvarchar(250) NOT NULL,
    Message nvarchar(max) NOT NULL,
    IsRead bit NOT NULL CONSTRAINT DF_Notifications_IsRead DEFAULT (0),
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Notifications_Users FOREIGN KEY (UserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
);
CREATE INDEX IX_Notifications_UserId_CreatedAt ON dbo.Notifications(UserId, CreatedAt DESC);

CREATE TABLE dbo.AppSettings (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
    [Key] nvarchar(100) NOT NULL,
    [Value] nvarchar(max) NOT NULL,
    UpdatedAt datetime2 NOT NULL CONSTRAINT DF_AppSettings_UpdatedAt DEFAULT SYSUTCDATETIME()
);
CREATE UNIQUE INDEX UX_AppSettings_Key ON dbo.AppSettings([Key]);

CREATE TABLE dbo.AttendanceRecords (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
    StudentUserId bigint NOT NULL,
    DepartmentId bigint NOT NULL,
    RecordedByUserId bigint NOT NULL,
    AttendanceDate date NOT NULL,
    Status nvarchar(20) NOT NULL,
    CreatedAt datetime2 NOT NULL CONSTRAINT DF_AttendanceRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AttendanceRecords_Student FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE,
    CONSTRAINT FK_AttendanceRecords_Department FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE CASCADE,
    CONSTRAINT FK_AttendanceRecords_RecordedBy FOREIGN KEY (RecordedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_AttendanceRecords_Student_Date ON dbo.AttendanceRecords(StudentUserId, AttendanceDate);
CREATE INDEX IX_AttendanceRecords_Department_Date ON dbo.AttendanceRecords(DepartmentId, AttendanceDate);

-- Private PDF metadata tables. Binary files remain outside wwwroot in protected storage.
CREATE TABLE dbo.ExamDocuments (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamDocuments PRIMARY KEY,
    ExamId bigint NOT NULL,
    StoredFileName nvarchar(100) NOT NULL,
    OriginalFileName nvarchar(255) NOT NULL,
    FileSize bigint NOT NULL,
    UploadedByUserId bigint NOT NULL,
    UploadedAt datetime2 NOT NULL CONSTRAINT DF_ExamDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ExamDocuments_Exams FOREIGN KEY (ExamId) REFERENCES dbo.Exams(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ExamDocuments_Users FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_ExamDocuments_ExamId ON dbo.ExamDocuments(ExamId);
CREATE INDEX IX_ExamDocuments_UploadedByUserId ON dbo.ExamDocuments(UploadedByUserId);

CREATE TABLE dbo.ExamSubmissionDocuments (
    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_ExamSubmissionDocuments PRIMARY KEY,
    AttemptId bigint NOT NULL,
    StoredFileName nvarchar(100) NOT NULL,
    OriginalFileName nvarchar(255) NOT NULL,
    FileSize bigint NOT NULL,
    UploadedByUserId bigint NOT NULL,
    UploadedAt datetime2 NOT NULL CONSTRAINT DF_ExamSubmissionDocuments_UploadedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_ExamSubmissionDocuments_ExamAttempts FOREIGN KEY (AttemptId) REFERENCES dbo.ExamAttempts(Id) ON DELETE CASCADE,
    CONSTRAINT FK_ExamSubmissionDocuments_Users FOREIGN KEY (UploadedByUserId) REFERENCES dbo.Users(Id) ON DELETE NO ACTION
);
CREATE UNIQUE INDEX UX_ExamSubmissionDocuments_AttemptId ON dbo.ExamSubmissionDocuments(AttemptId);
CREATE INDEX IX_ExamSubmissionDocuments_UploadedByUserId ON dbo.ExamSubmissionDocuments(UploadedByUserId);

INSERT dbo.Roles(Id, Name) VALUES
    (1, N'SuperAdmin'),
    (2, N'DepartmentAdmin'),
    (3, N'Teacher'),
    (4, N'Student');

INSERT dbo.QuestionTypes(Id, Code, Name) VALUES
    (1, N'MultipleChoice', N'چندگزینه‌ای'),
    (2, N'Essay', N'تشریحی'),
    (3, N'Matching', N'تطبیقی');

INSERT dbo.ExamAttemptStatuses(Id, Code) VALUES
    (1, N'InProgress'),
    (2, N'Submitted'),
    (3, N'PartiallyGraded'),
    (4, N'FullyGraded');

COMMIT TRANSACTION;
PRINT 'Canonical schema created successfully. Create the initial SuperAdmin through the configured bootstrap settings.';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
