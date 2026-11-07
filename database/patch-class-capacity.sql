/*
  Canonical class admission capacity.
  Run against the same database configured by the API connection string
  (MftDb_Canonical in the current canonical setup).
  Existing classes remain NULL until a department manager configures capacity.
*/
IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
    THROW 51501, 'dbo.CanonicalClasses was not found in the current database.', 1;

IF COL_LENGTH(N'dbo.CanonicalClasses', N'Capacity') IS NULL
BEGIN
    ALTER TABLE dbo.CanonicalClasses
        ADD Capacity int NULL;
END;

IF EXISTS
(
    SELECT 1
    FROM dbo.CanonicalClasses
    WHERE Capacity IS NOT NULL AND Capacity <= 0
)
    THROW 51502, 'Existing class capacity values must be positive.', 1;

SELECT
    Id,
    Name,
    DepartmentId,
    Capacity
FROM dbo.CanonicalClasses
ORDER BY DepartmentId, Name;
