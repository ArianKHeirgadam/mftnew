/*
  READ-ONLY audit for canonical exams that have not been assigned to a class.
  Run after patch-canonical-exam-class-targeting.sql.
  No data is changed by this script.

  For exams with attempts, review the CandidateClassCoverage result:
  only consider a controlled assignment when MissingAttemptedStudents = 0,
  and after confirming the intended class with the academic administrator.
*/
IF OBJECT_ID(N'dbo.Exams', N'U') IS NULL
    THROW 52040, 'dbo.Exams was not found.', 1;
IF COL_LENGTH(N'dbo.Exams', N'ClassId') IS NULL
    THROW 52041, 'Run patch-canonical-exam-class-targeting.sql first.', 1;
IF OBJECT_ID(N'dbo.CanonicalClasses', N'U') IS NULL
   OR OBJECT_ID(N'dbo.CanonicalClassMembers', N'U') IS NULL
    THROW 52042, 'Canonical class tables were not found.', 1;

-- Result 1: inventory of unassigned exams and whether attempts exist.
SELECT
    e.Id AS ExamId,
    e.Title,
    e.DepartmentId,
    e.IsPublished,
    COUNT(a.Id) AS AttemptCount,
    COUNT(DISTINCT a.StudentUserId) AS AttemptedStudentCount,
    CASE WHEN COUNT(a.Id) = 0 THEN N'Eligible for guarded API assignment'
         ELSE N'Requires membership audit and controlled decision' END AS AssignmentStatus
FROM dbo.Exams AS e
LEFT JOIN dbo.ExamAttempts AS a ON a.ExamId = e.Id
WHERE e.ClassId IS NULL
GROUP BY e.Id, e.Title, e.DepartmentId, e.IsPublished
ORDER BY e.Id;

-- Result 2: for exams with attempts, show each same-department candidate class
-- and how many distinct students with historical attempts are not members.
SELECT
    e.Id AS ExamId,
    e.Title,
    c.Id AS CandidateClassId,
    c.Name AS CandidateClassName,
    COUNT(DISTINCT a.StudentUserId) AS AttemptedStudents,
    COUNT(DISTINCT CASE WHEN m.StudentUserId IS NOT NULL THEN a.StudentUserId END) AS AttemptedStudentsInClass,
    COUNT(DISTINCT a.StudentUserId)
      - COUNT(DISTINCT CASE WHEN m.StudentUserId IS NOT NULL THEN a.StudentUserId END) AS MissingAttemptedStudents
FROM dbo.Exams AS e
INNER JOIN dbo.ExamAttempts AS a ON a.ExamId = e.Id
INNER JOIN dbo.CanonicalClasses AS c ON c.DepartmentId = e.DepartmentId
LEFT JOIN dbo.CanonicalClassMembers AS m
    ON m.ClassId = c.Id AND m.StudentUserId = a.StudentUserId
WHERE e.ClassId IS NULL
GROUP BY e.Id, e.Title, c.Id, c.Name
ORDER BY e.Id, MissingAttemptedStudents, c.Name;
