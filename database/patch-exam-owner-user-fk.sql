/*
    Migrate exam ownership from Teachers.Id to Users.Id.
    Run against a BACKED-UP development database first.
    Safe to re-run after a successful migration. Existing exams are preserved.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
        THROW 51200, 'dbo.Exams table was not found.', 1;
    IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
        THROW 51201, 'dbo.Users table was not found.', 1;
    IF OBJECT_ID(N'dbo.Teachers', N'U') IS NULL
        THROW 51202, 'dbo.Teachers table was not found.', 1;

    IF COL_LENGTH(N'dbo.Exams', N'TeacherId') IS NOT NULL
       AND COL_LENGTH(N'dbo.Exams', N'CreatedByUserId') IS NOT NULL
        THROW 51203, 'Both TeacherId and CreatedByUserId exist on dbo.Exams; inspect the schema manually.', 1;

    IF COL_LENGTH(N'dbo.Exams', N'TeacherId') IS NOT NULL
    BEGIN
        IF EXISTS (
            SELECT 1
            FROM dbo.Exams e
            LEFT JOIN dbo.Teachers t ON t.Id = e.TeacherId
            LEFT JOIN dbo.Users u ON u.Id = t.UserId
            WHERE t.Id IS NULL OR u.Id IS NULL
        )
            THROW 51204, 'An existing exam owner cannot be mapped from Teachers.Id to Users.Id; migration stopped.', 1;

        DECLARE @dropFkSql nvarchar(max) = N'';
        SELECT @dropFkSql +=
            N'ALTER TABLE dbo.Exams DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';'
        FROM sys.foreign_keys fk
        INNER JOIN sys.foreign_key_columns fkc
            ON fkc.constraint_object_id = fk.object_id
        INNER JOIN sys.columns c
            ON c.object_id = fkc.parent_object_id
           AND c.column_id = fkc.parent_column_id
        WHERE fk.parent_object_id = OBJECT_ID(N'dbo.Exams')
          AND c.name = N'TeacherId';

        IF @dropFkSql <> N''
            EXEC sys.sp_executesql @dropFkSql;

        UPDATE e
        SET e.TeacherId = t.UserId
        FROM dbo.Exams e
        INNER JOIN dbo.Teachers t ON t.Id = e.TeacherId;

        EXEC sys.sp_rename
            @objname = N'dbo.Exams.TeacherId',
            @newname = N'CreatedByUserId',
            @objtype = N'COLUMN';
    END;

    IF COL_LENGTH(N'dbo.Exams', N'CreatedByUserId') IS NULL
        THROW 51205, 'Neither TeacherId nor CreatedByUserId exists on dbo.Exams.', 1;

    EXEC sys.sp_executesql N'
        IF EXISTS (
            SELECT 1
            FROM dbo.Exams e
            LEFT JOIN dbo.Users u ON u.Id = e.CreatedByUserId
            WHERE u.Id IS NULL
        )
            THROW 51206, ''An exam creator does not exist in dbo.Users; FK was not created.'', 1;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.foreign_keys fk
            INNER JOIN sys.foreign_key_columns fkc
                ON fkc.constraint_object_id = fk.object_id
            INNER JOIN sys.columns sourceColumn
                ON sourceColumn.object_id = fkc.parent_object_id
               AND sourceColumn.column_id = fkc.parent_column_id
            INNER JOIN sys.tables targetTable
                ON targetTable.object_id = fk.referenced_object_id
            INNER JOIN sys.columns targetColumn
                ON targetColumn.object_id = fkc.referenced_object_id
               AND targetColumn.column_id = fkc.referenced_column_id
            WHERE fk.parent_object_id = OBJECT_ID(N''dbo.Exams'')
              AND sourceColumn.name = N''CreatedByUserId''
              AND targetTable.name = N''Users''
              AND targetColumn.name = N''Id''
        )
        BEGIN
            ALTER TABLE dbo.Exams WITH CHECK
            ADD CONSTRAINT FK_Exams_Users_CreatedByUserId
                FOREIGN KEY (CreatedByUserId) REFERENCES dbo.Users(Id);
            ALTER TABLE dbo.Exams
                CHECK CONSTRAINT FK_Exams_Users_CreatedByUserId;
        END;

        IF NOT EXISTS (
            SELECT 1
            FROM sys.indexes
            WHERE object_id = OBJECT_ID(N''dbo.Exams'')
              AND name = N''IX_Exams_CreatedByUserId''
        )
            CREATE INDEX IX_Exams_CreatedByUserId ON dbo.Exams(CreatedByUserId);
    ';

    COMMIT TRANSACTION;
    PRINT 'Exam creator ownership now references dbo.Users(Id).';
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
