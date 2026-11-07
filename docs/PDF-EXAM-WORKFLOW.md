# PDF-based exam workflow

## Intended flow

1. A teacher creates an exam and chooses either:
   - a structured exam using questions from the question bank; or
   - PDF-only mode, with no structured questions.
2. In PDF-only mode, the teacher must upload the original exam paper as a PDF (maximum 20 MB).
3. The student starts the published exam and downloads the same original PDF. The application does not OCR, scan, parse, or import questions from that file.
4. The student writes answers on paper, photographs/scans the answer sheet, and uploads one PDF, JPG, JPEG, or PNG file (maximum 20 MB).
5. The student previews the selected local file and confirms final submission.
6. The teacher opens the submitted attempt, previews or downloads the student's answer sheet, and enters a score from 0 to 100.
7. Results remain hidden from students until the teacher publishes the exam results.

## Deployment order

1. Back up the target SQL Server database and test the additive patch against a restored copy.
2. Execute `database/patch-exam-pdf-workflow.sql`.
3. Deploy the API and frontend together.
4. Ensure the API process identity can create/read/delete files under `App_Data/exam-files` under the API content root.
5. Keep that directory private and include it in protected backups. Do not expose it through static-file middleware or place it under `wwwroot`.
6. For multiple API instances, configure shared private storage before scaling out.

## Acceptance checks

- Create a PDF-only exam with no question-bank entries and upload its PDF.
- Verify a student in the exam's department can start it and download the exact uploaded PDF.
- Verify students outside the department cannot access the exam PDF or attempt.
- Verify the student can upload a valid PDF, JPG, JPEG, or PNG answer sheet, preview it, and submit.
- Verify an unsupported extension, invalid file signature, empty file, or file over 20 MB is rejected.
- Verify a student cannot submit a PDF-only attempt without an uploaded answer sheet.
- Verify the teacher sees the attempt in the pending-grading queue and can preview/download the student's answer sheet.
- Verify the teacher can assign a score from 0 to 100 and cannot edit a result after publication.
- Verify publication is blocked while any submitted PDF-only attempt remains ungraded.
- Verify students cannot see their score or another student's answer sheet before publication, and cannot access another student's attempt at any time.
- Verify structured-question exams continue to start, save answers, submit, and use their existing grading workflow.
- Verify the old PDF question-import/OCR route is no longer present.

## Security and operational notes

- Upload validation checks extension, size, and a basic file signature; it is not antivirus scanning or complete document sanitization.
- Files use server-generated names and are stored outside `wwwroot`.
- Local filesystem storage is suitable only when the API host's storage is persistent and backed up.
- A database failure after a file is written can leave an orphaned file; schedule private-storage cleanup.
- This change has not been compiled or browser-tested in the target environment. Run `dotnet restore`, `dotnet build Mft.sln`, and the acceptance checks above before production use.
