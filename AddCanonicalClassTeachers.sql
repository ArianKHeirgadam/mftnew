/*
  Adds explicit teacher-to-class assignments for the canonical academic schema.
  Safe to run more than once. Does not drop or alter existing data.
*/
IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
    THROW 52110, 'CanonicalClasses table was not found. Select MftDb_Canonical first.', 1;
IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
    THROW 52111, 'Users table was not found. Select MftDb_Canonical first.', 1;

IF OBJECT_ID(N'dbo.CanonicalClassTeachers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CanonicalClassTeachers
    (
        Id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CanonicalClassTeachers PRIMARY KEY,
        ClassId BIGINT NOT NULL,
        TeacherUserId BIGINT NOT NULL,
        AddedAt DATETIME2 NOT NULL CONSTRAINT DF_CanonicalClassTeachers_AddedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_CanonicalClassTeachers_CanonicalClasses
            FOREIGN KEY (ClassId) REFERENCES dbo.CanonicalClasses(Id) ON DELETE CASCADE,
        CONSTRAINT FK_CanonicalClassTeachers_Users
            FOREIGN KEY (TeacherUserId) REFERENCES dbo.Users(Id)
    );

    CREATE UNIQUE INDEX UX_CanonicalClassTeachers_ClassId_TeacherUserId
        ON dbo.CanonicalClassTeachers(ClassId, TeacherUserId);
    CREATE INDEX IX_CanonicalClassTeachers_TeacherUserId
        ON dbo.CanonicalClassTeachers(TeacherUserId);
END;
