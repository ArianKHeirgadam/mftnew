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

## Database patch for question bank and exam grading

Before running the API against the existing SQL Server database, back up the
database and execute
`database/patch-exam-grading-and-department-manager.sql` against the database
named by `DefaultConnection`. This additive script creates the columns used
to persist MCQ options/correct answers, attempt status, answer correctness and
department manager IDs. Do not skip this step: the previous schema-compatibility
mapping intentionally ignored those fields.

After applying the patch, build the API and run the test sequence in
`docs/EXAM-SCORING-VERIFICATION.md`. A 20-question test with 15 correct
answers should produce 75/100. Live SQL Server verification is still required.

## Run the frontend

Open `Frontend/` in VS Code and launch `login.html` with Live Server (usually `http://127.0.0.1:5500`). The API bridge defaults to `https://localhost:7081/api`; accept/trust the local ASP.NET HTTPS development certificate when prompted. If the API is hosted elsewhere, override `window.MFT_API_BASE` before `assets/js/api.js` loads.

## Role and security model

- `SuperAdmin`: system-wide management.
- `Department`: users, exams and reporting scoped to the assigned department.
- `Teacher`: student management in their department and exams/questions they own.
- `Student`: no account-management endpoints; can start published exams and access their own attempts/results.

The role is read from the authenticated account, not selected in the login UI. Newly created users are marked `MustChangePassword`; the current frontend prompts for a replacement password after login. An SMS provider is **not yet wired**, so this bundle must not be represented as delivering temporary passwords or verification codes by SMS.

## Current verification status

- JavaScript syntax checks pass with Node.js for the files in `Frontend/assets/js/`.
- JSON configuration files parse successfully.
- The .NET SDK is not installed in the build environment, so `dotnet restore`, `dotnet build`, migrations, API integration tests, and live SQL Server tests have **not** been run here.
- User, department, question, exam, result grading, notification, profile, settings, dashboard and report workflows are now connected where supported by the supplied API. See `API-ENDPOINTS.md` and `docs/PROJECT_STATUS.md` for unsupported items and test limitations. Do not treat this working version as production-ready until the .NET build, migrations, SQL Server integration and authorization tests are completed.
