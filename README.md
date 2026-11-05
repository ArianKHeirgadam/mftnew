# Mft — .NET 8 Clean Architecture + EF Core Code First

This working tree combines the supplied frontend with an ASP.NET Core Web API. The original 17 HTML pages, CSS, image assets, and existing UI scripts are retained under `Frontend/`; `assets/js/api.js` is added as the API bridge. The login role picker and simulated role-based login redirect have been removed: the backend now determines the role after validating username/password.

## Solution layout

- `src/Mft.Domain`: domain entities and role enum.
- `src/Mft.Application`: request/response contracts and service abstractions.
- `src/Mft.Infrastructure`: EF Core SQL Server persistence, token and authentication services.
- `src/Mft.API`: REST controllers, JWT authentication, authorization policies, Swagger and health endpoint.
- `Frontend`: supplied frontend and API integration layer.

## Requirements

- .NET 8 SDK
- SQL Server 2022 (or compatible SQL Server)
- EF Core CLI (`dotnet-ef` 8.0.20)
- VS Code Live Server or another static HTTP server for the frontend

## Run the backend

1. Review `src/Mft.API/appsettings.json`. The checked-in default connection string targets the development SQL Server instance `arianslaptop\\serverarian` and database `MftDb`; change it if your local environment differs. Do not put production credentials or secrets in version control.
2. Replace `Jwt:Key` with a unique random secret of at least 32 bytes, ideally via .NET user-secrets or environment variables.
3. If this database already has an earlier Mft migration applied, create a new migration for the added exam settings (for example `dotnet ef migrations add AddExamConfiguration --project src/Mft.Infrastructure --startup-project src/Mft.API`) and then run `dotnet ef database update`. For a new database, use the initial migration commands below.
4. From the repository root:

   ```powershell
   dotnet restore
   dotnet tool install --global dotnet-ef --version 8.0.20
   dotnet ef migrations add InitialCreate --project src/Mft.Infrastructure --startup-project src/Mft.API --output-dir Persistence/Migrations
   dotnet ef database update --project src/Mft.Infrastructure --startup-project src/Mft.API
   dotnet run --project src/Mft.API
   ```

5. The launch profile uses `https://localhost:7081` and `http://localhost:5081`. Swagger is available at `/swagger` in Development and the health endpoint is `/health`.
6. Create the first SuperAdmin with environment variables before startup:

   ```powershell
   $env:Bootstrap__CreateSuperAdmin="true"
   $env:Bootstrap__Username="admin"
   $env:Bootstrap__Password="use-a-unique-long-password"
   $env:Bootstrap__FullName="System Administrator"
   dotnet run --project src/Mft.API
   ```

   Set `Bootstrap__CreateSuperAdmin=false` after initialization. Do not reuse the example password.

## Database patch for question bank, exam grading and attendance

Before running the API against the existing SQL Server database, back up the
database and execute
`database/patch-exam-grading-and-department-manager.sql` against the database
named by `DefaultConnection`. This additive script creates the columns used
to persist MCQ options/correct answers, attempt status, answer correctness and
department manager IDs. Do not skip this step: the previous schema-compatibility
mapping intentionally ignored those fields.

Also execute `database/create-attendance-records.sql` on the same development database to enable daily attendance records. Back up the database first.\n\nAfter applying the patches, build the API and run the test sequence in
`docs/EXAM-SCORING-VERIFICATION.md`. A 20-question test with 15 correct
answers should produce 75/100. Live SQL Server verification is still required.

## PDF exam workflow

The PDF exam workflow uses private storage under `src/Mft.API/App_Data/exam-files` (never under `wwwroot`). The folder is excluded from Git. The API validates the PDF extension, size (20 MB maximum), and PDF signature before saving it with a generated server-side filename.

Before using this workflow on an existing database, back up the database and run
`database/patch-exam-pdf-workflow.sql` against the application database. This is an additive, re-runnable SQL Server patch; it adds per-exam question scores, result publication metadata, manual-grading metadata, and the two PDF metadata tables. It does not delete existing rows or columns. Do not run it against production until it has been tested on a backup copy.

### Workflow endpoints

- `POST /api/exam-files/exams/{examId}`: teacher/department/super-admin uploads the exam PDF.
- `GET /api/exam-files/exams/{examId}`: authorized exam PDF download; students need the exam to be published and belong to its department.
- `POST /api/exam-files/attempts/{attemptId}`: student uploads their PDF answer while the attempt is in progress and within its server-side time limit.
- `GET /api/exam-files/attempts/{attemptId}/info`: authorized submission file metadata.
- `GET /api/exam-files/attempts/{attemptId}`: student (after result publication) or authorized grader downloads the PDF answer.
- `PUT /api/results/{attemptId}/answers/{questionId}/grade`: manual grading for one answer; body: `{"score": 2.5, "comment": "..." }`. The score is entered in that question's raw points and normalized to the exam's 100-point result.
- `POST /api/results/exams/{examId}/publish`: teacher/department/super-admin publishes results after all saved answers needing manual grading have been graded.
- `POST /api/results/exams/{examId}/unpublish`: authorized manager withdraws publication.

The existing student result endpoints return no result details until the exam's `ResultsPublished` flag is enabled. Uploads are stored outside the web root and downloaded through authenticated endpoints.

## Run the frontend

Open `Frontend/` in VS Code and launch `login.html` with Live Server (usually `http://127.0.0.1:5500`). The API bridge defaults to `https://localhost:7081/api`; accept/trust the local ASP.NET HTTPS development certificate when prompted. If the API is hosted elsewhere, override `window.MFT_API_BASE` before `assets/js/api.js` loads.

## Role and security model

- `SuperAdmin`: system-wide management.
- `Department`: users, exams and reporting scoped to the assigned department.
- `Teacher`: student management in their department and exams/questions they own.
- `Student`: no account-management endpoints; can start published exams and access their own attempts/results.

The role is read from the authenticated account, not selected in the login UI. New accounts use the password entered by the administrator/teacher at creation time. The temporary-password redirect and forced-change middleware have been removed; users may change their password later from their profile. An SMS provider is **not yet wired**, so this bundle must not be represented as delivering temporary passwords or verification codes by SMS.

## Current verification status

- A code-level scan was performed for unsafe `querySelector(...).forEach(...)` patterns; none remain in the shared JavaScript files.\n- The latest attendance, PDF-import, matching-question and modal changes have **not yet been runtime-tested** in a browser or against SQL Server.
- JSON configuration files parse successfully.
- The .NET SDK is not installed in the build environment, so `dotnet restore`, `dotnet build`, migrations, API integration tests, and live SQL Server tests have **not** been run here.
- User, department, question, exam, result grading, notification, profile, settings, dashboard and report workflows are now connected where supported by the supplied API. See `API-ENDPOINTS.md` and `docs/PROJECT_STATUS.md` for unsupported items and test limitations. Do not treat this working version as production-ready until the .NET build, migrations, SQL Server integration and authorization tests are completed.
