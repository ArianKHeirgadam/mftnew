# MFT exam grading verification

## Required database preparation

Before starting the API, back up the development database and run
`database/patch-exam-grading-and-department-manager.sql` against the same
database configured by `DefaultConnection`. The patch adds persistence for
question options/correct answers, attempt status, answer correctness and the
department manager reference. It does not delete existing records.

The previous compatibility mapping intentionally ignored several of these CLR
properties. Without the SQL patch, the exam API cannot reliably persist the
answer key or the attempt lifecycle.

## Functional test

1. Sign in as a Teacher whose account has a valid `DepartmentId`.
2. Open Question Bank and create 20 multiple-choice questions. Give each four
   distinct options and choose exactly one correct answer. Save and reopen a
   question to verify that its options and correct answer remain selected.
3. Open the exam wizard. Select the teacher's department and teacher, choose
   all 20 questions, set the passing score, and publish the exam.
4. Sign in as a Student in the same department. Start the published exam,
   answer 15 questions correctly and 5 incorrectly, then submit.
5. Expected automatic score: `75.00 / 100`. Check that the result page and
   teacher grading view show the same score.
6. Repeat with 20 correct answers (expected `100.00`) and 20 incorrect or
   unanswered answers (expected `0.00`).
7. Add an Essay question to a separate exam. Confirm it is not automatically
   marked correct and remains available for manual grading.
8. Confirm the student start payload contains question text/options but never
   includes `CorrectAnswer`.

## SQL inspection

Replace the username/exam title filters with the test values:

```sql
SELECT q.Id, q.Text, q.Type, q.OptionsJson, q.CorrectAnswer
FROM dbo.Questions AS q
ORDER BY q.Text;

SELECT a.Id, a.Status, a.Score, a.StartedAt, a.SubmittedAt,
       e.Title AS ExamTitle
FROM dbo.ExamAttempts AS a
JOIN dbo.Exams AS e ON e.Id = a.ExamId
ORDER BY a.StartedAt DESC;

SELECT ea.ExamAttemptId, ea.QuestionId, ea.EssayText,
       ea.IsCorrect, ea.Score AS AwardedScore
FROM dbo.ExamAnswers AS ea
ORDER BY ea.ExamAttemptId, ea.QuestionId;
```

## Verification boundary

The source changes and SQL patch are committed, but this repository editing
environment has no access to the user's local SQL Server or .NET runtime.
Therefore the live workflow above is a required local integration test; do
not interpret the expected score as a test result until it has been run.
