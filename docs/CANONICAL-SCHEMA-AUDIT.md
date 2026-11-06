# Canonical schema compatibility audit

Date: 2026-11-06
Branch: feature/pdf-exam-workflow

## Scope and safety

This is a code/schema compatibility audit based on the canonical SQL Server schema supplied by the project owner and the current branch source. It does not connect to, alter, or migrate any database. The canonical schema remains authoritative. The PR remains open and unmerged.

## Confirmed high-impact mismatches

| Area | Current application model | Canonical database | Required treatment |
|---|---|---|---|
| User identity | `Guid` | `Users.Id bigint identity` | Use `long` in persistence/API identifiers; preserve external API compatibility only through deliberate DTO/route changes |
| Department identity | `Guid` | `Departments.Id bigint identity` | Same |
| User role | `User.Role` enum column | `Roles` + `UserRoles` | Resolve role through normalized join; map DB `DepartmentAdmin` to API/policy alias `Department` |
| Phone | `Mobile` | `PhoneNumber nvarchar(20)` | Map/rename explicitly; enforce unique phone semantics |
| Password lifecycle | Admin-defined initial password only | `Users.PasswordHash` | Hash the admin-defined password with BCrypt; no forced first-login change, no `MustChangePassword` field or endpoint |
| Department | ManagerUserId, ManagerName, DegreeLevel | No such columns | Remove from persistence model; derive manager display only if a valid relationship exists elsewhere |
| Question | DepartmentId, Category, Type, Difficulty, OptionsJson, CorrectAnswer, IsActive | SkillId, QuestionTypeId, DifficultyLevel, MetadataJson | Rework question domain/persistence mapping; category is reached through Skill -> ExamCategory; options/correct answer use related tables/metadata |
| MCQ options | `QuestionOptions`, Guid key | `MultipleChoiceOptions`, bigint key | Rename and map real columns |
| Exam composition | Exam -> Questions via composite (ExamId, QuestionId) | Exam -> ExamSections -> ExamQuestions (identity Id) -> Questions | Rebuild composition around sections and identity ExamQuestions |
| Exam schedule | ScheduledAt/Status conversion in EF | StartAt, EndAt, IsPublished bit | Map exact columns and preserve both start and end |
| Exam attempt | Guid key, StudentId, string Status, Score | bigint key, StudentUserId, StatusId tinyint, AutoGradedScore, FinalScore | Use status lookup and separate score semantics |
| Answers | `ExamAnswers`, QuestionId | `StudentAnswers`, ExamQuestionId | Store answer against a specific exam-question instance; do not infer ExamQuestionId from QuestionId |
| Essay/matching | JSON fields on Question | EssayGradingCriteria and MatchingPairs tables | Persist structured child records |
| Auth | JWT generated from User.Role | UserRoles/Roles | Load role memberships before token creation; do not assume a single role unless policy is explicitly established |

## Supporting tables

The supplied schema export does not include Classes, ClassMembers, Notifications, AppSettings, AttendanceRecords, ExamDocuments, or ExamSubmissionDocuments. These must be treated as optional feature extensions. The existing additive script `database/patch-bigint-feature-tables.sql` is not approved for execution until its definitions and the code mappings are reviewed together on a restored development database.

## Authentication-first migration plan

1. Introduce canonical persistence records for Users, Departments, Roles, and UserRoles using SQL Server key types and exact column names.
2. Implement role resolution as a query over UserRoles -> Roles. Keep the stored role code `DepartmentAdmin`; translate to policy name `Department` only at the authorization boundary.
3. Map `PhoneNumber` to the API's mobile/phone concept. Confirm whether Email is optional and whether phone uniqueness is enforced by existing data.
4. Rework login and JWT claims to use bigint user and department IDs and resolved role claims.
5. Rework user and department endpoints/DTOs and route constraints as one coordinated change.
6. Add integration tests against a restored copy of the canonical SQL Server database before moving to Questions.

## Explicit blockers before coding

- The current application exposes Guid identifiers in route constraints and request/response contracts across several controllers; changing only EF entities would break the API.
- The current `User` aggregate has a single enum role, while the canonical schema permits multiple rows in UserRoles. Confirm whether multiple roles per user are intended. The persistence implementation should still query normalized roles.
- Password-change flow and `MustChangePassword` have been removed by product decision; the admin-defined initial password remains the account password.
- The canonical Departments schema has no manager column. Department manager ownership must not be persisted by inventing columns; decide whether department administration is determined only by role + Users.DepartmentId or requires a separate extension.
- No live SQL Server connection or restored database was available during this audit; no build or integration test is claimed.

## Implementation progress

- Removed `MustChangePassword` from the domain user, login and create-user contracts, bootstrap account creation, and API responses.
- Removed the `/api/auth/change-password` endpoint and the profile-page password-change panel and JavaScript handler.
- Added `CanonicalIdentityDbContext` with explicit mappings for `Users`, `Departments`, `Roles`, and `UserRoles`, using the canonical bigint/int key types and foreign-key delete behaviors. It is registered against the existing connection string but is not yet used by the live authentication flow; this avoids mixing bigint identity claims with the remaining GUID-based controllers.
- The existing legacy `AppDbContext` and GUID-based user endpoints are not yet cut over. Do not deploy against the canonical database until the coordinated identity/controller migration is complete.

## Current conclusion

The existing EF model is not compatible with the canonical schema. Do not point the current application at the canonical production database. Migrate authentication and identity first, then proceed module-by-module. Do not merge the draft PR or execute legacy GUID-based SQL scripts as part of this cutover.
