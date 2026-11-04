# Mft project status — API integration revision

## Implemented in this revision

- Added inline create actions for departments in student/teacher forms and for teachers in the exam wizard; added inline Department-manager account creation in the department editor. Role checks remain enforced by the API.
- Corrected frontend GUID handling so department, user, question, exam and attempt IDs remain strings instead of being coerced to JavaScript numbers.
- Corrected the exam wizard's passing-score input to use the API's 0–100 scale.
- Added `database/patch-exam-grading-and-department-manager.sql` and EF mappings for MCQ options/correct answers, department manager IDs, attempt status and answer correctness. See `docs/EXAM-SCORING-VERIFICATION.md`.

- Preserved the supplied frontend pages, styles, and image assets.
- Reworked the API adapter into readable modules for authentication, user CRUD, departments, question CRUD, exam creation/start, result review, notifications, profile/password changes, settings and reports.
- Connected the student/teacher user forms and question/department modals to the backend instead of displaying fake success messages.
- Connected the exam wizard to persisted questions, departments and teachers; it submits the selected schedule, owner, duration, passing score, navigation preference and question IDs to the API. Random question selection chooses from the API-loaded pool. Question scores are normalized to a total of 20 points for new/updated exams.
- Connected online exam autosave and final submission to the attempt endpoints; the server enforces attempt duration, objective questions are auto-scored, essay answers remain pending manual grading, and result review reads the selected attempt from `results.html?attemptId=...`.
- Connected notification read/delete/read-all actions, profile save and password change, key/value settings, dashboard counts and department report data.
- Removed hardcoded sample chart series from `dashboard.js`; charts are rendered only from API-provided activity/department/result data.
- Added validation for MCQ correct answers, exam title/duration/schedule, selected question ownership, department managers and duplicate department names.
- Expanded the report endpoint with student/teacher totals to support frontend reporting.
- Added and refreshed `API-ENDPOINTS.md`.

## Not implemented / requires provider or further work

- SMS sending is not connected to a provider. Temporary passwords are marked for change at first login, but are not delivered by SMS.
- Password-reset email/SMS, image uploads, actual SQL Server backup/restore, PDF/Excel export, and comprehensive audit logging are not implemented.
- The frontend still has a few informational/demo layout sections (for example, historical activity copy and some decorative summary labels) that are not backed by dedicated API data. These must not be interpreted as authoritative records.
- A full automated authorization test suite and race-condition testing are still needed. Manual adversarial tests should cover cross-department access, teacher ownership, concurrent exam submission and grading.

## Verification

- `node --check` is used for frontend JavaScript syntax validation.
- JSON config parsing and HTML structural checks are run before packaging.
- The current execution environment does not have the .NET SDK or a SQL Server instance, so `dotnet restore`, `dotnet build`, EF migrations, Swagger smoke tests and live database integration tests cannot be claimed as passed.
