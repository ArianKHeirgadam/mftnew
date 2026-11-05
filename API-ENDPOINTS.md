# Mft API endpoint inventory

Base URL: `https://localhost:7081/api` (ASP.NET launch profile). All routes require a valid bearer token unless explicitly marked anonymous. Authorization is enforced by the API; hiding a frontend control is not considered a security boundary.

## Authentication and profile

| Method | Route | Access / purpose |
|---|---|---|
| POST | `/auth/login` | Anonymous username/password login; returns JWT and role |
| GET | `/auth/me` | Current authenticated user and department |
| POST | `/auth/change-password` | Verify current password, set a new password, clear temporary-password flag |
| GET / PUT | `/users/me/profile` | Read/update current user's profile |

## Users and departments

| Method | Route | Access / purpose |
|---|---|---|
| GET | `/users?role=&search=` | SuperAdmin, Department (own department), Teacher (students in assigned department) |
| GET | `/users/{id}` | Same role scope; response excludes password hashes |
| POST | `/users` | Create Student/Teacher/Department/SuperAdmin according to role permissions; requires an explicit password of at least 8 characters |
| PUT / DELETE | `/users/{id}` | Update or delete an in-scope user; self-deletion is blocked |
| GET | `/departments?search=` | SuperAdmin; Department/Teacher see only their assigned department |
| GET | `/departments/{id}` | SuperAdmin or matching Department/Teacher scope |
| POST / PUT / DELETE | `/departments[/{id}]` | SuperAdmin only |

## Questions and exams

| Method | Route | Access / purpose |
|---|---|---|
| GET | `/questions?search=&type=&category=` | Teacher sees only their own questions; Department/SuperAdmin can view all current question records |
| GET | `/questions/{id}` | Question detail, scoped for teachers |
| POST / PUT / DELETE | `/questions[/{id}]` | Create/edit/delete questions; teacher ownership enforced; MCQ options/correct answer validated |
| POST | `/questions/import-pdf/preview` | TeacherOrAbove; parses a text-based PDF and returns a preview without saving |\n| POST | `/questions/import-pdf` | TeacherOrAbove; imports confirmed MCQ, essay and matching questions from text-based PDF into the question bank (15 MB max; no OCR) |\n| GET | `/exams` | Student sees published exams; Teacher sees owned exams; Department sees department exams; SuperAdmin sees all |
| GET | `/exams/{id}` | Exam detail within role scope |
| POST / PUT / DELETE | `/exams[/{id}]` | Create/update/delete exams; title, duration, pass mark (0–20), navigation setting, teacher ownership and selected question ownership validated; exam question points are normalized to a total of 20 |
| POST | `/exams/{id}/start` | Student starts or resumes a published exam within its schedule; correct answers are not returned |
| PUT | `/exams/attempts/{attemptId}/answers` | Save answers for the authenticated student's active attempt while its server-side duration remains valid |
| POST | `/exams/attempts/{attemptId}/submit` | Finalize an active attempt; objective questions are auto-scored, essay answers remain pending manual grading, and expired attempts cannot submit new answers |

## Attendance\n\n| Method | Route | Access / purpose |\n|---|---|---|\n| GET | `/attendance?date=YYYY-MM-DD&departmentId=` | Teacher/Department see own department; SuperAdmin selects department; returns daily student statuses |\n| PUT | `/attendance` | Saves Present, Absent or Deleted status for selected students and date |\n\nRun `database/create-attendance-records.sql` before using attendance. The page defaults to the browser's local date.\n\n## Results, notifications, dashboard and settings

| Method | Route | Access / purpose |
|---|---|---|
| GET | `/results?examId=&studentId=` | Role-scoped attempt list |
| GET | `/results/{id}` | Attempt detail and answer review within role scope |
| PUT | `/results/{id}/grade` | Teacher/Department/SuperAdmin can grade within scope, bounded by exam max score |
| GET | `/notifications?unread=` | Current user's notifications |
| PUT | `/notifications/{id}/read` | Mark one owned notification read |
| PUT | `/notifications/read-all` | Mark all current user's notifications read |
| DELETE | `/notifications/{id}` | Delete one owned notification |
| GET | `/dashboard/summary` | Role-scoped aggregate counts |
| GET | `/dashboard/activity` | Role-scoped daily attempt counts for the last seven days |
| GET / PUT | `/settings` | SuperAdmin read/write key-value settings |
| GET | `/reports/departments` | SuperAdmin or Department; scoped department-level exam, score, pass-rate, participation, student and teacher aggregates |
| GET | `/health` | API health check (outside `/api`) |

## Schema requirements for exam grading

Run `database/patch-exam-grading-and-department-manager.sql` on a backed-up
development database before using the updated API. It adds persistence for
question category/difficulty/options/correct answers, department manager IDs,
attempt status and answer correctness. Frontend IDs are GUID strings and must
not be converted to JavaScript numbers.

## Frontend wiring in this revision

- Login uses API authentication and role-driven navigation; no role picker.
- User lists, create/edit/delete actions, department management, question CRUD, exam creation, exam start/autosave/submit, results review, notifications, profile/password update, settings key/value save, dashboard metrics, and department reports use the API.
- Report charts are populated only when API data is returned; hardcoded sample chart series were removed.
- Password reset by email/SMS, SMS delivery, actual database backup/restore, PDF/Excel report export, image uploads, and a full audit trail are not implemented because the necessary provider/storage/export workflow is not configured. The UI must not report those operations as successful.

The API's live Swagger document is the source of truth. These routes describe code, not a claim of successful SQL Server integration testing.

## Answer key management
- `GET /api/exams/{examId}/answer-key` — list question order, text, and saved correct answer for an exam.
- `PUT /api/exams/{examId}/answer-key` — direct JSON entry: `{ "entries": [{ "questionNumber": 1, "answer": "A" }], "replaceExisting": true }`. `questionId` may be used instead of `questionNumber`.
- `POST /api/exams/{examId}/answer-key/import` — multipart upload field `file`, optional `replaceExisting` (`true` by default). Supported: `.xlsx`, `.xlsm`, text-based `.pdf`, `.csv`, `.tsv`, `.json`, `.xml`, `.txt`.
- For CSV/TSV/Excel, use two columns: question number in the exam order, then answer. Headers are optional. JSON accepts an array or `{ "entries": [...] }`; XML accepts `<entry questionNumber="1" value="A" />`; TXT and PDF lines use `1,A` or `Question 1: A` style.
- For multiple-choice items, an answer may be the exact option text or a letter A-E (also Persian alef/b/j/d forms) matching the option's order. The endpoint validates the exam scope and caller's role/ownership. PDF files must contain selectable text; scanned PDFs require OCR before upload. Maximum upload size is 15 MB.
- UI: `Frontend/answer-key-upload.html`.\n- Question PDF import: `Frontend/question-import.html`; format uses `Q1:`, `A)`, `ANSWER:`, and `PAIR: left => right` markers. Scanned PDFs need OCR outside the app before upload. Matching questions are automatically graded proportionally per correct pair.


## PDF exam, manual grading and result publication workflow

- `POST /api/exam-files/exams/{examId}` — teacher/department/super-admin uploads the exam PDF as multipart field `file` (20 MB maximum).
- `GET /api/exam-files/exams/{examId}` — authenticated download. Students can download only a published exam in their department; its owner and authorized department/super admins can download it.
- `POST /api/exam-files/attempts/{attemptId}` — student uploads/replaces their PDF answer while the attempt is in progress and before the server-side deadline.
- `GET /api/exam-files/attempts/{attemptId}/info` — returns metadata for the authorized grader (or the student after result publication).
- `GET /api/exam-files/attempts/{attemptId}` — authenticated download of the student's PDF answer; students can access their own file only after the attempt is graded and results are published.
- `PUT /api/results/{attemptId}/answers/{questionId}/grade` — per-question manual grade. JSON body: `{ "score": 2.5, "comment": "..." }`. The score is entered in the question's configured raw points; it is normalized to the exam's 100-point scale. Teacher ownership and department scope are checked.
- `POST /api/results/exams/{examId}/publish` — publish results after all persisted answers requiring manual grading are graded.
- `POST /api/results/exams/{examId}/unpublish` — withdraw publication.
- Student result list/detail endpoints intentionally hide unpublished results and answer keys. The teacher's result detail page exposes manual-grading inputs and the protected PDF download control.

The SQL Server patch is `database/patch-exam-pdf-workflow.sql`. Back up the database and test the additive patch on a development copy before applying it. PDFs are stored under the API's private `App_Data/exam-files` directory, outside `wwwroot`, using generated filenames. The application must have write access to this directory, and deployment backups must include it.
