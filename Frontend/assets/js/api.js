/* Mft frontend/API adapter. Keeps the supplied page layout and uses the API for persisted data. */
(() => {
  'use strict';

  const API_BASE = window.MFT_API_BASE || 'https://localhost:7081/api';
  const TOKEN_KEY = 'mft.accessToken';
  const USER_KEY = 'mft.currentUser';
  const token = () => sessionStorage.getItem(TOKEN_KEY) || localStorage.getItem(TOKEN_KEY);
  const userStore = () => sessionStorage.getItem(USER_KEY) || localStorage.getItem(USER_KEY);
  const currentUser = () => { try { return JSON.parse(userStore() || 'null'); } catch { return null; } };
  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);
  const faNumber = value => Number(value || 0).toLocaleString('fa-IR');
  const notify = (message, type = 'ok') => window.showToast?.(message, type);

  async function request(path, options = {}) {
    const headers = new Headers(options.headers || {});
    headers.set('Accept', 'application/json');
    if (options.body != null && !(options.body instanceof FormData)) headers.set('Content-Type', 'application/json');
    if (token()) headers.set('Authorization', `Bearer ${token()}`);

    const response = await fetch(`${API_BASE}${path}`, { ...options, headers });
    if (response.status === 401 && !location.pathname.endsWith('login.html')) {
      clearSession();
      location.href = 'login.html';
      throw new Error('نشست شما منقضی شده است. دوباره وارد شوید.');
    }
    if (response.status === 204) return null;
    const payload = await response.json().catch(() => null);
    if (!response.ok) throw new Error(payload?.message || payload?.title || `خطای سرویس (${response.status})`);
    return payload;
  }

  function clearSession() {
    [sessionStorage, localStorage].forEach(store => { store.removeItem(TOKEN_KEY); store.removeItem(USER_KEY); });
  }

  async function safe(action, successMessage) {
    try {
      const result = await action();
      if (successMessage) notify(successMessage, 'ok');
      return result;
    } catch (error) {
      console.error(error);
      notify(error.message || 'ارتباط با سرور ناموفق بود.', 'danger');
      return undefined;
    }
  }

  function renderRows(selector, rows, render, columns = 5) {
    const body = $(selector);
    if (!body) return;
    body.innerHTML = rows.length
      ? rows.map(render).join('')
      : `<tr><td colspan="${columns}" style="text-align:center;padding:28px;color:var(--text-muted,#7b8794)">داده‌ای برای نمایش وجود ندارد.</td></tr>`;
  }

  async function login() {
    const form = $('#loginForm');
    if (!form) return;
    form.addEventListener('submit', async event => {
      event.preventDefault();
      const username = $('#username', form)?.value.trim();
      const password = $('#password', form)?.value;
      if (!username || !password) return notify('نام کاربری و رمز عبور را وارد کنید.', 'warn');

      await safe(async () => {
        const result = await request('/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) });
        const store = $('input[type="checkbox"]', form)?.checked ? localStorage : sessionStorage;
        store.setItem(TOKEN_KEY, result.accessToken);
        store.setItem(USER_KEY, JSON.stringify(result));

        const destination = {
          SuperAdmin: 'dashboard-superadmin.html',
          Department: 'dashboard-department.html',
          Teacher: 'dashboard-teacher.html',
          Student: 'dashboard-student.html'
        }[result.role] || 'dashboard-student.html';
        location.href = destination;
      });
    });
  }

  async function loadCurrentUser() {
    if (!token()) return null;
    const user = await request('/auth/me');
    const serialized = JSON.stringify(user);
    (sessionStorage.getItem(TOKEN_KEY) ? sessionStorage : localStorage).setItem(USER_KEY, serialized);
    document.body.dataset.role = (user.role || '').toLowerCase();
    $$('[data-current-user-name]').forEach(element => element.textContent = user.fullName || user.username);
    return user;
  }

  function attachSearch(selector, reload) {
    const field = $(selector);
    if (!field) return;
    let timer;
    field.addEventListener('input', () => {
      clearTimeout(timer);
      timer = setTimeout(() => safe(reload), 250);
    });
  }

  async function loadUsers() {
    const studentBody = $('#studentsBody');
    const teacherBody = $('#teachersBody') || $('body[data-active="teachers"] table tbody');
    const searchField = $('body[data-active="students"] .search-box input, body[data-active="teachers"] .search-box input');
    const search = searchField?.value.trim();

    if (studentBody) {
      const rows = await request(`/users?role=Student${search ? `&search=${encodeURIComponent(search)}` : ''}`);
      renderRows('#studentsBody', rows, row => `<tr>
        <td>${escapeHtml(row.fullName)}</td><td>${escapeHtml(row.username)}</td><td>${escapeHtml(row.department || '—')}</td>
        <td>—</td><td>—</td><td>${row.isActive ? 'فعال' : 'غیرفعال'}</td>
        <td><button class="btn btn-sm" data-user-edit="${row.id}" data-user-role="Student">ویرایش</button>
        <button class="btn btn-sm btn-danger" data-user-delete="${row.id}">حذف</button></td></tr>`, 7);
    }

    if (teacherBody && document.body.dataset.active === 'teachers') {
      const rows = await request(`/users?role=Teacher${search ? `&search=${encodeURIComponent(search)}` : ''}`);
      const cards = $('#cardView');
      if (cards) cards.innerHTML = rows.length ? rows.map(row => `<div class="card teacher-card"><div class="mohr"><div class="mohr-inner"><b>${escapeHtml(row.fullName.slice(0, 2))}</b></div></div><h4>${escapeHtml(row.fullName)}</h4><div class="spec">${escapeHtml(row.department || 'بدون دپارتمان')}</div><span class="badge ${row.isActive ? 'badge-ok' : 'badge-muted'}">${row.isActive ? 'فعال' : 'غیرفعال'}</span><div class="flex gap-8 mt-16" style="justify-content:center"><button class="btn btn-outline btn-sm" data-user-edit="${row.id}" data-user-role="Teacher">ویرایش</button><button class="btn btn-danger btn-sm" data-user-delete="${row.id}">حذف</button></div></div>`).join('') : '<p class="text-soft">استادی ثبت نشده است.</p>';
      renderRows('body[data-active="teachers"] table tbody', rows, row => `<tr>
        <td>${escapeHtml(row.fullName)}</td><td>${escapeHtml(row.department || '—')}</td><td>—</td><td>—</td>
        <td>${row.isActive ? 'فعال' : 'غیرفعال'}</td><td><button class="btn btn-sm" data-user-edit="${row.id}" data-user-role="Teacher">ویرایش</button>
        <button class="btn btn-sm btn-danger" data-user-delete="${row.id}">حذف</button></td></tr>`, 6);
    }
  }

  async function loadDepartments() {
    if (!$('body[data-active="departments"]')) return;
    const search = $('body[data-active="departments"] .search-box input')?.value.trim();
    const rows = await request(`/departments${search ? `?search=${encodeURIComponent(search)}` : ''}`);
    $$('[data-department-field]').forEach(select => {
      const selected = select.value;
      select.innerHTML = '<option value="">انتخاب دپارتمان</option>' + rows.map(dept => `<option value="${dept.id}">${escapeHtml(dept.name)}</option>`).join('');
      if (selected) select.value = selected;
    });
    const managerSelect = $('#departmentManagerId');
    if (managerSelect) {
      const managers = await request('/users?role=Department').catch(() => []);
      managerSelect.innerHTML = '<option value="">بدون مدیر</option>' + managers.map(manager => `<option value="${manager.id}">${escapeHtml(manager.fullName)}</option>`).join('');
    }
    renderRows('body[data-active="departments"] table tbody', rows, row => `<tr>
      <td>${escapeHtml(row.name)}</td><td>${escapeHtml(row.manager || '—')}</td><td>${faNumber(row.students)}</td><td>${faNumber(row.teachers)}</td>
      <td>${row.isActive ? 'فعال' : 'غیرفعال'}</td><td><button class="btn btn-outline btn-sm" data-department-edit="${row.id}">ویرایش</button>
      <button class="btn btn-danger btn-sm" data-department-delete="${row.id}">حذف</button></td></tr>`, 6);
  }

  async function loadQuestions() {
    if (!$('body[data-active="bank"]')) return;
    const search = $('body[data-active="bank"] .search-box input')?.value.trim();
    const rows = await request(`/questions${search ? `?search=${encodeURIComponent(search)}` : ''}`);
    renderRows('body[data-active="bank"] table tbody', rows, row => `<tr>
      <td>${escapeHtml(row.text)}</td><td>${escapeHtml(row.category)}</td><td>${escapeHtml(row.type)}</td><td>${escapeHtml(row.difficulty)}</td>
      <td><button class="btn btn-outline btn-sm" data-question-edit="${row.id}">ویرایش</button>
      <button class="btn btn-danger btn-sm" data-question-delete="${row.id}">حذف</button></td></tr>`, 5);
  }

  async function loadExams() {
    const role = (document.body.dataset.role || currentUser()?.role || '').toLowerCase();
    if (role !== 'student' || document.body.dataset.active !== 'dashboard') return;
    const rows = await request('/exams');
    renderRows('body[data-active="dashboard"] tbody', rows, row => `<tr>
      <td>${escapeHtml(row.title)}</td><td>${escapeHtml(row.subject || '—')}</td>
      <td>${row.startsAt ? new Date(row.startsAt).toLocaleDateString('fa-IR') : '—'}</td>
      <td>${faNumber(row.durationMinutes)} دقیقه</td><td>${row.isPublished ? 'آماده برگزاری' : 'منتشر نشده'}</td>
      <td>${row.isPublished ? `<button class="btn btn-primary" data-exam-start="${row.id}">شروع آزمون</button>` : '—'}</td></tr>`, 6);
    const nextExam = rows.filter(row => row.isPublished && (!row.startsAt || new Date(row.startsAt).getTime() >= Date.now())).sort((a, b) => (a.startsAt ? new Date(a.startsAt).getTime() : Infinity) - (b.startsAt ? new Date(b.startsAt).getTime() : Infinity))[0];
    const nextCard = $('body[data-active="dashboard"] .eyebrow');
    if (nextCard?.textContent.includes('آزمون پیش رو')) {
      const card = nextCard.closest('.card');
      const title = $('h2', card); const details = $('p', card); const link = $('a', card);
      if (nextExam) {
        if (title) title.textContent = nextExam.title;
        if (details) details.textContent = `${nextExam.startsAt ? new Date(nextExam.startsAt).toLocaleString('fa-IR') : 'زمان شروع اعلام نشده'} — مدت زمان: ${faNumber(nextExam.durationMinutes)} دقیقه`;
        if (link) { link.dataset.examStart = String(nextExam.id); link.href = '#'; link.textContent = 'شروع آزمون'; }
      } else {
        if (title) title.textContent = 'آزمون زمان‌بندی‌شده‌ای وجود ندارد';
        if (details) details.textContent = 'پس از انتشار آزمون توسط استاد، این بخش به‌روزرسانی می‌شود.';
        if (link) link.remove();
      }
      const hours = $('.mohr.lg.gold .mohr-inner', card);
      if (hours) hours.innerHTML = '<b>—</b><span>ساعت مانده</span>';
    }
  }

  async function loadResults() {
    if (!$('body[data-active="results"]')) return;
    const params = new URLSearchParams(location.search);
    const attemptId = params.get('attemptId');
    const examId = params.get('examId');
    const role = (currentUser()?.role || '').toLowerCase();
    if (attemptId) {
      let result;
      try { result = await request(`/results/${encodeURIComponent(attemptId)}`); }
      catch (error) {
        const heading = $('body[data-active="results"] h1');
        if (heading) heading.textContent = 'نتیجه هنوز منتشر نشده یا در دسترس نیست';
        const body = $('body[data-active="results"] .card table tbody');
        if (body) body.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:28px">پس از تصحیح و انتشار رسمی نتیجه، جزئیات این آزمون نمایش داده می‌شود.</td></tr>';
        return;
      }
      const heading = $('body[data-active="results"] h1');
      if (heading) {
        const scoreLabel = result.score != null
          ? `${faNumber(result.score)} از ${faNumber(result.maxScore)}`
          : result.autoScore != null
            ? `نمره موقت ${faNumber(result.autoScore)} از ${faNumber(result.maxScore)} — در انتظار تصحیح ${faNumber(result.pendingManualCount)} سؤال تشریحی`
            : (result.resultsPublished ? 'در انتظار تکمیل تصحیح' : 'منتشر نشده');
        const outcome = result.score == null ? '' : (Number(result.score) >= Number(result.passingScore) ? ' — قبول' : ' — نیازمند تلاش بیشتر');
        heading.textContent = `نتیجه آزمون: ${result.exam} — ${scoreLabel}${outcome}`;
      }
      const body = $('body[data-active="results"] .card table tbody');
      const canGrade = ['teacher', 'department', 'superadmin'].includes(role);
      if (body) {
        body.innerHTML = result.answers.length ? result.answers.map((answer, index) => {
          const isEssay = ['Essay', 'Text'].includes(String(answer.questionType || '').trim());
          const canEditGrade = canGrade && isEssay && !result.resultsPublished;
          const gradeControl = canEditGrade
            ? `<div class="flex gap-8 mt-8"><input class="input" type="number" min="0" max="${Number(answer.maxRawScore || 0)}" step="0.01" value="${Number(answer.rawAwardedScore || 0)}" data-grade-score style="max-width:100px" aria-label="نمره خام سؤال"><input class="input" type="text" maxlength="2000" data-grade-comment value="${escapeHtml(answer.gradingComment || '')}" placeholder="توضیح تصحیح" style="max-width:180px"><button class="btn btn-primary btn-sm" data-grade-answer="${answer.questionId}">ثبت نمره</button></div><small class="text-soft">حداکثر نمره خام: ${faNumber(answer.maxRawScore || 0)}</small>`
            : (isEssay && answer.isCorrect == null ? 'در انتظار تصحیح تشریحی' : answer.isCorrect == null ? 'کلید پاسخ تعریف نشده' : answer.isCorrect ? 'صحیح' : 'نادرست');
          return `<tr>
            <td>${faNumber(index + 1)}</td><td>${escapeHtml(answer.question || `سؤال ${answer.questionId}`)}</td>
            <td>${escapeHtml(answer.answer || 'بدون پاسخ')}</td><td>${escapeHtml(answer.correctAnswer || '—')}</td>
            <td>${gradeControl}</td></tr>`;
        }).join('') : '<tr><td colspan="5">پاسخی ثبت نشده است.</td></tr>';

        if (canGrade) body.querySelectorAll('[data-grade-answer]').forEach(button => button.addEventListener('click', async () => {
          const row = button.closest('tr');
          const score = Number(row?.querySelector('[data-grade-score]')?.value);
          const comment = row?.querySelector('[data-grade-comment]')?.value || null;
          if (!Number.isFinite(score) || score < 0) return notify('نمره واردشده معتبر نیست.', 'warn');
          button.disabled = true;
          try {
            await request(`/results/${encodeURIComponent(attemptId)}/answers/${encodeURIComponent(button.dataset.gradeAnswer)}/grade`, { method: 'PUT', body: JSON.stringify({ score, comment }) });
            notify('نمره سؤال ثبت شد.', 'ok');
            await loadResults();
          } catch (error) { notify(error.message || 'ثبت نمره ناموفق بود.', 'danger'); button.disabled = false; }
        }));
      }

      const pdfBox = $('#submissionPdfBox');
      if (pdfBox && canGrade) {
        try {
          const info = await request(`/exam-files/attempts/${encodeURIComponent(attemptId)}/info`);
          pdfBox.hidden = false;
          pdfBox.innerHTML = `<div class="section-title"><h3>پاسخ‌نامه تشریحی دانشجو</h3></div><p class="text-soft text-sm">${escapeHtml(info.fileName)} — ${faNumber(Math.ceil(info.size / 1024))} کیلوبایت</p><button type="button" class="btn btn-outline" id="previewSubmissionPdf">مشاهده پاسخ‌نامه</button> <button type="button" class="btn btn-outline" id="downloadSubmissionPdf">دریافت فایل</button>`;
          $('#previewSubmissionPdf')?.addEventListener('click', () => window.MftApi.preview(`/exam-files/attempts/${encodeURIComponent(attemptId)}`).catch(error => notify(error.message, 'danger')));
          $('#downloadSubmissionPdf')?.addEventListener('click', () => window.MftApi.download(`/exam-files/attempts/${encodeURIComponent(attemptId)}`).catch(error => notify(error.message, 'danger')));
        } catch { pdfBox.hidden = true; pdfBox.innerHTML = ''; }
      } else if (pdfBox) { pdfBox.hidden = true; pdfBox.innerHTML = ''; }

      const publishButton = $('#publishExamResults');
      if (publishButton) {
        publishButton.hidden = !canGrade || result.resultsPublished === true;
        publishButton.onclick = async () => {
          publishButton.disabled = true;
          try {
            await request(`/results/exams/${encodeURIComponent(result.examId)}/publish`, { method: 'POST' });
            notify('نتایج آزمون منتشر شد.', 'ok');
            await loadResults();
          } catch (error) { notify(error.message || 'انتشار نتایج ناموفق بود.', 'danger'); }
          finally { publishButton.disabled = false; }
        };
      }
      const unpublishButton = $('#unpublishExamResults');
      if (unpublishButton) {
        unpublishButton.hidden = !canGrade || result.resultsPublished !== true;
        unpublishButton.onclick = async () => {
          unpublishButton.disabled = true;
          try {
            await request(`/results/exams/${encodeURIComponent(result.examId)}/unpublish`, { method: 'POST' });
            notify('انتشار نتایج لغو شد؛ اکنون می‌توانید نمرات را اصلاح کنید.', 'ok');
            await loadResults();
          } catch (error) { notify(error.message || 'لغو انتشار ناموفق بود.', 'danger'); }
          finally { unpublishButton.disabled = false; }
        };
      }
      return;
    }
    const rows = await request(`/results${examId ? `?examId=${encodeURIComponent(examId)}` : ''}`);
    const header = $('body[data-active="results"] table thead tr');
    if (header) header.innerHTML = '<th>#</th><th>آزمون</th><th>دانشجو</th><th>وضعیت</th><th>نمره</th>';
    renderRows('body[data-active="results"] table tbody', rows, row => `<tr>
      <td><a href="results.html?attemptId=${encodeURIComponent(row.id)}">${escapeHtml(String(row.id).slice(0, 8))}</a></td><td>${escapeHtml(row.examTitle)}</td><td>${escapeHtml(row.studentName || '—')}</td>
      <td>${row.status === 'InProgress' ? 'در حال برگزاری' : row.status === 'AwaitingPublication' ? 'آماده انتشار نتیجه' : row.status === 'Submitted' ? 'ثبت شده / در انتظار تصحیح' : 'تصحیح شده'}</td>
      <td>${row.score == null ? (row.resultsPublished ? 'در انتظار تصحیح' : 'منتشر نشده') : faNumber(row.score)}</td></tr>`, 5);

    const publishButton = $('#publishExamResults');
    if (publishButton) {
      publishButton.hidden = !examId || role === 'student' || rows.some(row => row.resultsPublished);
      publishButton.onclick = async () => {
        if (!examId || publishButton.disabled) return;
        publishButton.disabled = true;
        try {
          await request(`/results/exams/${encodeURIComponent(examId)}/publish`, { method: 'POST' });
          notify('نتایج آزمون منتشر شد.', 'ok');
          await loadResults();
        } catch (error) { notify(error.message || 'انتشار نتایج ناموفق بود.', 'danger'); }
        finally { publishButton.disabled = false; }
      };
    }
    const unpublishButton = $('#unpublishExamResults');
    if (unpublishButton) {
      unpublishButton.hidden = !examId || role === 'student' || !rows.some(row => row.resultsPublished);
      unpublishButton.onclick = async () => {
        if (!examId || unpublishButton.disabled) return;
        unpublishButton.disabled = true;
        try {
          await request(`/results/exams/${encodeURIComponent(examId)}/unpublish`, { method: 'POST' });
          notify('انتشار نتایج لغو شد؛ می‌توانید نمرات را اصلاح کنید.', 'ok');
          await loadResults();
        } catch (error) { notify(error.message || 'لغو انتشار ناموفق بود.', 'danger'); }
        finally { unpublishButton.disabled = false; }
      };
    }
  }

  async function loadTeacherGrades() {
    if (!document.body.matches('[data-active="dashboard"]') || (currentUser()?.role || '').toLowerCase() !== 'teacher') return;
    const attempts = await request('/results');
    const pending = attempts.filter(attempt => attempt.status === 'Submitted');
    const list = $('#teacherPendingPlaceholder');
    if (list) list.innerHTML = pending.length ? pending.map(attempt => `<li class="flex items-center justify-between" style="padding:10px 0;border-bottom:1px solid var(--border)"><div><b>${escapeHtml(attempt.studentName || 'دانشجو')}</b><div class="text-soft text-sm">${escapeHtml(attempt.examTitle)} — ارسال ${attempt.submittedAt ? new Date(attempt.submittedAt).toLocaleString('fa-IR') : 'بدون تاریخ'}</div></div><button class="btn btn-outline btn-sm" data-grade-attempt="${attempt.id}">تصحیح</button></li>`).join('') : '<li class="text-soft">برگه‌ای در انتظار تصحیح نیست.</li>';
    const count = $('#mftPendingGradeCount');
    if (count) count.textContent = `${faNumber(pending.length)} مورد باقی‌مانده`;

    const exams = await request('/exams');
    const upcoming = $('#mftUpcomingExams');
    if (upcoming) upcoming.innerHTML = exams.length ? exams.map(exam => `<li class="flex items-center gap-12"><div class="mohr sm"><div class="mohr-inner"><b>${escapeHtml(exam.title.slice(0, 2))}</b></div></div><div><b style="font-size:13px">${escapeHtml(exam.title)}</b><div class="text-soft text-sm">${exam.startsAt ? new Date(exam.startsAt).toLocaleString('fa-IR') : 'زمان‌بندی نشده'} — ${faNumber(exam.durationMinutes)} دقیقه</div></div></li>`).join('') : '<li class="text-soft">هنوز آزمونی ثبت نشده است.</li>';
  }

  async function loadStudentPerformance() {
    if (!document.body.matches('[data-active="dashboard"]') || (currentUser()?.role || '').toLowerCase() !== 'student') return;
    const attempts = await request('/results');
    const scored = attempts.filter(attempt => attempt.score != null).sort((a, b) => new Date(a.submittedAt || a.startedAt) - new Date(b.submittedAt || b.startedAt));
    window.MftChartData = { ...(window.MftChartData || {}), performance: scored.map((attempt, index) => ({ label: attempt.examTitle || `آزمون ${index + 1}`, score: attempt.score })) };
    document.dispatchEvent(new Event('mft:data-ready'));
  }

  async function loadDashboardExtras() {
    if (!document.body.matches('[data-active="dashboard"]')) return;
    const role = (currentUser()?.role || '').toLowerCase();
    if (role === 'superadmin') {
      const [activity, departments, notifications] = await Promise.all([
        request('/dashboard/activity'),
        request('/reports/departments').catch(() => []),
        request('/notifications').catch(() => [])
      ]);
      window.MftChartData = { ...(window.MftChartData || {}), activity, departments };
      document.dispatchEvent(new Event('mft:data-ready'));
      const activityList = $('#dashboardRecentActivity');
      if (activityList) activityList.innerHTML = activity.length ? activity.slice(-5).reverse().map(item => `<li><b>${new Date(item.date).toLocaleDateString('fa-IR')}</b><div class="text-soft text-sm">${faNumber(item.count)} تلاش آزمون ثبت شده است.</div></li>`).join('') : '<li class="text-soft">در بازه اخیر فعالیت آزمونی ثبت نشده است.</li>';
      const unreadCount = $('#dashboardUnreadCount');
      if (unreadCount) unreadCount.textContent = `${faNumber(notifications.filter(item => !item.isRead).length)} خوانده‌نشده`;
      const notificationList = $('#dashboardRecentNotifications');
      if (notificationList) notificationList.innerHTML = notifications.length
        ? notifications.slice(0, 3).map(item => `<li class="alert alert-info"><b>${escapeHtml(item.title)}</b><div>${escapeHtml(item.message)}</div></li>`).join('')
        : '<li class="text-soft">اعلانی ثبت نشده است.</li>';
    }
    if (role === 'department') {
      const exams = await request('/exams');
      renderRows('#departmentExamsBody', exams, exam => `<tr><td>${escapeHtml(exam.title)}</td><td>—</td><td>${exam.startsAt ? new Date(exam.startsAt).toLocaleDateString('fa-IR') : '—'}</td><td>—</td><td>${exam.isPublished ? 'منتشرشده' : 'پیش‌نویس'}</td><td><a href="results.html?examId=${exam.id}" class="btn btn-ghost btn-sm">نتایج</a></td></tr>`, 6);
    }
  }

  async function openGradeAttempt(id) {
    const result = await request(`/results/${id}`);
    const modal = $('#gradeModal');
    if (!modal) return;
    modal.dataset.attemptId = String(id);
    $('#gradeExamTitle', modal).textContent = `${result.exam} — ${result.answers.length} پاسخ`;
    const answerDetails = $('#gradeAnswerDetails', modal);
    answerDetails.innerHTML = result.answers.map((answer, index) => `<div style="padding:8px 0;border-bottom:1px solid var(--border)"><b>سؤال ${faNumber(index + 1)}:</b> ${escapeHtml(answer.question || '')}<br><span>پاسخ دانشجو: ${escapeHtml(answer.answer || 'بدون پاسخ')}</span><br><span>نمره خودکار: ${faNumber(answer.awardedScore)}</span></div>`).join('') || 'پاسخ متنی ثبت نشده است؛ برگه دست‌نویس را از دکمه زیر بررسی کنید.';
    const submissionActions = document.createElement('div');
    submissionActions.className = 'flex gap-8 mt-8';
    const submissionPreview = document.createElement('button');
    submissionPreview.type = 'button'; submissionPreview.className = 'btn btn-primary';
    submissionPreview.textContent = 'مشاهده برگه پاسخ';
    submissionPreview.addEventListener('click', async () => {
      try { await window.MftApi.preview(`/exam-files/attempts/${encodeURIComponent(id)}`); }
      catch (error) { notify(error.message || 'پیش‌نمایش پاسخ دانشجو در دسترس نیست.', 'danger'); }
    });
    const submissionDownload = document.createElement('button');
    submissionDownload.type = 'button'; submissionDownload.className = 'btn btn-outline';
    submissionDownload.textContent = 'دانلود برگه پاسخ';
    submissionDownload.addEventListener('click', async () => {
      try { await window.MftApi.download(`/exam-files/attempts/${encodeURIComponent(id)}`); }
      catch (error) { notify(error.message || 'فایل پاسخ دانشجو در دسترس نیست.', 'danger'); }
    });
    submissionActions.append(submissionPreview, submissionDownload);
    answerDetails.append(submissionActions);
    const score = $('[data-grade-score]', modal);
    if (score) { score.max = String(result.maxScore ?? 0); score.value = result.score ?? ''; score.placeholder = `حداکثر ${result.maxScore ?? 0}`; }
    modal.classList.add('show');
  }

  async function saveGrade(modal) {
    const id = modal.dataset.attemptId;
    const input = $('[data-grade-score]', modal);
    const score = Number(input?.value);
    if (!id || !Number.isFinite(score) || score < 0) return notify('نمره معتبر وارد کنید.', 'warn');
    await safe(async () => {
      await request(`/results/${id}/grade`, { method: 'PUT', body: JSON.stringify(score) });
      modal.classList.remove('show');
      await loadTeacherGrades();
    }, 'نمره ثبت شد.');
  }

  function exportTableCsv() {
    const table = $('body[data-active="reports"] table');
    if (!table) return notify('جدولی برای خروجی وجود ندارد.', 'warn');
    const csv = [...table.rows].map(row => [...row.cells].map(cell => `"${cell.innerText.replace(/"/g, '""').replace(/\s+/g, ' ').trim()}"`).join(',')).join('\r\n');
    const blob = new Blob(['\uFEFF', csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a'); link.href = url; link.download = 'mft-department-report.csv'; link.click(); URL.revokeObjectURL(url);
  }

  async function loadNotificationIndicators() {
    const [unread, recent] = await Promise.all([
      request('/notifications?unread=true').catch(() => []),
      request('/notifications').catch(() => [])
    ]);
    const count = Array.isArray(unread) ? unread.length : 0;
    document.querySelectorAll('[data-notification-count]').forEach(element => {
      element.textContent = faNumber(count);
      element.hidden = count === 0;
    });
    const recentBox = $('#topbarRecentNotifications');
    const recentRows = Array.isArray(recent) ? recent : [];
    if (recentBox) recentBox.innerHTML = recentRows.length
      ? recentRows.slice(0, 4).map(item => `<a href="notifications.html" style="display:block;padding:8px 10px;border-bottom:1px solid var(--border);white-space:normal"><b>${escapeHtml(item.title)}</b><div class="text-soft text-sm">${escapeHtml(item.message)}</div></a>`).join('')
      : '<div class="text-soft">اعلانی وجود ندارد.</div>';
  }

  async function loadNotifications() {
    if (!document.body.matches('[data-active="notifications"]')) return;
    const rows = await request('/notifications');
    const list = $('[data-notification-list]') || $('.notification-list');
    if (!list) return;
    const renderNotification = row => `<article class="notification-item ${row.isRead ? '' : 'unread'}" data-notification-id="${row.id}">
      <div><b>${escapeHtml(row.title)}</b><p>${escapeHtml(row.message)}</p><small>${new Date(row.createdAt).toLocaleString('fa-IR')}</small></div>
      <div class="flex gap-8"><button class="btn btn-sm" data-notification-read="${row.id}" ${row.isRead ? 'disabled' : ''}>خوانده شد</button>
      <button class="btn btn-sm btn-danger" data-notification-delete="${row.id}">حذف</button></div></article>`;
    list.innerHTML = rows.length ? rows.map(renderNotification).join('') : '<div class="empty-state">اعلانی وجود ندارد.</div>';
    const unreadPanel = $('#tUnread');
    const readPanel = $('#tRead');
    if (unreadPanel) unreadPanel.innerHTML = rows.some(row => !row.isRead) ? rows.filter(row => !row.isRead).map(renderNotification).join('') : '<div class="empty-state">اعلان خوانده‌نشده‌ای وجود ندارد.</div>';
    if (readPanel) readPanel.innerHTML = rows.some(row => row.isRead) ? rows.filter(row => row.isRead).map(renderNotification).join('') : '<div class="empty-state">اعلان خوانده‌شده‌ای وجود ندارد.</div>';
    const tabs = $$('[data-tabs] .tab-btn');
    if (tabs.length >= 3) { tabs[0].textContent = `همه (${faNumber(rows.length)})`; tabs[1].textContent = `خوانده‌نشده (${faNumber(rows.filter(row => !row.isRead).length)})`; tabs[2].textContent = `خوانده‌شده (${faNumber(rows.filter(row => row.isRead).length)})`; }
  }

  async function loadDashboard() {
    if (!$('[data-dashboard-summary]') && !document.querySelector('body[data-active="dashboard"]')) return;
    const data = await request('/dashboard/summary');
    $$('[data-metric]').forEach(element => {
      const key = element.dataset.metric;
      if (key in data) element.textContent = faNumber(data[key]);
    });
    $$('[data-count]').forEach(element => {
      const key = element.dataset.metric;
      if (key && key in data) {
        element.dataset.count = data[key];
        element.textContent = faNumber(data[key]);
      }
    });
    if (document.getElementById('chartActivity')) {
      const activity = await request('/dashboard/activity').catch(() => []);
      window.MftChartData = { ...(window.MftChartData || {}), activity };
      document.dispatchEvent(new Event('mft:data-ready'));
    }
  }

  async function loadProfile() {
    const form = $('body[data-active="profile"] #tInfo');
    if (!form) return;
    const profile = await request('/users/me/profile');
    $('[data-profile-name]') && ($('[data-profile-name]').textContent = profile.fullName || profile.username);
    $('[data-profile-summary]') && ($('[data-profile-summary]').textContent = `${profile.role || 'کاربر'} — ${profile.department || 'بدون دپارتمان'} — ${profile.username || ''}`);
    $('[data-profile-role]') && ($('[data-profile-role]').textContent = profile.role || '—');
    $('[data-profile-department]') && ($('[data-profile-department]').textContent = profile.department || '—');
    const inputs = $$('input', form);
    const name = inputs.find(input => input.type === 'text');
    if (name) name.value = profile.fullName || '';
    const email = $('input[type="email"]', form);
    const mobile = $('input[type="tel"]', form);
    const department = $$('input', form).find(input => input.disabled);
    if (email) email.value = profile.email || '';
    if (mobile) mobile.value = profile.mobile || '';
    if (department) department.value = profile.department || '';
    const save = $('button', form);
    save?.addEventListener('click', event => {
      event.preventDefault();
      safe(() => request('/users/me/profile', { method: 'PUT', body: JSON.stringify({ fullName: name?.value.trim(), email: email?.value.trim() || null, mobile: mobile?.value.trim() || null }) }), 'اطلاعات پروفایل ذخیره شد.');
    }, { once: true });

    const passPanel = $('#tPass');
    const passButton = $('button', passPanel);
    passButton?.addEventListener('click', event => {
      event.preventDefault();
      const fields = $$('input[type="password"]', passPanel);
      const [currentPassword, newPassword, repeatPassword] = fields.map(field => field.value);
      if (!currentPassword || !newPassword || newPassword.length < 8) return notify('رمز فعلی و رمز جدید معتبر الزامی است.', 'warn');
      if (newPassword !== repeatPassword) return notify('تکرار رمز جدید مطابقت ندارد.', 'warn');
      safe(() => request('/auth/change-password', { method: 'POST', body: JSON.stringify({ currentPassword, newPassword }) }), 'رمز عبور تغییر کرد.');
    });
  }

  async function loadSettings() {
    if (!document.body.matches('[data-active="settings"]')) return;
    const settings = await request('/settings');
    const map = new Map(settings.map(item => [item.key, item.value]));
    $$('[data-setting-key]').forEach(input => {
      const value = map.get(input.dataset.settingKey);
      if (value == null) return;
      if (input.type === 'checkbox') input.checked = value === 'true';
      else input.value = value;
    });
  }

  async function saveSettings() {
    await safe(async () => {
      for (const input of $$('[data-setting-key]')) {
        const value = input.type === 'checkbox' ? String(input.checked) : input.value;
        await request('/settings', { method: 'PUT', body: JSON.stringify({ key: input.dataset.settingKey, value }) });
      }
    }, 'تنظیمات ذخیره شد.');
  }

  async function loadRecentAttempts() {
    const body = $('[data-recent-attempts]');
    if (!body) return;
    const params = new URLSearchParams({ take: '100' });
    const departmentId = $('[data-report-department]')?.value;
    const search = $('[data-report-search]')?.value.trim();
    if (departmentId) params.set('departmentId', departmentId);
    if (search) params.set('search', search);
    const rows = await request('/reports/recent-attempts?' + params.toString());
    if (!Array.isArray(rows)) throw new Error('پاسخ تلاش‌های اخیر معتبر نیست.');
    body.innerHTML = rows.length ? rows.map(row => {
      const status = row.status === 'Graded' ? 'تصحیح‌شده' : row.status === 'Submitted' ? 'ارسال‌شده' : escapeHtml(row.status);
      const date = value => value ? new Date(value).toLocaleString('fa-IR') : '—';
      return `<tr><td>${escapeHtml(row.department)}</td><td>${escapeHtml(row.student)}</td><td>${escapeHtml(row.exam)}</td><td>${date(row.startedAt)}</td><td>${date(row.submittedAt)}</td><td>${status}</td><td>${row.score == null ? 'در انتظار تصحیح' : Number(row.score).toLocaleString('fa-IR')}</td></tr>`;
    }).join('') : '<tr><td colspan="7" class="text-soft" style="text-align:center;padding:24px">تلاشی برای این فیلتر پیدا نشد.</td></tr>';
  }

  async function loadReports() {
    if (!document.body.matches('[data-active="reports"]')) return;
    const rows = await request('/reports/departments');
    if (!Array.isArray(rows)) throw new Error('پاسخ گزارش دپارتمان‌ها معتبر نیست.');
    const table = $('body[data-active="reports"] .card table tbody');
    if (table) renderRows('body[data-active="reports"] table tbody', rows, row => `<tr><td>${escapeHtml(row.department)}</td><td>${faNumber(row.students)}</td><td>${faNumber(row.teachers)}</td><td>${faNumber(row.exams)}</td><td>${Number(row.averageScore || 0).toLocaleString('fa-IR')}</td><td>${Number(row.passRate || 0).toLocaleString('fa-IR', { maximumFractionDigits: 1 })}%</td><td>${faNumber(row.participation)}</td></tr>`, 7);
    const totalExams = rows.reduce((sum, row) => sum + Number(row.exams || 0), 0);
    const gradedAttempts = rows.reduce((sum, row) => sum + Number(row.gradedAttempts || 0), 0);
    const passedAttempts = rows.reduce((sum, row) => sum + Number(row.passedAttempts || 0), 0);
    const weightedPassRate = gradedAttempts ? passedAttempts * 100 / gradedAttempts : 0;
    const totalStudents = rows.reduce((sum, row) => sum + Number(row.students || 0), 0);
    const totalAttempts = rows.reduce((sum, row) => sum + Number(row.participation || 0), 0);
    const metrics = { exams: totalExams, passRate: weightedPassRate, participation: totalAttempts, students: totalStudents };
    document.querySelectorAll('body[data-active="reports"] [data-metric]').forEach(element => {
      const key = element.dataset.metric;
      if (key in metrics) element.textContent = faNumber(Math.round(metrics[key] * 10) / 10);
    });
    const departmentFilter = $('[data-report-department]');
    if (departmentFilter) {
      const selected = departmentFilter.value;
      departmentFilter.innerHTML = '<option value="">همه دپارتمان‌ها</option>' + rows.filter(row => row.department !== 'بدون دپارتمان' && row.departmentId).map(row => `<option value="${row.departmentId}">${escapeHtml(row.department)}</option>`).join('');
      departmentFilter.value = selected;
    }
    const activity = await request('/dashboard/activity').catch(() => []);
    window.MftChartData = { ...(window.MftChartData || {}), activity, departments: rows };
    document.dispatchEvent(new Event('mft:data-ready'));
    await safe(loadRecentAttempts);
    const searchInput = $('[data-report-search]');
    const deptSelect = $('[data-report-department]');
    const refresh = $('[data-report-refresh]');
    if (searchInput && !searchInput.dataset.wired) {
      searchInput.dataset.wired = 'true';
      let timer;
      searchInput.addEventListener('input', () => { clearTimeout(timer); timer = setTimeout(() => safe(loadRecentAttempts), 300); });
    }
    if (deptSelect && !deptSelect.dataset.wired) {
      deptSelect.dataset.wired = 'true';
      deptSelect.addEventListener('change', () => safe(loadRecentAttempts));
    }
    if (refresh && !refresh.dataset.wired) {
      refresh.dataset.wired = 'true';
      refresh.addEventListener('click', () => safe(loadReports));
    }
  }

  async function setupNotificationComposer() {
    const panel = $('[data-notification-compose]');
    if (!panel) return;
    const role = (currentUser()?.role || '').toLowerCase();
    if (role !== 'superadmin') return;
    panel.hidden = false;
    const [departments, users] = await Promise.all([request('/departments'), request('/users')]);
    const departmentSelect = $('[data-notification-department]');
    const userSelect = $('[data-notification-user]');
    if (departmentSelect) departmentSelect.innerHTML = '<option value="">انتخاب دپارتمان</option>' + departments.map(item => `<option value="${item.id}">${escapeHtml(item.name)}</option>`).join('');
    if (userSelect) userSelect.innerHTML = '<option value="">انتخاب کاربر</option>' + users.filter(item => item.isActive !== false).map(item => `<option value="${item.id}">${escapeHtml(item.fullName || item.username)} — ${escapeHtml(item.role)}</option>`).join('');
    const audience = $('[data-notification-audience]');
    const sync = () => {
      const value = audience?.value;
      $('[data-notification-department-wrap]').hidden = value !== 'Department';
      $('[data-notification-user-wrap]').hidden = value !== 'User';
      $('[data-notification-class-note]').hidden = value !== 'Class';
    };
    audience?.addEventListener('change', sync);
    sync();
  }

  async function sendNotification() {
    const audience = $('[data-notification-audience]')?.value;
    const title = $('[data-notification-title]')?.value.trim();
    const message = $('[data-notification-message]')?.value.trim();
    if (!title || !message) return notify('عنوان و متن اعلان را وارد کنید.', 'warn');
    const payload = { audience, title, message };
    if (audience === 'Department') payload.departmentId = $('[data-notification-department]')?.value;
    if (audience === 'User') payload.userId = $('[data-notification-user]')?.value;
    await safe(async () => {
      const result = await request('/notifications', { method: 'POST', body: JSON.stringify(payload) });
      $('[data-notification-title]').value = '';
      $('[data-notification-message]').value = '';
      notify(`اعلان برای ${faNumber(result.sent)} نفر ثبت شد.`, 'ok');
    });
  }

  async function openUserEditor(id, role) {
    const modalId = role === 'Teacher' ? 'teacherModal' : 'studentModal';
    const modal = document.getElementById(modalId);
    if (!modal) return;
    const fields = $$('input, select', modal).filter(field => !['radio', 'checkbox', 'file'].includes(field.type));
    const nameField = $('[data-fullname-field]', modal) || fields.find(field => field.type === 'text' && !field.hasAttribute('data-username-field'));
    let existing = null;
    if (id) existing = await request(`/users/${id}`);
    if (nameField) nameField.value = existing?.fullName || '';
    const departmentField = $('[data-department-field]', modal);
    if (departmentField && existing?.departmentId) departmentField.value = String(existing.departmentId);
    const activeField = $('[data-user-active]', modal);
    if (activeField) activeField.checked = existing ? Boolean(existing.isActive) : true;
    const usernameField = fields.find(field => field.hasAttribute('data-username-field'));
    if (usernameField) usernameField.value = existing?.username || '';
    const email = fields.find(field => field.type === 'email');
    const mobile = fields.find(field => field.type === 'tel');
    if (email) email.value = existing?.email || '';
    if (mobile) mobile.value = existing?.mobile || '';
    modal.dataset.editId = id || '';
    modal.dataset.editRole = role;
    modal.classList.add('show');
  }

  async function saveUserFromModal(modal) {
    const role = modal.dataset.editRole || (modal.id === 'teacherModal' ? 'Teacher' : 'Student');
    const fields = $$('input, select', modal).filter(field => !['radio', 'checkbox', 'file'].includes(field.type));
    const fullName = $('[data-fullname-field]', modal)?.value.trim() || fields.find(field => field.type === 'text' && !field.hasAttribute('data-username-field'))?.value.trim();
    const username = fields.find(field => field.hasAttribute('data-username-field'))?.value.trim();
    const password = fields.find(field => field.type === 'password')?.value;
    const email = fields.find(field => field.type === 'email')?.value.trim() || null;
    const mobile = fields.find(field => field.type === 'tel')?.value.trim() || null;
    const id = modal.dataset.editId;
    if (!fullName) return notify('نام و نام خانوادگی الزامی است.', 'warn');
    if (!id && !username) return notify('نام کاربری الزامی است.', 'warn');
    if (!id && (!password || password.length < 8)) return notify('رمز عبور باید حداقل ۸ کاراکتر باشد.', 'warn');
    await safe(async () => {
      const departmentField = $('[data-department-field]', modal);
      const departmentId = departmentField?.value || null;
      const isActive = $('[data-user-active]', modal)?.checked ?? true;
      if (id) await request(`/users/${id}`, { method: 'PUT', body: JSON.stringify({ fullName, email, mobile, departmentId, isActive }) });
      else {
        if (!password || password.length < 8) return notify('رمز عبور باید حداقل ۸ کاراکتر باشد.', 'warn');
        await request('/users', { method: 'POST', body: JSON.stringify({ username, password, fullName, role, email, mobile, departmentId, mustChangePassword: false }) });
      }
      modal.classList.remove('show');
      await loadUsers();
    }, id ? 'اطلاعات کاربر ویرایش شد.' : 'کاربر ساخته شد.');
  }

  async function loadExamWizard() {
    if (!document.body.matches('[data-active="exams"]') || !$('#wizardSteps')) return;
    const [departments, questions] = await Promise.all([
      request('/departments').catch(() => []),
      request('/questions').catch(() => [])
    ]);
    const departmentWrap = $('#deptChoices');
    const questionWrap = $('#manualPick');
    if (departmentWrap) {
      departmentWrap.innerHTML = departments.length ? departments.map((dept, index) => `<div class="choice-card ${index === 0 ? 'selected' : ''}" data-id="${dept.id}" data-value="${escapeHtml(dept.name)}"><div class="mohr sm"><div class="mohr-inner"><b>${escapeHtml(dept.name.slice(0, 2))}</b></div></div><b>${escapeHtml(dept.name)}</b></div>`).join('') : '<p class="text-soft">دپارتمانی ثبت نشده است. از گزینه «ایجاد دپارتمان» استفاده کنید.</p>';
    }
    if (questionWrap) {
      questionWrap.innerHTML = questions.map(question => `<label class="qpick-row"><input type="checkbox" data-question-type="${escapeHtml(question.type)}" value="${question.id}"><span style="flex:1">${escapeHtml(question.text)}</span><span class="badge badge-info">${escapeHtml(question.type)}</span></label>`).join('') || '<p class="text-soft">ابتدا از بانک سؤال، سؤال ثبت کنید.</p>';
    }
    const teacherWrap = $('#teacherChoices');
    if (teacherWrap) {
      const me = await request('/auth/me').catch(() => null);
      const teachers = me?.role === 'Teacher' ? [me] : await request('/users?role=Teacher').catch(() => []);
      teacherWrap.innerHTML = teachers.map((teacher, index) => `<div class="choice-card ${index === 0 ? 'selected' : ''}" data-id="${teacher.id}" data-value="${escapeHtml(teacher.fullName)}"><div class="mohr sm"><div class="mohr-inner"><b>${escapeHtml(teacher.fullName.slice(0, 2))}</b></div></div><b>${escapeHtml(teacher.fullName)}</b></div>`).join('') || '<p class="text-soft">استادی برای انتخاب وجود ندارد. می‌توانید استاد جدید ایجاد کنید.</p>';
    }
  }

  function wireClicks() {
    document.addEventListener('click', event => {
      const opener = event.target.closest('[data-modal-open="studentModal"], [data-modal-open="teacherModal"], [data-modal-open="questionModal"], [data-modal-open="deptModal"]');
      if (!opener) return;
      const modal = document.getElementById(opener.dataset.modalOpen);
      if (!modal) return;
      delete modal.dataset.editId;
      if (modal.id === 'teacherModal') modal.dataset.editRole = 'Teacher';
      if (modal.id === 'studentModal') modal.dataset.editRole = 'Student';
      $$('input, textarea', modal).forEach(field => { if (!['radio', 'checkbox', 'file'].includes(field.type)) field.value = ''; });
      $$('input[type="radio"]', modal).forEach((field, index) => { field.checked = index === 0; });
      $$('select', modal).forEach(select => { select.selectedIndex = 0; });
      const activeField = $('[data-user-active]', modal);
      if (activeField) activeField.checked = true;
      if ($('[data-question-difficulty]', modal)) $('[data-question-difficulty]', modal).value = 'Medium';
      if ($('#mcqOptions', modal)) $('#mcqOptions', modal).style.display = 'block';
      if ($('#matchingOptions', modal)) $('#matchingOptions', modal).hidden = true;
    });
    document.addEventListener('click', event => {
      const gradeSave = event.target.closest('[data-grade-save]');
      if (gradeSave) { event.preventDefault(); saveGrade(gradeSave.closest('.modal-overlay')); return; }
      const gradeAttempt = event.target.closest('[data-grade-attempt]');
      if (gradeAttempt) { event.preventDefault(); safe(() => openGradeAttempt(gradeAttempt.dataset.gradeAttempt)); return; }
      if (event.target.closest('[data-export-print]')) { event.preventDefault(); window.print(); return; }
      if (event.target.closest('[data-export-csv]')) { event.preventDefault(); exportTableCsv(); return; }
      if (event.target.closest('[data-backup-unavailable], [data-restore-unavailable]')) { event.preventDefault(); notify('پشتیبان‌گیری و بازیابی دیتابیس هنوز در سمت سرور پیکربندی نشده است؛ این دکمه داده‌ها را تغییر نمی‌دهد.', 'warn'); return; }
      const resetUnavailable = event.target.closest('[data-password-reset-unavailable]');
      if (resetUnavailable) { event.preventDefault(); notify('بازیابی خودکار رمز هنوز به سرویس ایمیل یا پیامک متصل نشده است؛ با مدیر سامانه تماس بگیرید.', 'warn'); return; }
      const sendNotice = event.target.closest('[data-notification-send]');
      if (sendNotice) { event.preventDefault(); sendNotification(); return; }
      const readAll = event.target.closest('[data-notification-read-all]');
      if (readAll) { event.preventDefault(); safe(async () => { await request('/notifications/read-all', { method: 'PUT' }); await loadNotifications(); }, 'همه اعلان‌ها خوانده شدند.'); return; }
      const settingsSave = event.target.closest('[data-settings-save]');
      if (settingsSave) { event.preventDefault(); saveSettings(); return; }
      const questionSave = event.target.closest('[data-question-save]');
      if (questionSave) {
        event.preventDefault();
        if (document.body.dataset.active !== 'bank') saveQuestionFromModal(questionSave.closest('.modal-overlay'));
        return;
      }
      if (event.target.closest('[data-create-department]')) { event.preventDefault(); safe(createDepartmentInline); return; }
      if (event.target.closest('[data-create-manager]')) { event.preventDefault(); safe(createManagerInline); return; }
      if (event.target.closest('[data-create-teacher]')) { event.preventDefault(); safe(createTeacherInline); return; }
      const departmentSave = event.target.closest('[data-department-save]');
      if (departmentSave) { event.preventDefault(); saveDepartmentFromModal(departmentSave.closest('.modal-overlay')); return; }
      const userSave = event.target.closest('[data-user-save]');
      if (userSave) { event.preventDefault(); saveUserFromModal(userSave.closest('.modal-overlay')); return; }
    });
    document.addEventListener('click', async event => {
      const target = event.target.closest('[data-user-delete], [data-user-edit], [data-question-delete], [data-question-edit], [data-department-delete], [data-department-edit], [data-notification-delete], [data-notification-read], [data-exam-start], [data-result-view]');
      if (!target) return;
      event.preventDefault();
      await safe(async () => {
        if (target.dataset.userDelete) {
          if (!confirm('از حذف این کاربر مطمئن هستید؟')) return;
          await request(`/users/${target.dataset.userDelete}`, { method: 'DELETE' });
          await loadUsers();
        } else if (target.dataset.userEdit) {
          await openUserEditor(target.dataset.userEdit, target.dataset.userRole || 'Student');
        } else if (target.dataset.questionDelete) {
          if (!confirm('این سؤال حذف شود؟')) return;
          await request(`/questions/${target.dataset.questionDelete}`, { method: 'DELETE' });
          await loadQuestions();
        } else if (target.dataset.questionEdit) {
          await editQuestion(target.dataset.questionEdit);
        } else if (target.dataset.departmentDelete) {
          if (!confirm('این دپارتمان حذف شود؟')) return;
          await request(`/departments/${target.dataset.departmentDelete}`, { method: 'DELETE' });
          await loadDepartments();
        } else if (target.dataset.departmentEdit) {
          await editDepartment(target.dataset.departmentEdit);
        } else if (target.dataset.notificationDelete) {
          await request(`/notifications/${target.dataset.notificationDelete}`, { method: 'DELETE' });
          await loadNotifications();
        } else if (target.dataset.notificationRead) {
          await request(`/notifications/${target.dataset.notificationRead}/read`, { method: 'PUT' });
          await loadNotifications();
        } else if (target.dataset.examStart) {
          const attempt = await request(`/exams/${target.dataset.examStart}/start`, { method: 'POST' });
          sessionStorage.setItem('mft.activeAttempt', JSON.stringify(attempt));
          location.href = 'online-exam.html';
        } else if (target.dataset.resultView) {
          const result = await request(`/results/${target.dataset.resultView}`);
          const answers = result.answers.map(answer => `سؤال ${answer.questionId}: ${escapeHtml(answer.answer || 'بدون پاسخ')} — نمره ${faNumber(answer.awardedScore)}`).join('\n');
          alert(`${result.exam}\nوضعیت: ${result.status}\nنمره: ${result.score ?? 'در انتظار تصحیح'}\n\n${answers}`);
        }
      });
    });

  }

  async function saveQuestionFromModal(modal) {
    const text = $('[data-question-text]', modal)?.value.trim();
    const type = $('#qType', modal)?.value || 'MultipleChoice';
    const difficulty = $('[data-question-difficulty]', modal)?.value || 'Medium';
    const category = $('[data-question-category]', modal)?.value || 'عمومی';
    const optionFields = $$('[data-question-option]', modal);
    let options = optionFields.map(field => field.value.trim()).filter(Boolean);
    const correctIndex = Number($('#correctOption', modal)?.value || 0) - 1;
    let correctAnswer = type === 'MultipleChoice' ? (optionFields[correctIndex]?.value.trim() || null) : null;
    if (type === 'Matching') {
      const left = $$('[data-match-left]', modal).map(field => field.value.trim());
      const right = $$('[data-match-right]', modal).map(field => field.value.trim());
      const pairs = left.map((value, index) => ({ left: value, right: right[index] })).filter(pair => pair.left && pair.right);
      options = pairs.map(pair => pair.left);
      if (pairs.length < 2 || new Set(pairs.map(pair => pair.right.toLocaleLowerCase())).size !== pairs.length)
        return notify('حداقل دو جفت معتبر با پاسخ‌های غیرتکراری وارد کنید.', 'warn');
      correctAnswer = JSON.stringify(Object.fromEntries(pairs.map(pair => [pair.left, pair.right])));
    }
    if (!text) return notify('متن سؤال الزامی است.', 'warn');
    if (type === 'MultipleChoice' && (options.length < 2 || !correctAnswer)) return notify('حداقل دو گزینه و پاسخ صحیح را مشخص کنید.', 'warn');
    const departmentId = $('#questionDepartment', modal)?.value || null;
    if (!departmentId) return notify('برای ثبت سؤال، یک دپارتمان معتبر انتخاب کنید. اگر فهرست خالی است، مدیر سامانه باید حساب شما را به دپارتمان متصل کند.', 'warn');
    const payload = { text, departmentId, type, difficulty, category, options: ['MultipleChoice','Matching'].includes(type) ? options : [], correctAnswer, score: 1 };
    await safe(async () => {
      const id = modal.dataset.editId;
      await request(id ? `/questions/${id}` : '/questions', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      delete modal.dataset.editId;
      modal.classList.remove('show');
      await loadQuestions();
    }, 'سؤال ذخیره شد.');
  }

  async function saveDepartmentFromModal(modal) {
    const fields = $$('input, select', modal);
    const name = fields.find(field => field.type === 'text')?.value.trim();
    const managerValue = $('#departmentManagerId', modal)?.value;
    const status = fields.find(field => field.tagName === 'SELECT' && field.id !== 'departmentManagerId')?.value || 'فعال';
    if (!name) return notify('نام دپارتمان الزامی است.', 'warn');
    const payload = { name, managerUserId: managerValue || null, isActive: !['غیرفعال', 'Inactive'].includes(status) };
    await safe(async () => {
      const id = modal.dataset.editId;
      await request(id ? `/departments/${id}` : '/departments', { method: id ? 'PUT' : 'POST', body: JSON.stringify(payload) });
      delete modal.dataset.editId;
      modal.classList.remove('show');
      await loadDepartments();
    }, 'دپارتمان ذخیره شد.');
  }

  async function createDepartmentInline() {
    if ((currentUser()?.role || '').toLowerCase() !== 'superadmin') {
      return notify('فقط مدیر کل امکان ایجاد دپارتمان دارد.', 'warn');
    }
    const values = await showQuickCreateModal('ایجاد دپارتمان جدید', [
      { name: 'name', label: 'نام دپارتمان', placeholder: 'مثلاً مهندسی کامپیوتر' }
    ]);
    if (!values) return;
    const created = await request('/departments', {
      method: 'POST',
      body: JSON.stringify({ name: values.name, managerUserId: null, isActive: true })
    });
    const rows = await request('/departments');
    $$('[data-department-field]').forEach(select => {
      const selected = select.value;
      select.innerHTML = '<option value="">انتخاب دپارتمان</option>' +
        rows.map(dept => `<option value="${dept.id}">${escapeHtml(dept.name)}</option>`).join('');
      select.value = created.id;
      if (selected && selected !== created.id) select.value = selected;
    });
    if ($('#deptChoices')) {
      await loadExamWizard();
      const newCard = $$('#deptChoices .choice-card').find(card => card.dataset.id === created.id);
      if (newCard) {
        $$('#deptChoices .choice-card').forEach(card => card.classList.toggle('selected', card === newCard));
        $('#prevDept').textContent = newCard.dataset.value || newCard.textContent.trim();
      }
    }
    if ($('body[data-active="departments"]')) await loadDepartments();
    notify('دپارتمان ایجاد شد و به فهرست انتخاب اضافه شد.', 'ok');
  }

  function showQuickCreateModal(title, fields) {
    return new Promise(resolve => {
      const overlay = document.createElement('div');
      overlay.className = 'modal-overlay show';
      overlay.style.zIndex = '220';
      const box = document.createElement('div');
      box.className = 'modal-box';
      const head = document.createElement('div');
      head.className = 'modal-head';
      const heading = document.createElement('h3');
      heading.style.fontSize = '16px';
      heading.textContent = title;
      const close = document.createElement('button');
      close.type = 'button'; close.className = 'icon-btn'; close.textContent = '✕';
      head.append(heading, close);
      const body = document.createElement('div');
      body.className = 'modal-body';
      const controls = {};
      fields.forEach(field => {
        const wrapper = document.createElement('div'); wrapper.className = 'field';
        const label = document.createElement('label'); label.textContent = field.label;
        const input = document.createElement('input'); input.className = 'input';
        input.type = field.type || 'text'; input.autocomplete = 'off';
        input.placeholder = field.placeholder || ''; input.required = true;
        wrapper.append(label, input); body.appendChild(wrapper); controls[field.name] = input;
      });
      const foot = document.createElement('div'); foot.className = 'modal-foot';
      const submit = document.createElement('button'); submit.type = 'button';
      submit.className = 'btn btn-primary'; submit.textContent = 'ایجاد';
      const cancel = document.createElement('button'); cancel.type = 'button';
      cancel.className = 'btn btn-outline'; cancel.textContent = 'انصراف';
      foot.append(submit, cancel); box.append(head, body, foot); overlay.appendChild(box);
      document.body.appendChild(overlay);
      const finish = value => { overlay.remove(); resolve(value); };
      close.addEventListener('click', () => finish(null));
      cancel.addEventListener('click', () => finish(null));
      overlay.addEventListener('click', event => { if (event.target === overlay) finish(null); });
      submit.addEventListener('click', () => {
        const values = Object.fromEntries(Object.entries(controls).map(([key, input]) => [key, input.value.trim()]));
        const missing = fields.find(field => !values[field.name]);
        if (missing) { notify(missing.label + ' الزامی است.', 'warn'); controls[missing.name].focus(); return; }
        finish(values);
      });
      Object.values(controls)[0]?.focus();
    });
  }

  async function createManagerInline() {
    if ((currentUser()?.role || '').toLowerCase() !== 'superadmin') {
      return notify('فقط مدیر کل امکان ایجاد مدیر دپارتمان دارد.', 'warn');
    }
    const values = await showQuickCreateModal('ایجاد مدیر دپارتمان', [
      { name: 'fullName', label: 'نام و نام خانوادگی مدیر' },
      { name: 'username', label: 'نام کاربری' },
      { name: 'password', label: 'رمز عبور (حداقل ۸ کاراکتر)', type: 'password' }
    ]);
    if (!values) return;
    const created = await request('/users', {
      method: 'POST',
      body: JSON.stringify({ username: values.username, password: values.password, fullName: values.fullName, role: 'Department', email: null, mobile: null, departmentId: null, mustChangePassword: false })
    });
    const select = $('#departmentManagerId');
    if (select) {
      const managers = await request('/users?role=Department');
      select.innerHTML = '<option value="">بدون مدیر</option>' +
        managers.map(manager => `<option value="${manager.id}">${escapeHtml(manager.fullName)}</option>`).join('');
      select.value = created.id;
    }
    notify('حساب مدیر ایجاد شد؛ با ذخیره دپارتمان به آن متصل می‌شود.', 'ok');
  }

  async function createTeacherInline() {
    const role = (currentUser()?.role || '').toLowerCase();
    if (!['superadmin', 'department'].includes(role)) {
      return notify('ایجاد استاد از این بخش برای نقش فعلی مجاز نیست.', 'warn');
    }
    const values = await showQuickCreateModal('ایجاد حساب استاد', [
      { name: 'fullName', label: 'نام و نام خانوادگی استاد' },
      { name: 'username', label: 'نام کاربری استاد' },
      { name: 'password', label: 'رمز عبور (حداقل ۸ کاراکتر)', type: 'password' }
    ]);
    if (!values) return;
    const { fullName, username } = values;
    const department = $('#deptChoices .choice-card.selected');
    const departmentId = department?.dataset.id || null;
    if (role === 'superadmin' && !departmentId) return notify('ابتدا دپارتمان مقصد را انتخاب یا ایجاد کنید.', 'warn');
    const created = await request('/users', {
      method: 'POST',
      body: JSON.stringify({ username, password: values.password, fullName, role: 'Teacher', email: null, mobile: null, departmentId, mustChangePassword: false })
    });
    await loadExamWizard();
    const newTeacherCard = $$('#teacherChoices .choice-card').find(card => card.dataset.id === created.id);
    if (newTeacherCard) {
      $$('#teacherChoices .choice-card').forEach(card => card.classList.toggle('selected', card === newTeacherCard));
      $('#prevTeacher').textContent = newTeacherCard.dataset.value || newTeacherCard.textContent.trim();
    }
    notify('استاد ایجاد شد و به فهرست انتخاب اضافه شد.', 'ok');
  }

  async function editQuestion(id) {
    const question = await request(`/questions/${id}`);
    const modal = $('#questionModal');
    if (!modal) return;
    modal.dataset.editId = String(id);
    const fields = $$('textarea, input, select', modal);
    fields.find(field => field.tagName === 'TEXTAREA').value = question.text || '';
    $('#qType', modal).value = question.type || 'MultipleChoice';
    $('[data-question-difficulty]', modal).value = question.difficulty || 'Medium';
    $('[data-question-category]', modal).value = question.category || 'عمومی';
    $$('[data-question-option]', modal).forEach((field, index) => field.value = question.type === 'MultipleChoice' ? (question.options?.[index] || '') : '');
    if (question.type === 'Matching') {
      let pairs = {};
      try { pairs = JSON.parse(question.correctAnswer || '{}'); } catch { pairs = {}; }
      $$('[data-match-left]', modal).forEach((field, index) => { field.value = question.options?.[index] || ''; });
      $$('[data-match-right]', modal).forEach((field, index) => { field.value = pairs[question.options?.[index]] || ''; });
      $('#mcqOptions', modal).style.display = 'none';
      $('#matchingOptions', modal).hidden = false;
    } else {
      $('#mcqOptions', modal).style.display = question.type === 'MultipleChoice' ? 'block' : 'none';
      $('#matchingOptions', modal).hidden = true;
    }
    const correctIndex = question.options?.indexOf(question.correctAnswer) ?? -1;
    if ($('#correctOption', modal)) $('#correctOption', modal).value = correctIndex >= 0 ? String(correctIndex + 1) : '';
    if ($('#questionDepartment', modal) && question.departmentId) $('#questionDepartment', modal).value = question.departmentId;
    modal.classList.add('show');
  }

  async function editDepartment(id) {
    const department = await request(`/departments/${id}`);
    const modal = $('#deptModal');
    if (!modal) return;
    modal.dataset.editId = String(id);
    const input = $('input[type="text"]', modal);
    if (input) input.value = department.name || '';
    if ($('#departmentManagerId', modal)) $('#departmentManagerId', modal).value = department.managerUserId || '';
    const statusSelect = $$('select', modal).find(field => field.id !== 'departmentManagerId');
    if (statusSelect) statusSelect.value = department.isActive ? 'فعال' : 'غیرفعال';
    modal.classList.add('show');
  }


  async function init() {
    wireClicks();
    await login();
    if (!token()) return;
    if ($('#studentsBody')) $('#studentsBody').replaceChildren();
    if ($('body[data-active="teachers"] table tbody')) $('body[data-active="teachers"] table tbody').replaceChildren();
    if ($('#cardView')) $('#cardView').replaceChildren();
    if (document.body.dataset.active === 'exams') { $('#deptChoices')?.replaceChildren(); $('#teacherChoices')?.replaceChildren(); $('#manualPick')?.replaceChildren(); }
    if ($('body[data-active="departments"] table tbody')) $('body[data-active="departments"] table tbody').replaceChildren();
    if ($('body[data-active="bank"] table tbody')) $('body[data-active="bank"] table tbody').replaceChildren();
    if ($('body[data-active="reports"] table tbody')) $('body[data-active="reports"] table tbody').replaceChildren();
    if ($('body[data-active="results"] table tbody')) $('body[data-active="results"] table tbody').replaceChildren();
    if ($('[data-notification-list]')) $('[data-notification-list]').replaceChildren();
    if ($('body[data-active="dashboard"] table tbody')) $('body[data-active="dashboard"] table tbody').replaceChildren();
    if ($('#departmentExamsBody')) $('#departmentExamsBody').replaceChildren();
    await safe(async () => {
      await loadCurrentUser();
      await loadNotificationIndicators();
      if (document.body.dataset.active === 'notifications') await setupNotificationComposer();
      const isSuperAdmin = (currentUser()?.role || '').toLowerCase() === 'superadmin';
      $$('[data-create-department], [data-create-manager]').forEach(button => { button.hidden = !isSuperAdmin; });
      $$('[data-create-teacher]').forEach(button => { button.hidden = !['superadmin', 'department'].includes((currentUser()?.role || '').toLowerCase()); });
      const loaders = [
        [loadUsers, ['students', 'teachers']],
        [loadDepartments, ['departments']],
        [loadQuestions, ['bank']],
        [loadExams, ['dashboard']],
        [loadResults, ['results']],
        [loadNotifications, ['notifications']],
        [loadDashboard, ['dashboard']],
        [loadTeacherGrades, ['dashboard']],
        [loadDashboardExtras, ['dashboard']],
        [loadStudentPerformance, ['dashboard']],
        [loadProfile, ['profile']],
        [loadSettings, ['settings']],
        [loadReports, ['reports']],
        [loadExamWizard, ['exams']]
      ];
      await Promise.all(loaders.filter(([, pages]) => pages.includes(document.body.dataset.active)).map(([loader]) => loader().catch(error => console.warn('Mft API adapter:', error.message))));
      if (['students', 'teachers'].includes(document.body.dataset.active)) {
        const role = (currentUser()?.role || '').toLowerCase();
        if (role === 'superadmin' || role === 'department') {
          const departments = await request('/departments').catch(() => []);
          $$('[data-department-field]').forEach(select => { select.innerHTML = '<option value="">انتخاب دپارتمان</option>' + departments.map(dept => `<option value="${dept.id}">${escapeHtml(dept.name)}</option>`).join(''); });
        }
      }
      if (document.body.dataset.active === 'students') attachSearch('body[data-active="students"] .search-box input', loadUsers);
      if (document.body.dataset.active === 'teachers') attachSearch('body[data-active="teachers"] .search-box input', loadUsers);
      if (document.body.dataset.active === 'departments') attachSearch('body[data-active="departments"] .search-box input', loadDepartments);
      if (document.body.dataset.active === 'bank') attachSearch('body[data-active="bank"] .search-box input', loadQuestions);
    });
  }

  async function preview(path) {
    const tab = window.open('about:blank', '_blank');
    if (!tab) throw new Error('مرورگر بازشدن پنجره جدید را مسدود کرد؛ اجازه Pop-up را فعال کنید.');
    try {
      const headers = new Headers();
      if (token()) headers.set('Authorization', `Bearer ${token()}`);
      const response = await fetch(`${API_BASE}${path}`, { headers });
      if (!response.ok) {
        const payload = await response.json().catch(() => null);
        throw new Error(payload?.message || `خطای دریافت فایل (${response.status})`);
      }
      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      tab.location.href = url;
      setTimeout(() => URL.revokeObjectURL(url), 60000);
    } catch (error) { tab.close(); throw error; }
  }

  async function download(path) {
    const headers = new Headers();
    if (token()) headers.set('Authorization', `Bearer ${token()}`);
    const response = await fetch(`${API_BASE}${path}`, { headers });
    if (!response.ok) {
      const payload = await response.json().catch(() => null);
      throw new Error(payload?.message || `خطای دریافت فایل (${response.status})`);
    }
    const blob = await response.blob();
    const disposition = response.headers.get('content-disposition') || '';
    const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i);
    const plain = disposition.match(/filename="?([^";]+)"?/i);
    const filename = encoded ? decodeURIComponent(encoded[1]) : (plain?.[1] || 'submission.pdf');
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url; anchor.download = filename; document.body.appendChild(anchor); anchor.click(); anchor.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }

  window.MftApi = {
    request,
    download,
    preview,
    logout() { clearSession(); location.href = 'login.html'; },
    refresh: init
  };
  document.addEventListener('DOMContentLoaded', () => { init().catch(error => console.error('Mft initialization failed:', error)); });
})();
