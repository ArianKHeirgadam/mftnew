-- Run this script in MftDb_Canonical.
-- Idempotently adds the optional class schedule without changing Capacity.
IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
    THROW 51420, 'dbo.CanonicalClasses was not found in the current database.', 1;

IF COL_LENGTH(N'dbo.CanonicalClasses', N'Schedule') IS NULL
BEGIN
    ALTER TABLE dbo.CanonicalClasses ADD Schedule nvarchar(200) NULL;
END;

-- Dynamic SQL avoids SQL Server compiling Schedule before ALTER TABLE runs.
EXEC sys.sp_executesql N'
    SELECT Id, Name, DepartmentId, Capacity, Schedule
    FROM dbo.CanonicalClasses
    ORDER BY DepartmentId, Name;
';
