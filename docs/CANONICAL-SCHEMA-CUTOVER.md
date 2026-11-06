# Canonical SQL Server schema cutover

The SQL Server schema supplied for this project is the single source of truth. The application must target it; the schema must not be reshaped to fit the old GUID-based EF model.

## Confirmed core contract

- Ordinary entity keys including Users, Departments, Questions, Exams, ExamAttempts, ExamQuestions, and StudentAnswers are bigint identity values.
- Roles and Permissions IDs are int; ExamAttemptStatuses.Id is tinyint.
- OtpCodes.Id and RefreshTokens.Id remain uniqueidentifier.
- User roles are normalized through UserRoles(UserId, RoleId) and Roles; there is no Users.Role column.
- Question kinds are looked up through QuestionTypes: MultipleChoice, Essay, Matching.
- Exam attempts reference ExamAttemptStatuses; StudentAnswers.ExamQuestionId references the identity key of ExamQuestions.
- MCQ options are in MultipleChoiceOptions; matching pairs are in MatchingPairs; essay criteria are in EssayGradingCriteria.
- Exam composition is Exams -> ExamSections -> ExamQuestions -> Questions, with sections referencing Skills and ExamCategories.

## Role name mapping

The database values are authoritative: SuperAdmin, DepartmentAdmin, Teacher, Student. The application may expose Department as its authorization-policy name, but must map it to DepartmentAdmin without changing the stored role.

## Unique constraints that application logic must preserve

- Users.Username and Users.PhoneNumber
- ExamAttempts(ExamId, StudentUserId)
- ExamQuestions has its own Id; it is not identified by (ExamId, QuestionId).
- StudentAnswers(ExamAttemptId, ExamQuestionId)
- Skills(ExamCategoryId, Name)
- ExamCategories.Name, QuestionTypes.Code, ExamAttemptStatuses.Code, Permissions.Code

## Supporting feature tables

The supplied schema export did not include all UI feature tables. The additive script database/patch-bigint-feature-tables.sql creates missing classes, class members, notifications, settings, attendance, and PDF metadata tables with bigint foreign keys to canonical core tables. It never alters existing core tables and stops if a same-named support table has a non-bigint primary key.

Do not execute that script until the application code has completed the bigint cutover. Back up and test on a restored development database first. Existing GUID-based scripts for these features are legacy artifacts and must not be used with the canonical database.

## Implementation sequence

1. Canonical identity persistence records and mappings for Users, Departments, Roles, and UserRoles are implemented in CanonicalIdentityDbContext.
2. Authentication/JWT and identity endpoints now resolve roles from UserRoles/Roles. Preserve the policy alias Department only at the API boundary.
3. Rebuild exam and answer persistence around ExamSections, ExamQuestions.Id, ExamAttempts.StatusId, and StudentAnswers.ExamQuestionId.
4. Align PDF, attendance, class, notification, and settings models with the additive bigint support schema.
5. Canonical identity controller route constraints and DTOs now use bigint IDs; account creation requires a unique PhoneNumber, and department manager display is derived from DepartmentAdmin + Users.DepartmentId (no noncanonical manager column).
6. Remaining feature controllers, DTOs, and frontend identifiers still require coordinated bigint migration. Generate a schema diff and test against a restored copy. Never run a destructive migration against the primary database.

## Verification status

Implemented in this phase: canonical identity DbContext, login/password verification against Users, JWT role claims from UserRoles/Roles, bigint identity DTOs/routes for auth/users/departments, and DepartmentAdmin-to-Department policy mapping. Admin-defined passwords are BCrypt-hashed; there is no forced password-change flow.

Not yet a completed application cutover: other feature controllers still use the legacy GUID-based AppDbContext and old table/column names. The legacy context remains registered to avoid deleting dependencies prematurely, but it is not compatible with the supplied canonical schema. Automatic EF migrations are explicitly blocked at startup. No .NET build or SQL Server integration test is claimed; test on a restored canonical database before deployment. Do not run legacy GUID SQL scripts or deploy this partial cutover against production.