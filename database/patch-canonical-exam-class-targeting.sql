/*
  Canonical class roster and exam targeting schema.
  Uses separate table names to avoid collisions with legacy GUID-based Classes
  and ClassMembers tables. Run after canonical dbo.Users, dbo.Departments,
  and dbo.Exams have been migrated to bigint keys.
  This patch is additive; existing Exams remain unassigned (ClassId = NULL).
*/
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL OR OBJECT_ID(N'dbo.Departments', N'U') IS NULL
    THROW 52030, 'Canonical Users and Departments tables must exist first.', 1;
IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 52031, 'Canonical Exams table was not found.', 1;

IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanonicalClasses
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CanonicalClasses PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        DepartmentId bigint NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_CanonicalClasses_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CanonicalClasses_Departments FOREIGN KEY (DepartmentId)
            REFERENCES dbo.Departments(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_CanonicalClasses_DepartmentId_Name
        ON dbo.CanonicalClasses(DepartmentId, Name);
    CREATE INDEX IX_CanonicalClasses_DepartmentId
        ON dbo.CanonicalClasses(DepartmentId);
END;

IF OBJECT_ID(N'dbo.CanonicalClassMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanonicalClassMembers
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_CanonicalClassMembers PRIMARY KEY,
        ClassId bigint NOT NULL,
        StudentUserId bigint NOT NULL,
        AddedAt datetime2 NOT NULL CONSTRAINT DF_CanonicalClassMembers_AddedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CanonicalClassMembers_Classes FOREIGN KEY (ClassId)
            REFERENCES dbo.CanonicalClasses(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CanonicalClassMembers_Users FOREIGN KEY (StudentUserId)
            REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_CanonicalClassMembers_ClassId_StudentUserId
        ON dbo.CanonicalClassMembers(ClassId, StudentUserId);
    CREATE INDEX IX_CanonicalClassMembers_StudentUserId
        ON dbo.CanonicalClassMembers(StudentUserId);
END;

IF COL_LENGTH(N'dbo.Exams', N'ClassId') IS NULL
    ALTER TABLE dbo.Exams ADD ClassId bigint NULL;

IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys
    WHERE name = N'FK_Exams_CanonicalClasses_ClassId'
      AND parent_object_id = OBJECT_ID(N'dbo.Exams')
)
    ALTER TABLE dbo.Exams WITH CHECK
        ADD CONSTRAINT FK_Exams_CanonicalClasses_ClassId
        FOREIGN KEY (ClassId) REFERENCES dbo.CanonicalClasses(Id) ON DELETE NO ACTION;

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE name = N'IX_Exams_ClassId' AND object_id = OBJECT_ID(N'dbo.Exams')
)
    CREATE INDEX IX_Exams_ClassId ON dbo.Exams(ClassId);
