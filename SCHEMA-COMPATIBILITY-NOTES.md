# Mft schema compatibility patch — 2026-10-15

This patch aligns the project's identity CLR types with the supplied SQL Server schema (`uniqueidentifier`) and adds explicit EF Core column/table mappings for the columns that were confirmed in the schema output.

## Important limitation

This is **not a verified full application repair**. The runtime used to prepare this archive does not have the .NET SDK, so `dotnet build` and integration tests could not be run here. Also, the existing application design assumes that student/teacher accounts are rows in `Users`, while the supplied database has separate `Students` and `Teachers` tables. Question options/correct answers are also stored in `QuestionOptions` in the database, while the legacy application code expects JSON/correct-answer fields on `Questions`. The `AppSettings` table is absent from the supplied schema. Those areas require service/controller refactoring before every endpoint can be considered database-compatible.

## Safety

- No SQL migration is included or run.
- No database table is dropped or altered by this patch.
- `Database:ApplyMigrationsOnStartup` remains `false`.
- Before deploying, back up the database and run the app against a development copy first.

## Next verification on Windows

From the extracted project root:

```powershell
dotnet build .\src\Mft.API
```

Then test login and inspect the backend log. Do not treat this archive as production-ready until build and endpoint tests pass and the remaining schema differences listed above are resolved.
