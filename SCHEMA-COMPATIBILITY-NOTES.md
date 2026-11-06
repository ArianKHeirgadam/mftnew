# Mft schema compatibility notes

## Question creator identity fix

The API identifies the current actor with `User.UserId()`, which is the primary key of `dbo.Users`. The previous SQL schema mapped `Questions.CreatedByTeacherId` to `Teachers.Id`, creating a mismatch for SuperAdmin and Department accounts and for teacher accounts whose `Users.Id` differs from `Teachers.Id`.

The code maps `Question.CreatedByUserId` to `Questions.CreatedByUserId` and configures a restrictive foreign key to `Users.Id`. Question list, get, update, and delete authorization use the authenticated user identity and role scope.

### Required question migration

Back up the database and execute:

```text
database/patch-question-owner-user-fk.sql
```

The script validates legacy ownership, translates existing question owners through `Teachers.UserId`, renames the column, and replaces the FK with `Questions.CreatedByUserId -> Users.Id`. It does not create fake teacher records, delete questions, or drop the Teachers table.

## Exam creator identity and role support

Exam ownership follows the same identity model. `Exam.CreatedByUserId` maps to `Exams.CreatedByUserId` and references `Users.Id`, not `Teachers.Id`. This allows all roles covered by the `TeacherOrAbove` policy to create exams:

- `SuperAdmin`: may create exams for a selected valid department.
- `Department`: may create exams only in the department from the authenticated account.
- `Teacher`: may create exams only in the department from the authenticated account.

The API derives the default creator from the authenticated user. If an authorized SuperAdmin or Department manager explicitly assigns a teacher as the exam owner through `CreatedByUserId`, the API validates that the selected account is a Teacher and, for Department managers, belongs to their department. Student accounts cannot create exams.

### Required exam migration

Back up the database and execute:

```text
database/patch-exam-owner-user-fk.sql
```

The script validates that every existing `Exams.TeacherId` maps through `Teachers.Id -> Teachers.UserId -> Users.Id`; stops without changing ownership if any legacy reference is orphaned; drops the legacy FK; translates existing exam owners to `Users.Id`; renames `TeacherId` to `CreatedByUserId`; and creates a restrictive FK to `Users.Id` plus an index.

It preserves existing exams and does not delete or remove the `Teachers` table.

## Verification status

The repository changes are prepared on `feature/pdf-exam-workflow`. SQL Server execution against the user's database, `dotnet build`, and role-based integration tests have not been run here. Test on a backed-up development database first, then verify exam creation for SuperAdmin, Department, and Teacher; verify that Student is denied; and verify existing exams remain accessible.