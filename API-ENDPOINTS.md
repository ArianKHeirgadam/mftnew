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
| GET | `/exams` | Student sees published exams; Teacher sees owned exams; Department sees department exams; SuperAdmin sees all |
| GET | `/exams/{id}` | Exam detail within role scope |
| POST / PUT / DELETE | `/exams[/{id}]` | SuperAdmin, Department and Teacher can create/update/delete within role scope; creator is a Users identity, department scope is enforced, and selected question ownership is validated; exam question points are normalized to a total of 100 |
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

## Canonical single-attempt exam workflow

These routes use bigint IDs and the supplied canonical tables; they are separate from the legacy GUID-based `/exams` routes above.

| Method | Route | Access / purpose |
|---|---|---|
| GET | `/canonical/exams` | Student list includes whether a single attempt already exists and its active attempt ID/status; other roles see their scoped canonical exams |
| GET | `/canonical/exams/{examId}` | Canonical exam structure and question references |
| POST / PUT | `/canonical/exams` and `/canonical/exams/{id}` | Create/update canonical exam sections and questions; structure becomes immutable once any student attempt exists |
| POST | `/canonical/exams/{examId}/attempt/start` | Create the student's only attempt; unique (ExamId, StudentUserId) constraint rejects any second attempt |
| GET | `/canonical/exams/{examId}/attempt/{attemptId}` | Load the active attempt, deadline, questions, and previously saved answers; only the owner can access it |
| PUT | `/canonical/exams/{examId}/attempt/{attemptId}/answers` | Autosave the active attempt before its server-side deadline |
| POST | `/canonical/exams/{examId}/attempt/{attemptId}/submit` | Finalize once; unanswered MCQ/matching items receive zero, objective answers are auto-graded, essays await manual grading |

The student dashboard and `online-exam.html` now use this canonical attempt workflow. An in-progress attempt can be resumed by opening the same attempt ID; that is a continuation, not a second attempt. A submitted attempt is shown as completed and cannot be restarted.

**Remaining integration:** the teacher question-bank and exam-creation pages still call legacy GUID-based endpoints. They must be migrated to canonical bigint question/skill/category/exam APIs before the full teacher-to-student workflow is end-to-end. Do not mix legacy GUID IDs with canonical bigint IDs.

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
- Password reset by email/SMS, SMS delivery, actual database backup/restore, PDF/Excel report export, generic profile image uploads, and a full audit trail are not implemented because the necessary provider/storage/export workflow is not configured. The UI must not report those operations as successful.

The API's live Swagger document is the source of truth. These routes describe code, not a claim of successful SQL Server integration testing.

## Answer key management
- `GET /api/exams/{examId}/answer-key` — list question order, text, and saved correct answer for an exam.
- `PUT /api/exams/{examId}/answer-key` — direct JSON entry: `{ "entries": [{ "questionNumber": 1, "answer": "A" }], "replaceExisting": true }`. `questionId` may be used instead of `questionNumber`.
- `POST /api/exams/{examId}/answer-key/import` — multipart upload field `file`, optional `replaceExisting` (`true` by default). Supported: `.xlsx`, `.xlsm`, text-based `.pdf`, `.csv`, `.tsv`, `.json`, `.xml`, `.txt`.
- For CSV/TSV/Excel, use two columns: question number in the exam order, then answer. Headers are optional. JSON accepts an array or `{ "entries": [...] }`; XML accepts `<entry questionNumber="1" value="A" />`; TXT and PDF lines use `1,A` or `Question 1: A` style.
- For multiple-choice items, an answer may be the exact option text or a letter A-E (also Persian alef/b/j/d forms) matching the option's order. The endpoint validates the exam scope and caller's role/ownership. PDF files must contain selectable text; scanned PDFs require OCR before upload. Maximum upload size is 15 MB.
- UI: `Frontend/answer-key-upload.html`.\n- PDF question import and OCR are not part of the supported workflow. The uploaded exam PDF is stored and served as-is; the application does not scan, parse, or extract questions from it.


## PDF exam, manual grading and result publication workflow

- PDF-only exams can be created with zero question-bank entries; the teacher must upload the exam paper PDF before a student can start the exam.
- The exam PDF is delivered as the original file. The exam workflow does not run OCR, parse pages, scan for questions, or import its contents.
- `POST /api/exam-files/exams/{examId}` — teacher/department/super-admin uploads the exam PDF as multipart field `file` (20 MB maximum).
- `GET /api/exam-files/exams/{examId}` — authenticated download. Students can download only a published exam in their department; its owner and authorized department/super admins can download it.
- `POST /api/exam-files/attempts/{attemptId}` — student uploads/replaces a PDF, JPG, JPEG, or PNG answer sheet while the attempt is in progress and before the server-side deadline (20 MB maximum).
- `POST /api/exams/attempts/{attemptId}/submit` — in PDF-only mode, finalizes the attempt only after an answer-sheet file has been uploaded; the attempt remains pending until a teacher enters a score.
- `PUT /api/results/{id}/grade` — teacher enters the PDF-only attempt's final score from 0 to 100. Publication is blocked until each PDF-only submission is graded.
- `GET /api/exam-files/attempts/{attemptId}/info` — returns metadata to the authorized grader or the student who owns the attempt.
- `GET /api/exam-files/attempts/{attemptId}` — authenticated preview/download of the student's PDF or image answer sheet; students can access only their own file, while authorized graders can access the attempt's file.
- `PUT /api/results/{attemptId}/answers/{questionId}/grade` — per-question manual grade. JSON body: `{ "score": 2.5, "comment": "..." }`. The score is entered in the question's configured raw points; it is normalized to the exam's 100-point scale. Teacher ownership and department scope are checked.
- `POST /api/results/exams/{examId}/publish` — publish results after all persisted answers requiring manual grading are graded.
- `POST /api/results/exams/{examId}/unpublish` — withdraw publication.
- Student result list/detail endpoints intentionally hide unpublished results and answer keys. The teacher's result detail page exposes manual-grading inputs and the protected PDF download control.

The SQL Server patch is `database/patch-exam-pdf-workflow.sql`. Back up the database and test the additive patch on a development copy before applying it. PDFs are stored under the API's private `App_Data/exam-files` directory, outside `wwwroot`, using generated filenames. The application must have write access to this directory, and deployment backups must include it.
