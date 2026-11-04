IF OBJECT_ID(N'dbo.AttendanceRecords', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AttendanceRecords
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_AttendanceRecords PRIMARY KEY,
        StudentUserId UNIQUEIDENTIFIER NOT NULL,
        DepartmentId UNIQUEIDENTIFIER NOT NULL,
        RecordedByUserId UNIQUEIDENTIFIER NOT NULL,
        AttendanceDate DATE NOT NULL,
        Status NVARCHAR(20) NOT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AttendanceRecords_CreatedAt DEFAULT SYSUTCDATETIME(),
        CONSTRAINT CK_AttendanceRecords_Status CHECK (Status IN (N'Present', N'Absent', N'Deleted')),
        CONSTRAINT FK_AttendanceRecords_Student FOREIGN KEY (StudentUserId) REFERENCES dbo.Users(Id),
        CONSTRAINT FK_AttendanceRecords_Department FOREIGN KEY (DepartmentId) REFERENCES dbo.Departments(Id),
        CONSTRAINT FK_AttendanceRecords_Recorder FOREIGN KEY (RecordedByUserId) REFERENCES dbo.Users(Id)
    );
    CREATE UNIQUE INDEX UX_AttendanceRecords_Student_Date ON dbo.AttendanceRecords(StudentUserId, AttendanceDate);
    CREATE INDEX IX_AttendanceRecords_Department_Date ON dbo.AttendanceRecords(DepartmentId, AttendanceDate);
END;
