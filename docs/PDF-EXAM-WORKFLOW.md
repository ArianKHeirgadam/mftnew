# PDF exam workflow — deployment and verification

## Deployment order

1. Back up the target SQL Server database and test the patch against a restored copy.
2. Execute `database/patch-exam-pdf-workflow.sql`. The script is additive and re-runnable; it does not drop existing data.
3. Deploy the API and frontend together.
4. Ensure the API process identity can create, read, and delete files under `src/Mft.API/App_Data/exam-files` (or the equivalent API content-root path in the deployment).
5. Include that private directory in the deployment's protected backup strategy. Do not expose it through static-file middleware or move it under `wwwroot`.
6. Verify the API build, SQL Server connectivity, and role-based tests before production use.

## End-to-end acceptance test

Use a development database and test accounts for one teacher, one student in the same department, and one student in a different department.

1. Teacher creates an exam with two multiple-choice questions and one essay question, and optionally uploads the exam PDF.
2. Verify the exam PDF can be downloaded by the owning teacher and the in-department student after the exam is published. Verify another department's student receives no access.
3. Student starts the exam, answers the questions, uploads a PDF response, and submits before the deadline.
4. Verify the student cannot read the result or download their submission PDF before result publication.
5. Teacher opens the attempt result detail, downloads the student's PDF, and grades the essay answer question-by-question.
6. Verify the teacher cannot enter a score greater than the question's configured raw points.
7. Verify publication is rejected while any saved answer still needs manual grading.
8. Teacher publishes results. Verify the student can now read the result, correct answers, awarded scores, and download their own submitted PDF. Verify the exam is closed to new attempts after publication.
9. Verify the student cannot read another student's attempt or download another student's PDF.
10. Verify a teacher cannot grade an exam owned by another teacher, and a department manager cannot grade outside their department.
11. Unpublish results before making any correction; grading endpoints reject changes while results remain published.

## Important limits

- PDF question extraction uses selectable text. Scanned PDFs need OCR before import; OCR is not implemented here.
- The upload endpoint checks file extension, size, and the PDF signature. It is not an antivirus scanner or a complete PDF sanitization service.
- Files are stored on the API host's local filesystem. Multi-instance deployments need shared private storage or an equivalent storage service before scaling out.
- A failed database write after file creation may leave an orphaned file; periodic private-storage cleanup should be part of operational maintenance.
- This implementation has not been compiled or integration-tested in this environment because the .NET SDK and a SQL Server instance are unavailable. Do not consider the workflow production-ready until the acceptance test above passes on the target environment.
