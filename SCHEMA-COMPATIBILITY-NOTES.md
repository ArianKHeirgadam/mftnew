# Mft schema compatibility notes

## Question creator identity fix

The API consistently identifies the current actor with `User.UserId()`, which is the primary key of `dbo.Users`. The previous SQL schema mapped `Questions.CreatedByTeacherId` to `Teachers.Id`, creating a mismatch for SuperAdmin and Department accounts and for teacher accounts whose `Users.Id` differs from `Teachers.Id`.

The code now maps `Question.CreatedByUserId` to `Questions.CreatedByUserId` and configures a restrictive foreign key to `Users.Id`. Question list, get, update, and delete authorization already use the current user ID and therefore now align with the database identity model.

### Required database migration

Before running the updated API, back up the database and execute:

```text
database/patch-question-owner-user-fk.sql
```

The script:
- validates that every legacy question owner maps through `Teachers.UserId` to an existing `Users.Id`;
- refuses to proceed if teacher profiles are ambiguous or orphaned;
- translates existing question owners from `Teachers.Id` to `Users.Id`;
- renames `CreatedByTeacherId` to `CreatedByUserId`;
- replaces the old FK with `Questions.CreatedByUserId -> Users.Id`.

It does not create fake teacher records, delete questions, or drop the Teachers table.

## Verification status

The repository files were updated, but this environment has not run SQL Server against the user's database and has not run `dotnet build` or integration tests. Run the migration on a backup/development database first, then build and test question creation/list/update/delete for SuperAdmin, Department, and Teacher roles before deploying.

Other schema compatibility items documented by earlier work may still require separate verification.
