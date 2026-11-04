/*
  Additive schema patch for the application settings endpoint.
  Run once against MftDb_Fresh (or the configured application database).
  This script creates only the missing AppSettings table and does not alter existing data.
*/
IF OBJECT_ID(N'dbo.AppSettings', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.AppSettings
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_AppSettings PRIMARY KEY,
        [Key] nvarchar(100) NOT NULL,
        [Value] nvarchar(max) NOT NULL,
        UpdatedAt datetime2 NOT NULL
            CONSTRAINT DF_AppSettings_UpdatedAt DEFAULT (SYSUTCDATETIME())
    );

    CREATE UNIQUE INDEX UX_AppSettings_Key
        ON dbo.AppSettings([Key]);
END;
