/* Create class roster tables for class-targeted notifications. Run once on the MFT database. */
IF OBJECT_ID(N'dbo.Classes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Classes (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_Classes PRIMARY KEY,
        Name nvarchar(200) NOT NULL,
        DepartmentId uniqueidentifier NOT NULL,
        CreatedAt datetime2 NOT NULL CONSTRAINT DF_Classes_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_Classes_Departments FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_Classes_DepartmentId_Name ON dbo.Classes(DepartmentId, Name);
END;
IF OBJECT_ID(N'dbo.ClassMembers', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClassMembers (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ClassMembers PRIMARY KEY,
        ClassId uniqueidentifier NOT NULL,
        StudentUserId uniqueidentifier NOT NULL,
        AddedAt datetime2 NOT NULL CONSTRAINT DF_ClassMembers_AddedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT FK_ClassMembers_Classes FOREIGN KEY (ClassId) REFERENCES dbo.Classes(Id) ON DELETE CASCADE,
        CONSTRAINT FK_ClassMembers_Users FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id) ON DELETE CASCADE
    );
    CREATE UNIQUE INDEX UX_ClassMembers_ClassId_StudentUserId ON dbo.ClassMembers(ClassId, StudentUserId);
END;
GO
