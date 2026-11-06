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

1. Replace GUID-based core entities with confirmed key types and explicit lookup entities/navigation mappings.
2. Move authorization from User.Role to UserRoles/Roles. Preserve the policy alias Department only at the API boundary.
3. Rebuild exam and answer persistence around ExamSections, ExamQuestions.Id, ExamAttempts.StatusId, and StudentAnswers.ExamQuestionId.
4. Align PDF, attendance, class, notification, and settings models with the additive bigint support schema.
5. Update controller route constraints, DTOs, JWT claims, and frontend identifiers together.
6. Generate a migration/SQL diff and test against a restored copy. Never run a destructive migration against the primary database.

## Verification status

This is a schema contract and additive support-table script, not a completed application cutover. The existing API still contains GUID route parameters and direct User.Role access. Do not deploy or run the new SQL script until those code paths have been migrated and a .NET build plus SQL Server integration tests pass.