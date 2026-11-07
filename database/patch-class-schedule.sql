-- Run against MftDb_Canonical. Safe to run repeatedly.
IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
    THROW 51420, 'dbo.CanonicalClasses was not found in the current database.', 1;

IF COL_LENGTH(N'dbo.CanonicalClasses', N'Schedule') IS NULL
    ALTER TABLE dbo.CanonicalClasses ADD Schedule nvarchar(200) NULL;

SELECT Id, Name, DepartmentId, Capacity, Schedule
FROM dbo.CanonicalClasses
ORDER BY DepartmentId, Name;
