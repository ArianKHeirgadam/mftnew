/*
    Migrate question ownership from Teachers.Id to Users.Id.

    Why:
      Questions.CreatedByTeacherId currently stores User.UserId() in the API,
      but the database FK points to Teachers.Id. This migration translates
      existing teacher-owned questions through Teachers.UserId, renames the
      column to CreatedByUserId, and points the FK at Users(Id).

    Run once against a BACKUP / development copy first.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Questions', N'U') IS NULL
        THROW 51000, 'dbo.Questions table was not found.', 1;

    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        THROW 51001, 'dbo.Users table was not found.', 1;

    IF OBJECT_ID(N'dbo.Teachers', N'U') IS NULL
        THROW 51002, 'dbo.Teachers table was not found.', 1;

    IF COL_LENGTH(N'dbo.Questions', N'CreatedByTeacherId') IS NOT NULL
    BEGIN
        -- Every legacy owner must be resolvable before changing any data.
        IF EXISTS (
            SELECT 1
            FROM dbo.Questions q
            LEFT JOIN dbo.Teachers t ON t.Id = q.CreatedByTeacherId
            WHERE t.Id IS NULL
        )
            THROW 51003, 'A question owner has no matching Teachers.Id; migration stopped without changing ownership.', 1;

        -- A single user must not map to multiple teacher profiles.
        IF EXISTS (
            SELECT t.UserId
            FROM dbo.Teachers t
            GROUP BY t.UserId
            HAVING COUNT_BIG(*) > 1
        )
            THROW 51004, 'Multiple teacher profiles map to the same user; resolve duplicates before migration.', 1;

        -- Ensure translated owner IDs are valid Users before dropping the old FK.
        IF EXISTS (
            SELECT 1
            FROM dbo.Questions q
            INNER JOIN dbo.Teachers t ON t.Id = q.CreatedByTeacherId
            LEFT JOIN dbo.Users u ON u.Id = t.UserId
            WHERE u.Id IS NULL
        )
            THROW 51005, 'A teacher profile references a missing Users row; migration stopped.', 1;

        -- Drop the legacy FK by its actual column, even if its name differs.
        DECLARE @dropFkSql nvarchar(max) = N'';
        SELECT @dropFkSql = @dropFkSql +
            N'ALTER TABLE dbo.Questions DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns col
            ON col.object_id = fkc.parent_object_id
           AND col.column_id = fkc.parent_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Questions')
          AND col.name = N'CreatedByTeacherId';

        IF @dropFkSql <> N''
            EXEC sys.sp_executesql @dropFkSql;

        -- Translate legacy Teacher IDs to their associated User IDs.
        UPDATE q
        SET q.CreatedByTeacherId = t.UserId
        FROM dbo.Questions q
        INNER JOIN dbo.Teachers t ON t.Id = q.CreatedByTeacherId;

        EXEC sys.sp_rename
            @objname = N'dbo.Questions.CreatedByTeacherId',
            @newname = N'CreatedByUserId',
            @objtype = N'COLUMN';
    END;

    IF COL_LENGTH(N'dbo.Questions', N'CreatedByUserId') IS NULL
        THROW 51006, 'Neither legacy nor migrated question owner column was found.', 1;

    IF EXISTS (
        SELECT 1
        FROM dbo.Questions q
        LEFT JOIN dbo.Users u ON u.Id = q.CreatedByUserId
        WHERE u.Id IS NULL
    )
        THROW 51007, 'A question owner does not exist in dbo.Users; user FK was not created.', 1;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.foreign_keys
        WHERE name = N'FK_Questions_Users_CreatedByUserId'
          AND parent_object_id = OBJECT_ID(N'dbo.Questions')
    )
    BEGIN
        ALTER TABLE dbo.Questions WITH CHECK
        ADD CONSTRAINT FK_Questions_Users_CreatedByUserId
            FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id);

        ALTER TABLE dbo.Questions
            CHECK CONSTRAINT FK_Questions_Users_CreatedByUserId;
    END;

    IF NOT EXISTS (
        SELECT 1
        FROM sys.indexes
        WHERE object_id = OBJECT_ID(N'dbo.Questions')
          AND name = N'IX_Questions_CreatedByUserId'
    )
    BEGIN
        CREATE INDEX IX_Questions_CreatedByUserId
            ON dbo.Questions(CreatedByUserId);
    END;

    COMMIT TRANSACTION;
    PRINT 'Question creator ownership now references dbo.Users(Id).';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
