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
  const parseUtcDate = value => { if (!value) return null; const raw=String(value); const normalized=/(?:Z|[+-]\d{2}:?\d{2})$/i.test(raw)?raw:raw+'Z'; const date=new Date(normalized); return Number.isNaN(date.getTime())?null:date; };
  if (!document.querySelector('script[data-mft-persian-picker]')) {
    const pickerScript = document.createElement('script');
    pickerScript.src = 'assets/js/persian-date-picker.js';
    pickerScript.dataset.mftPersianPicker = 'true';
    document.head.appendChild(pickerScript);
  }
  const nativeFaDate = Date.prototype.toLocaleDateString, nativeFaDateTime = Date.prototype.toLocaleString;
  if (!Date.prototype.__mftFaPersianCalendar) {
    Date.prototype.toLocaleDateString = function(l,o) { return String(l || '').toLowerCase().startsWith('fa') ? nativeFaDate.call(this,l,{calendar:'persian',numberingSystem:'arabext',...(o || {})}) : nativeFaDate.call(this,l,o); };
    Date.prototype.toLocaleString = function(l,o) { return String(l || '').toLowerCase().startsWith('fa') ? nativeFaDateTime.call(this,l,{calendar:'persian',numberingSystem:'arabext',...(o || {})}) : nativeFaDateTime.call(this,l,o); };
    Object.defineProperty(Date.prototype,'__mftFaPersianCalendar',{value:true});
  }
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

  function attachGlobalSearch() {
    const field = $('.topbar .search-box input');
    if (!field || field.dataset.globalSearchReady) return;
    field.dataset.globalSearchReady = 'true';
    const host = field.closest('.search-box');
    host.style.position = 'relative';
    const panel = document.createElement('div');
    panel.id = 'globalSearchResults';
    panel.className = 'dropdown-menu';
    panel.style.cssText = 'display:none;position:absolute;top:calc(100% + 8px);right:0;left:0;z-index:1200;max-height:420px;overflow:auto;min-width:280px;padding:8px;background:var(--surface);border:1px solid var(--border);box-shadow:var(--shadow-lg);border-radius:12px';
    host.appendChild(panel);
    let timer;
    const close = () => { panel.style.display = 'none'; };
    const showMessage = message => {
      panel.innerHTML = '';
      const row = document.createElement('div');
      row.className = 'text-soft text-sm';
      row.style.padding = '12px';
      row.textContent = message;
      panel.appendChild(row);
      panel.style.display = 'block';
    };
    field.addEventListener('input', () => {
      clearTimeout(timer);
      const term = field.value.trim();
      if (term.length < 2) return close();
      showMessage('در حال جستجو...');
      timer = setTimeout(async () => {
        try {
          const tasks = await Promise.allSettled([
            request('/users?search=' + encodeURIComponent(term)),
            request('/departments?search=' + encodeURIComponent(term)),
            request('/canonical/classes')
          ]);
          const users = tasks[0].status === 'fulfilled' ? tasks[0].value : [];
          const departments = tasks[1].status === 'fulfilled' ? tasks[1].value : [];
          const classes = tasks[2].status === 'fulfilled' ? tasks[2].value : [];
          const results = [];
          users.filter(item => (item.fullName || '').toLocaleLowerCase().includes(term.toLocaleLowerCase()) ||
            (item.username || '').toLocaleLowerCase().includes(term.toLocaleLowerCase()))
            .slice(0, 8).forEach(item => {
              const role = String(item.role || '').toLowerCase();
              const isTeacher = role === 'teacher' || (item.roles || []).some(value => String(value).toLowerCase() === 'teacher');
              const isStudent = role === 'student' || (item.roles || []).some(value => String(value).toLowerCase() === 'student');
              if (!isTeacher && !isStudent) return;
              results.push({ title: item.fullName || item.username, detail: (isTeacher ? 'استاد' : 'دانشجو') + ' · ' + (item.department || 'بدون دپارتمان'), href: (isTeacher ? 'teachers.html' : 'students.html') + '?search=' + encodeURIComponent(term) });
            });
          departments.filter(item => (item.name || '').toLocaleLowerCase().includes(term.toLocaleLowerCase()))
            .slice(0, 4).forEach(item => results.push({ title: item.name, detail: 'دپارتمان', href: 'departments.html?search=' + encodeURIComponent(term) }));
          classes.filter(item => (item.name || '').toLocaleLowerCase().includes(term.toLocaleLowerCase()))
            .slice(0, 4).forEach(item => results.push({ title: item.name, detail: 'کلاس · ' + (item.department || ''), href: 'classes.html?search=' + encodeURIComponent(term) }));
          panel.innerHTML = '';
          if (!results.length) return showMessage('نتیجه‌ای برای جستجوی شما پیدا نشد.');
          results.slice(0, 12).forEach(item => {
            const link = document.createElement('a');
            link.href = item.href;
            link.style.cssText = 'display:flex;flex-direction:column;gap:3px;padding:10px 12px;border-radius:8px;text-decoration:none;color:var(--text)';
            const title = document.createElement('b');
            title.textContent = item.title;
            const detail = document.createElement('small');
            detail.className = 'text-soft';
            detail.textContent = item.detail;
            link.append(title, detail);
            link.addEventListener('mouseenter', () => { link.style.background = 'var(--surface-2)'; });
            link.addEventListener('mouseleave', () => { link.style.background = 'transparent'; });
            panel.appendChild(link);
          });
          panel.style.display = 'block';
        } catch (error) {
          showMessage(error.message || 'جستجو ناموفق بود.');
        }
      }, 300);
    });
    field.addEventListener('focus', () => { if (panel.childElementCount && field.value.trim().length >= 2) panel.style.display = 'block'; });
    document.addEventListener('click', event => { if (!host.contains(event.target)) close(); });
    field.addEventListener('keydown', event => { if (event.key === 'Escape') close(); });
  }

  async function loadUsers() {
    const studentBody = $('#studentsBody');
    const teacherBody = $('#teachersBody') || $('body[data-active="teachers"] table tbody');
    const searchField = $('main .table-toolbar .search-box input');
    const search = searchField?.value.trim() || new URLSearchParams(location.search).get('search') || '';

    if (studentBody) {
      const rows = await request(`/users?role=Student${search ? `&search=${encodeURIComponent(search)}` : ''}`);
      renderRows('#studentsBody', rows, row => `<tr>
        <td>${escapeHtml(row.fullName)}</td><td>${escapeHtml(row.username)}</td><td>${escapeHtml(row.department || '—')}</td>
        <td>${row.isActive ? 'فعال' : 'غیرفعال'}</td>
        <td><button class="btn btn-sm" data-user-edit="${row.id}" data-user-role="Student">ویرایش</button>
        <button class="btn btn-sm btn-danger" data-user-delete="${row.id}">حذف</button></td></tr>`, 5);
    }

    if (teacherBody && document.body.dataset.active === 'teachers') {
      const rows = await request(`/users?role=Teacher${search ? `&search=${encodeURIComponent(search)}` : ''}`);
      const cards = $('#cardView');
      if (cards) cards.innerHTML = rows.length ? rows.map(row => `<div class="card teacher-card"><div class="mohr"><div class="mohr-inner"><b>${escapeHtml(row.fullName.slice(0, 2))}</b></div></div><h4>${escapeHtml(row.fullName)}</h4><div class="spec">${escapeHtml(row.department || 'بدون دپارتمان')}</div><span class="badge ${row.isActive ? 'badge-ok' : 'badge-muted'}">${row.isActive ? 'فعال' : 'غیرفعال'}</span><div class="flex gap-8 mt-16" style="justify-content:center"><button class="btn btn-outline btn-sm" data-user-edit="${row.id}" data-user-role="Teacher">ویرایش</button><button class="btn btn-danger btn-sm" data-user-delete="${row.id}">حذف</button></div></div>`).join('') : '<p class="text-soft">استادی ثبت نشده است.</p>';
      renderRows('body[data-active="teachers"] table tbody', rows, row => `<tr>
        <td>${escapeHtml(row.fullName)}</td><td>${escapeHtml(row.department || '—')}</td>
        <td>${row.isActive ? 'فعال' : 'غیرفعال'}</td><td><button class="btn btn-sm" data-user-edit="${row.id}" data-user-role="Teacher">ویرایش</button>
        <button class="btn btn-sm btn-danger" data-user-delete="${row.id}">حذف</button></td></tr>`, 4);
    }
  }

  async function loadDepartments() {
    if (!$('body[data-active="departments"]')) return;
    const search = $('main .table-toolbar .search-box input')?.value.trim() || new URLSearchParams(location.search).get('search') || '';
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
    const search = $('main .table-toolbar .search-box input')?.value.trim() || new URLSearchParams(location.search).get('search') || '';
    const rows = await request(`/questions${search ? `?search=${encodeURIComponent(search)}` : ''}`);
    const typeNames = { MultipleChoice: 'چهارگزینه‌ای', Essay: 'تشریحی', Matching: 'وصل‌کردنی' };
    const difficultyNames = { 1: 'آسان', 2: 'متوسط', 3: 'سخت' };
    renderRows('body[data-active="bank"] table tbody', rows, row => `<tr>
      <td>${escapeHtml(row.text)}</td><td>${escapeHtml(row.skillName || row.categoryName || '—')}</td>
      <td>${escapeHtml(typeNames[row.questionTypeCode] || row.questionTypeName || row.questionTypeCode || '—')}</td>
      <td>${escapeHtml(difficultyNames[Number(row.difficultyLevel)] || '—')}</td>
      <td><button class="btn btn-outline btn-sm" data-question-edit="${row.id}">ویرایش</button>
      <button class="btn btn-danger btn-sm" data-question-delete="${row.id}">حذف</button></td></tr>`, 5);
  }

  async function loadExams() {
    const role = (document.body.dataset.role || currentUser()?.role || '').toLowerCase();
    if (role !== 'student' || document.body.dataset.active !== 'dashboard') return;
    const rows = await request('/canonical/exams');
    const serverNow = parseUtcDate(rows[0]?.serverNowUtc)?.getTime() ?? Date.now();
    const monotonicAtSync = window.performance?.now?.() ?? Date.now();
    const now = () => serverNow + ((window.performance?.now?.() ?? Date.now()) - monotonicAtSync);
    const nextExam = rows.filter(row => row.isPublished && row.hasAttempt !== true && (!row.endAt || (parseUtcDate(row.endAt)?.getTime() ?? 0) >= now()))
      .sort((a,b) => (parseUtcDate(a.startAt)?.getTime() ?? now()) - (parseUtcDate(b.startAt)?.getTime() ?? now()))[0];

    renderRows('body[data-active="dashboard"] tbody', rows, row => {
      const already = row.hasAttempt === true, starts = parseUtcDate(row.startAt), ends = parseUtcDate(row.endAt), current = now();
      const available = row.isPublished && (!starts || starts.getTime() <= current) && (!ends || ends.getTime() >= current);
      const action = already && Number(row.existingAttemptStatusId) === 1
        ? '<button class="btn btn-primary" data-exam-continue="' + row.id + '" data-attempt-id="' + row.existingAttemptId + '">ادامه آزمون قبلی</button>'
        : already ? '<span class="badge badge-muted">تلاش ثبت نهایی شده — شرکت مجدد ممنوع</span>'
        : available ? '<button class="btn btn-primary" data-exam-start="' + row.id + '">شروع آزمون</button>'
        : '<span class="badge badge-muted">زمان شروع نرسیده یا مهلت تمام شده</span>';
      return '<tr><td>' + escapeHtml(row.title) + '</td><td>—</td><td>' + (starts ? starts.toLocaleDateString('fa-IR') : '—') +
        '</td><td>' + faNumber(row.durationMinutes) + ' دقیقه</td><td>' + (row.isPublished ? 'منتشر شده' : 'منتشر نشده') + '</td><td>' + action + '</td></tr>';
    }, 6);

    const card = $('body[data-active="dashboard"] .eyebrow')?.closest('.card');
    if (!card) return;
    const title = $('h2', card), details = $('p', card), link = $('a', card), timer = $('.mohr.lg.gold .mohr-inner', card);
    if (!nextExam) {
      if (title) title.textContent = 'آزمون پیش‌رویی وجود ندارد';
      if (details) details.textContent = 'پس از انتشار آزمون زمان‌بندی‌شده، اطلاعات آن در این بخش نمایش داده می‌شود.';
      if (link) link.remove();
      if (timer) timer.innerHTML = '<b>—</b><span>آزمون پیش‌رو</span>';
      return;
    }
    if (title) title.textContent = nextExam.title;
    if (details) details.textContent = (nextExam.startAt ? (parseUtcDate(nextExam.startAt)?.toLocaleString('fa-IR') || '—') : 'در حال برگزاری') + ' — مدت زمان: ' + faNumber(nextExam.durationMinutes) + ' دقیقه';
    const startAt = parseUtcDate(nextExam.startAt)?.getTime() ?? now();
    const endAt = parseUtcDate(nextExam.endAt)?.getTime() ?? Infinity;
    const tick = () => {
      const current = now();
      const left = startAt - current;
      if (!timer) return;
      if (left > 0) {
        const seconds = Math.floor(left / 1000), days = Math.floor(seconds / 86400), hours = Math.floor((seconds % 86400) / 3600), minutes = Math.floor((seconds % 3600) / 60), secs = seconds % 60;
        timer.innerHTML = '<b>' + faNumber(days) + ' روز</b><span>' + faNumber(hours) + ':' + String(minutes).padStart(2,'0') + ':' + String(secs).padStart(2,'0') + ' مانده</span>';
        if (link) { link.textContent = 'هنوز شروع نشده'; link.classList.remove('btn-gold'); link.classList.add('btn-outline'); link.setAttribute('aria-disabled','true'); delete link.dataset.examStart; }
      } else if (current <= endAt) {
        timer.innerHTML = '<b>اکنون</b><span>زمان شروع آزمون رسیده است</span>';
        if (link) { link.textContent = 'ورود به آزمون'; link.classList.remove('btn-outline'); link.classList.add('btn-gold'); link.setAttribute('aria-disabled','false'); link.dataset.examStart = String(nextExam.id); }
      } else {
        timer.innerHTML = '<b>پایان</b><span>مهلت شرکت تمام شده است</span>';
        if (link) { link.removeAttribute('data-exam-start'); link.setAttribute('aria-disabled','true'); link.textContent = 'مهلت پایان یافته'; }
      }
    };
    if (window.__mftUpcomingExamTimer) clearInterval(window.__mftUpcomingExamTimer);
    tick(); window.__mftUpcomingExamTimer = setInterval(tick, 1000);
  }

  async function loadResults() {
    if (!$('body[data-active="results"]')) return;
    const params = new URLSearchParams(location.search);
    const attemptId = params.get('attemptId');
    const examId = params.get('examId');
    const role = (currentUser()?.role || '').toLowerCase();
    if (attemptId) {
      let result;
      try { result = await request(`/canonical/results/${encodeURIComponent(attemptId)}`); }
      catch (error) {
        const heading = $('body[data-active="results"] h1');
        if (heading) heading.textContent = 'نتیجه هنوز منتشر نشده یا در دسترس نیست';
        const body = $('body[data-active="results"] .card table tbody');
        if (body) body.innerHTML = '<tr><td colspan="5" style="text-align:center;padding:28px">پس از تصحیح و انتشار رسمی نتیجه، جزئیات این آزمون نمایش داده می‌شود.</td></tr>';
        return;
      }
      const heading = $('body[data-active="results"] h1');
      if (heading) {
        const scoreLabel = result.isPdfOnly && result.score == null
          ? 'پاسخنامه PDF در انتظار تصحیح استاد'
          : result.status === 'PartiallyGraded' && result.autoScore != null
            ? `نمره موقت تستی ${faNumber(result.autoScore)} — ${faNumber(result.pendingManualCount)} سؤال تشریحی در انتظار تصحیح`
            : result.score != null
            ? `${faNumber(result.score)} از ${faNumber(result.maxScore)}`
            : result.autoScore != null
              ? `نمره تستی ${faNumber(result.autoScore)} از ${faNumber(result.maxScore)}`
              : (result.resultsPublished ? 'در انتظار تکمیل تصحیح' : 'هنوز نمره‌ای ثبت نشده');
        heading.textContent = `نتیجه آزمون: ${result.exam} — ${scoreLabel}`;
        const studentInfo = $('#resultStudentInfo');
        if (studentInfo) { studentInfo.hidden = !['teacher','department','superadmin'].includes(role); studentInfo.textContent = 'دانشجو: ' + (result.studentName || '—') + ' | شماره دانشجویی: ' + (result.studentNumber || 'ثبت نشده'); }
      }
      const body = $('body[data-active="results"] .card table tbody');
      const canGrade = ['teacher', 'department', 'superadmin'].includes(role);
      if (body) {
        if (result.isPdfOnly) {
          body.innerHTML = `<tr><td>۱</td><td>آزمون PDFمحور</td><td>فایل پاسخ به‌صورت PDF ارسال شده است.</td><td>—</td><td>${canGrade && !result.resultsPublished ? `<div class="flex gap-8 mt-8"><input class="input" type="number" min="0" max="100" step="0.01" value="${result.pdfGrade?.score ?? result.score ?? ''}" placeholder="نمره" data-pdf-grade-score style="max-width:110px" aria-label="نمره از ۱۰۰"><button class="btn btn-primary btn-sm" id="savePdfGrade">ثبت نمره</button></div><small class="text-soft">نمره کلی از ۱۰۰</small>` : (result.score == null ? 'در انتظار تصحیح استاد' : `${faNumber(result.score)} از ۱۰۰`)}</td></tr>`;
          if (canGrade && !result.resultsPublished) $('#savePdfGrade')?.addEventListener('click', async event => {
            const button=event.currentTarget;const rawScore=$('[data-pdf-grade-score]')?.value?.trim() ?? '';const score=Number(rawScore);
            if(rawScore===''||!Number.isFinite(score)||score<0||score>100)return notify('ابتدا نمره‌ای بین ۰ تا ۱۰۰ وارد کنید.','warn');
            button.disabled=true;
            try{
              const payload={score};
              if(result.pdfGrade?.rowVersion) payload.rowVersion=result.pdfGrade.rowVersion;
              const route=role==='teacher'
                ? `/canonical/teacher/essay-grading/attempts/${encodeURIComponent(attemptId)}/pdf-grade`
                : `/canonical/results/${encodeURIComponent(attemptId)}/pdf-grade`;
              await request(route,{method:'PUT',body:JSON.stringify(payload)});
              notify('نمره کلی ثبت شد.','ok');await loadResults();
            }
            catch(error){notify(error.message||'ثبت نمره ناموفق بود.','danger');button.disabled=false;}
          });
        } else body.innerHTML = result.answers.length ? result.answers.map((answer, index) => {
          const isEssay = ['Essay', 'Text'].includes(String(answer.questionType || '').trim());
          const canEditGrade = canGrade && isEssay && !result.resultsPublished;
          const questionTitle = escapeHtml(answer.question || `سؤال ${answer.questionId}`);
          if (canGrade && !isEssay) {
            const autoScoreText = answer.isAutoGraded
              ? `تصحیح خودکار سیستم: ${faNumber(answer.awardedScore ?? 0)} از ${faNumber(answer.maxScore || 0)}`
              : 'در انتظار پردازش خودکار';
            return `<tr><td>${faNumber(index + 1)}</td><td>${questionTitle}</td><td colspan="2" class="text-soft">پاسخ تستی توسط سیستم بررسی شده؛ پاسخ دانشجو برای تصحیح دستی نمایش داده نمی‌شود.</td><td>${autoScoreText}</td></tr>`;
          }
          const gradeControl = canEditGrade
            ? `<div class="flex gap-8 mt-8"><input class="input" type="number" min="0" max="${Number(answer.maxScore || 0)}" step="0.01" value="${Number(answer.awardedScore || 0)}" data-grade-score style="max-width:100px" aria-label="نمره خام سؤال"><button class="btn btn-primary btn-sm" data-grade-answer="${answer.examQuestionId}">ثبت نمره</button></div><small class="text-soft">حداکثر نمره خام: ${faNumber(answer.maxScore || 0)}</small>`
            : (isEssay ? (answer.isManuallyGraded ? 'تصحیح شده' : 'در انتظار تصحیح تشریحی') : (answer.isAutoGraded ? 'تصحیح خودکار شده' : '—'));
          return `<tr>
            <td>${faNumber(index + 1)}</td><td>${questionTitle}</td>
            <td>${escapeHtml(typeof answer.answer === 'string' ? answer.answer : answer.answer?.text ?? JSON.stringify(answer.answer ?? 'بدون پاسخ'))}</td><td>${escapeHtml(Array.isArray(answer.correctAnswer) ? answer.correctAnswer.map(x => x.optionText || `${x.leftItem || ''}: ${x.rightItem || ''}`).join('، ') : answer.correctAnswer || '—')}</td>
            <td>${gradeControl}</td></tr>`;
        }).join('') : '<tr><td colspan="5">پاسخی ثبت نشده است.</td></tr>';

        if (canGrade) body.querySelectorAll('[data-grade-answer]').forEach(button => button.addEventListener('click', async () => {
          const row = button.closest('tr');
          const score = Number(row?.querySelector('[data-grade-score]')?.value);
          const comment = row?.querySelector('[data-grade-comment]')?.value || null;
          if (!Number.isFinite(score) || score < 0) return notify('نمره واردشده معتبر نیست.', 'warn');
          button.disabled = true;
          try {
            await request(`/canonical/results/${encodeURIComponent(attemptId)}/answers/${encodeURIComponent(button.dataset.gradeAnswer)}/grade`, { method: 'PUT', body: JSON.stringify({ score }) });
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
          pdfBox.innerHTML = `<div class="section-title"><h3>${result.isPdfOnly ? 'فایل پاسخ آزمون PDFمحور' : 'پاسخ‌نامه تشریحی دانشجو'}</h3></div><p class="text-soft text-sm">${escapeHtml(info.fileName)} — ${faNumber(Math.ceil(info.size / 1024))} کیلوبایت</p><iframe id="submissionPdfFrame" title="پاسخ‌نامه PDF دانشجو" style="width:100%;height:620px;border:1px solid var(--border);border-radius:12px;background:#fff" loading="lazy"></iframe><div class="flex gap-8 mt-12"><button type="button" class="btn btn-outline" id="previewSubmissionPdf">بازکردن در زبانه جدید</button> <button type="button" class="btn btn-outline" id="downloadSubmissionPdf">دریافت فایل</button></div>`;
          const submissionUrl = await window.MftApi.objectUrl(`/exam-files/attempts/${encodeURIComponent(attemptId)}`);
          $('#submissionPdfFrame').src = submissionUrl;
          $('#previewSubmissionPdf')?.addEventListener('click', () => window.MftApi.preview(`/exam-files/attempts/${encodeURIComponent(attemptId)}`).catch(error => notify(error.message, 'danger')));
          $('#downloadSubmissionPdf')?.addEventListener('click', () => window.MftApi.download(`/exam-files/attempts/${encodeURIComponent(attemptId)}`).catch(error => notify(error.message, 'danger')));
        } catch {
          if (result.examPdfAvailable) {
            pdfBox.hidden = false;
            pdfBox.innerHTML = '<div class="section-title"><h3>پاسخ‌نامه PDF دانشجو</h3></div><p class="text-soft">دانشجو هنوز فایل PDF پاسخ‌نامه را بارگذاری نکرده است.</p>';
          } else { pdfBox.hidden = true; pdfBox.innerHTML = ''; }
        }
      } else if (pdfBox) { pdfBox.hidden = true; pdfBox.innerHTML = ''; }

      const essayGradingBox = $('#essayGradingBox');
      if (essayGradingBox && role === 'teacher' && !result.isPdfOnly) {
        essayGradingBox.hidden = true;
        essayGradingBox.innerHTML = '';
        try {
          const essaySections = await request('/canonical/teacher/essay-grading/attempts/' +
            encodeURIComponent(attemptId) + '/sections');
          if (essaySections.length) {
            essayGradingBox.hidden = false;
            essayGradingBox.innerHTML = '<div class="section-title"><h3>پاسخ‌های PDF تشریحی — تصحیح استاد</h3></div><p class="text-soft text-sm mb-16">پاسخ‌های تستی این آزمون به‌صورت خودکار تصحیح شده‌اند. برای هر بخش تشریحی، PDF دانشجو را بررسی و نمره را ثبت کنید.</p>';
            for (const section of essaySections) {
              const article = document.createElement('section');
              article.className = 'card card-pad mt-16';
              const heading = document.createElement('h4');
              heading.textContent = 'بخش تشریحی ' + faNumber(section.displayOrder || 1);
              article.append(heading);
              const paper = document.createElement('p');
              paper.className = 'text-soft text-sm mt-8';
              paper.textContent = 'صورت سؤال: ' + (section.questionPaperFileName || 'ثبت نشده');
              article.append(paper);
              if (section.answerFileName) {
                const answerLabel = document.createElement('p');
                answerLabel.className = 'text-soft text-sm mt-8';
                answerLabel.textContent = 'پاسخ دانشجو: ' + section.answerFileName;
                article.append(answerLabel);
                const frame = document.createElement('iframe');
                frame.title = 'PDF پاسخ تشریحی دانشجو';
                frame.style.cssText = 'width:100%;height:560px;border:1px solid var(--border);border-radius:10px;background:#fff;margin-top:10px';
                const answerRoute = '/canonical/teacher/essay-grading/attempts/' + encodeURIComponent(attemptId) + '/sections/' + encodeURIComponent(section.id) + '/answer.pdf';
                frame.src = await window.MftApi.objectUrl(answerRoute);
                article.append(frame);
                const download = document.createElement('a');
                download.className = 'btn btn-outline btn-sm mt-8';
                download.href = frame.src;
                download.target = '_blank';
                download.rel = 'noopener';
                download.textContent = 'بازکردن PDF پاسخ در زبانه جدید';
                article.append(download);
              } else {
                const missing = document.createElement('p');
                missing.className = 'alert alert-warn mt-12';
                missing.textContent = 'دانشجو برای این بخش PDF پاسخ بارگذاری نکرده است.';
                article.append(missing);
              }
              const maxScore = Number(section.totalPoints || 100);
              const grade = section.grade || null;
              const controls = document.createElement('div');
              controls.className = 'flex gap-8 mt-16';
              controls.style.flexWrap = 'wrap';
              const scoreInput = document.createElement('input');
              scoreInput.className = 'input';
              scoreInput.type = 'number';
              scoreInput.min = '0';
              scoreInput.max = String(maxScore);
              scoreInput.step = '0.01';
              scoreInput.placeholder = 'نمره از ' + maxScore;
              scoreInput.value = grade?.score == null ? '' : String(grade.score);
              scoreInput.style.maxWidth = '150px';
              scoreInput.setAttribute('aria-label', 'نمره بخش تشریحی');
              const feedbackInput = document.createElement('textarea');
              feedbackInput.className = 'input';
              feedbackInput.rows = 2;
              feedbackInput.maxLength = 2000;
              feedbackInput.placeholder = 'بازخورد استاد (اختیاری)';
              feedbackInput.value = grade?.feedback || '';
              feedbackInput.style.flex = '1 1 260px';
              const saveGrade = document.createElement('button');
              saveGrade.type = 'button';
              saveGrade.className = 'btn btn-primary';
              saveGrade.textContent = grade ? 'به‌روزرسانی نمره' : 'ثبت نمره بخش';
              saveGrade.addEventListener('click', async () => {
                const raw = scoreInput.value.trim();
                const score = Number(raw);
                if (!raw || !Number.isFinite(score) || score < 0 || score > maxScore) {
                  notify('نمره باید بین صفر و ' + maxScore + ' باشد.', 'warn');
                  return;
                }
                saveGrade.disabled = true;
                try {
                  const payload = { score, feedback: feedbackInput.value.trim() || null };
                  if (grade?.rowVersion) payload.rowVersion = grade.rowVersion;
                  await request('/canonical/teacher/essay-grading/attempts/' + encodeURIComponent(attemptId) +
                    '/sections/' + encodeURIComponent(section.id) + '/grade', {
                      method: 'PUT', body: JSON.stringify(payload)
                    });
                  notify('نمره بخش تشریحی ثبت شد.', 'ok');
                  await loadResults();
                } catch (error) {
                  notify(error.message || 'ثبت نمره بخش ناموفق بود.', 'danger');
                  saveGrade.disabled = false;
                }
              });
              controls.append(scoreInput, feedbackInput, saveGrade);
              article.append(controls);
              const status = document.createElement('p');
              status.className = 'text-soft text-sm mt-8';
              status.textContent = grade
                ? 'آخرین تصحیح: ' + new Date(grade.gradedAt).toLocaleString('fa-IR') + ' — نمره ' + faNumber(grade.score) + ' از ' + faNumber(maxScore)
                : 'این بخش هنوز نمره‌گذاری نشده است.';
              article.append(status);
              essayGradingBox.append(article);
            }
          }
        } catch (error) {
          essayGradingBox.hidden = false;
          essayGradingBox.innerHTML = '<p class="text-soft">دریافت صف تصحیح تشریحی ناموفق بود: ' + escapeHtml(error.message || 'خطای نامشخص') + '</p>';
        }
      } else if (essayGradingBox) {
        essayGradingBox.hidden = true;
        essayGradingBox.innerHTML = '';
      }

      const publishButton = $('#publishExamResults');
      if (publishButton) {
        publishButton.hidden = !canGrade || result.resultsPublished === true;
        publishButton.onclick = async () => {
          publishButton.disabled = true;
          try {
            await request(`/canonical/results/exams/${encodeURIComponent(result.examId)}/publish`, { method: 'POST' });
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
            await request(`/canonical/results/exams/${encodeURIComponent(result.examId)}/unpublish`, { method: 'POST' });
            notify('انتشار نتایج لغو شد؛ اکنون می‌توانید نمرات را اصلاح کنید.', 'ok');
            await loadResults();
          } catch (error) { notify(error.message || 'لغو انتشار ناموفق بود.', 'danger'); }
          finally { unpublishButton.disabled = false; }
        };
      }
      return;
    }
    const rows = await request(`/canonical/results${examId ? `?examId=${encodeURIComponent(examId)}` : ''}`);
    const header = $('body[data-active="results"] table thead tr');
    if (header) header.innerHTML = '<th>#</th><th>آزمون</th><th>دانشجو</th><th>وضعیت</th><th>نمره</th>';
    renderRows('body[data-active="results"] table tbody', rows, row => `<tr>
      <td><a href="results.html?attemptId=${encodeURIComponent(row.id)}">${escapeHtml(String(row.id).slice(0, 8))}</a></td><td>${escapeHtml(row.examTitle)}</td><td>${escapeHtml(row.studentName || '—')}<div class="text-soft text-sm">شماره دانشجویی: ${escapeHtml(row.studentNumber || 'ثبت نشده')}</div></td>
      <td>${row.status === 'InProgress' ? 'در حال برگزاری' : row.isPdfOnly && row.status !== 'FullyGraded' ? 'پاسخنامه PDF در انتظار تصحیح استاد' : row.status === 'Submitted' ? 'تصحیح خودکار انجام شد' : row.status === 'PartiallyGraded' ? (row.hasAutoGradedAnswers ? 'نمره تستی آماده — تشریحی در انتظار تصحیح' : 'در انتظار تصحیح تشریحی') : row.status === 'FullyGraded' ? 'تصحیح کامل' : 'در انتظار بررسی'}</td>
      <td>${row.score == null ? (row.resultsPublished ? 'در انتظار تصحیح' : 'منتشر نشده') : faNumber(row.score)}</td></tr>`, 5);

    const publishButton = $('#publishExamResults');
    if (publishButton) {
      publishButton.hidden = !examId || role === 'student' || rows.some(row => row.resultsPublished);
      publishButton.onclick = async () => {
        if (!examId || publishButton.disabled) return;
        publishButton.disabled = true;
        try {
          await request(`/canonical/results/exams/${encodeURIComponent(examId)}/publish`, { method: 'POST' });
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
          await request(`/canonical/results/exams/${encodeURIComponent(examId)}/unpublish`, { method: 'POST' });
          notify('انتشار نتایج لغو شد؛ می‌توانید نمرات را اصلاح کنید.', 'ok');
          await loadResults();
        } catch (error) { notify(error.message || 'لغو انتشار ناموفق بود.', 'danger'); }
        finally { unpublishButton.disabled = false; }
      };
    }
  }

  async function loadTeacherGrades() {
    if (!document.body.matches('[data-active="dashboard"]') || (currentUser()?.role || '').toLowerCase() !== 'teacher') return;
    const list = $('#teacherPendingPlaceholder'), count = $('#mftPendingGradeCount'), upcoming = $('#mftUpcomingExams');
    const attemptsTask = request('/canonical/results').then(attempts => {
      const pending = (Array.isArray(attempts) ? attempts : []).filter(x => ['Submitted','PartiallyGraded'].includes(x.status));
      if (list) list.innerHTML = pending.length ? pending.map(x => '<li class="flex items-center justify-between" style="padding:10px 0;border-bottom:1px solid var(--border)"><div><b>' + escapeHtml(x.studentName || 'دانشجو') + '</b><div class="text-soft text-sm">شماره دانشجویی: ' + escapeHtml(x.studentNumber || 'ثبت نشده') + '</div><div class="text-soft text-sm">' + escapeHtml(x.examTitle) + ' — ارسال ' + (x.submittedAt ? new Date(x.submittedAt).toLocaleString('fa-IR') : 'بدون تاریخ') + '</div><div class="text-soft text-sm">' + (x.isPdfOnly ? 'پاسخنامه PDF — در انتظار تصحیح استاد' : (x.score == null ? 'نمره تستی: هنوز ثبت نشده' : 'نمره خودکار تا این مرحله: ' + faNumber(x.score)) + (x.status === 'PartiallyGraded' ? ' — پاسخ تشریحی در انتظار تصحیح' : '')) + '</div></div><a class="btn btn-outline btn-sm" href="results.html?attemptId=' + encodeURIComponent(x.id) + '">تصحیح</a></li>').join('') : '<li class="text-soft">برگه‌ای در انتظار تصحیح نیست.</li>';
      if (count) count.textContent = faNumber(pending.length) + ' مورد باقی‌مانده';
    }).catch(error => {
      if (list) list.innerHTML = '<li class="text-soft">دریافت صف تصحیح ناموفق بود: ' + escapeHtml(error.message || 'خطای نامشخص') + '</li>';
      if (count) count.textContent = 'ناموفق';
    });
    const examsTask = request('/canonical/exams').then(exams => {
      const now = Date.now();
      const scheduled = (Array.isArray(exams) ? exams : []).filter(x => x.startAt && (parseUtcDate(x.startAt)?.getTime() ?? 0) >= now && (!x.endAt || (parseUtcDate(x.endAt)?.getTime() ?? 0) >= now)).sort((a,b) => (parseUtcDate(a.startAt)?.getTime() ?? 0) - (parseUtcDate(b.startAt)?.getTime() ?? 0));
      if (upcoming) upcoming.innerHTML = scheduled.length ? scheduled.map(x => '<li class="flex items-center gap-12"><div class="mohr sm"><div class="mohr-inner"><b>' + escapeHtml(String(x.title || '').slice(0,2)) + '</b></div></div><div><b style="font-size:13px">' + escapeHtml(x.title || 'آزمون بدون عنوان') + '</b><div class="text-soft text-sm">' + (parseUtcDate(x.startAt)?.toLocaleString('fa-IR') || '—') + ' — ' + faNumber(x.durationMinutes) + ' دقیقه</div><small class="text-soft">' + (x.isPublished ? 'منتشرشده' : 'پیش‌نویس') + '</small></div></li>').join('') : '<li class="text-soft">آزمون زمان‌بندی‌شده آینده‌ای وجود ندارد.</li>';
    }).catch(error => {
      if (upcoming) upcoming.innerHTML = '<li class="text-soft">دریافت آزمون‌های زمان‌بندی‌شده ناموفق بود: ' + escapeHtml(error.message || 'خطای نامشخص') + '</li>';
    });
    await Promise.allSettled([attemptsTask, examsTask]);
  }

  async function loadStudentPerformance() {
    if (!document.body.matches('[data-active="dashboard"]') || (currentUser()?.role || '').toLowerCase() !== 'student') return;
    const attempts = await request('/canonical/results');
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
      const exams = await request('/canonical/exams');
      renderRows('#departmentExamsBody', exams, exam => `<tr><td>${escapeHtml(exam.title)}</td><td>—</td><td>${exam.startAt ? new Date(exam.startAt).toLocaleDateString('fa-IR') : '—'}</td><td>—</td><td>${exam.isPublished ? 'منتشرشده' : 'پیش‌نویس'}</td><td><a href="results.html?examId=${exam.id}" class="btn btn-ghost btn-sm">نتایج</a></td></tr>`, 6);
    }
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
    const panels = { tAll: rows, tUnread: rows.filter(row => !row.isRead), tRead: rows.filter(row => row.isRead) };
    const empty = { tAll: 'اعلانی وجود ندارد.', tUnread: 'اعلان خوانده‌نشده‌ای وجود ندارد.', tRead: 'اعلان خوانده‌شده‌ای وجود ندارد.' };
    const render = row => `<article class="notification-item ${row.isRead ? '' : 'unread'}" data-notification-id="${row.id}">
      <div><b>${escapeHtml(row.title)}</b><p>${escapeHtml(row.message)}</p><small>${new Date(row.createdAt).toLocaleString('fa-IR')}</small></div>
      <div class="flex gap-8"><button class="btn btn-sm" data-notification-read="${row.id}" ${row.isRead ? 'disabled' : ''}>خوانده شد</button>
      <button class="btn btn-sm btn-danger" data-notification-delete="${row.id}">حذف</button></div></article>`;
    Object.entries(panels).forEach(([id, items]) => {
      const panel = document.getElementById(id);
      if (!panel) return;
      // Replace the complete panel content so an empty-state from the previous tab
      // can never remain beside or above the new tab's empty-state.
      panel.replaceChildren();
      if (items.length) panel.innerHTML = items.map(render).join('');
      else {
        const state = document.createElement('div');
        state.className = 'empty-state';
        state.textContent = empty[id];
        panel.appendChild(state);
      }
    });
    const tabs = $$('[data-tabs] .tab-btn');
    if (tabs.length >= 3) {
      tabs[0].textContent = `همه (${faNumber(rows.length)})`;
      tabs[1].textContent = `خوانده‌نشده (${faNumber(panels.tUnread.length)})`;
      tabs[2].textContent = `خوانده‌شده (${faNumber(panels.tRead.length)})`;
    }
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
      if (!mobile?.value.trim()) return notify('شماره تماس الزامی است.', 'warn');
      safe(() => request('/users/me/profile', { method: 'PUT', body: JSON.stringify({ fullName: name?.value.trim(), email: email?.value.trim() || null, mobile: mobile.value.trim() }) }), 'اطلاعات پروفایل ذخیره شد.');
    }, { once: true });


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
    if (!panel || (currentUser()?.role || '').toLowerCase() !== 'superadmin') return;
    panel.hidden = false;
    const [departments, users, classes] = await Promise.all([
      request('/departments'), request('/users'), request('/classes')
    ]);
    const departmentSelect = $('[data-notification-department]');
    const userSelect = $('[data-notification-user]');
    const classSelect = $('[data-notification-class]');
    const classDepartment = $('[data-class-department]');
    const rosterWrap = $('[data-class-roster-wrap]');
    const rosterSelect = $('[data-class-roster-members]');
    const fillDepartments = select => {
      if (select) select.innerHTML = '<option value="">انتخاب دپارتمان</option>' +
        departments.map(x => `<option value="${x.id}">${escapeHtml(x.name)}</option>`).join('');
    };
    fillDepartments(departmentSelect);
    fillDepartments(classDepartment);
    if (userSelect) userSelect.innerHTML = '<option value="">انتخاب کاربر</option>' +
      users.filter(x => x.isActive !== false).map(x =>
        `<option value="${x.id}">${escapeHtml(x.fullName || x.username)} — ${escapeHtml(x.role)}</option>`).join('');
    const renderClasses = selectedId => {
      if (!classSelect) return;
      classSelect.innerHTML = '<option value="">انتخاب کلاس</option>' + classes.map(x =>
        `<option value="${x.id}">${escapeHtml(x.name)} — ${escapeHtml(x.department || '')} (${faNumber(x.memberCount)} نفر)</option>`).join('');
      if (selectedId) classSelect.value = selectedId;
    };
    renderClasses();
    const audience = $('[data-notification-audience]');
    const sync = () => {
      const value = audience?.value;
      $('[data-notification-department-wrap]').hidden = value !== 'Department';
      $('[data-notification-user-wrap]').hidden = value !== 'User';
      $('[data-notification-class-wrap]').hidden = value !== 'Class';
      $('[data-class-create-wrap]').hidden = value !== 'Class';
      if (value !== 'Class' && rosterWrap) rosterWrap.hidden = true;
    };
    audience?.addEventListener('change', sync);
    sync();

    const studentsForDepartment = departmentId => users.filter(x =>
      x.role === 'Student' && x.isActive !== false && String(x.departmentId) === String(departmentId));
    classDepartment?.addEventListener('change', () => {
      const members = $('[data-class-members]');
      if (members) members.innerHTML = studentsForDepartment(classDepartment.value).map(x =>
        `<option value="${x.id}">${escapeHtml(x.fullName || x.username)}</option>`).join('');
    });

    classSelect?.addEventListener('change', () => safe(async () => {
      const classId = classSelect.value;
      if (!classId) {
        if (rosterWrap) rosterWrap.hidden = true;
        return;
      }
      const info = await request(`/classes/${classId}/members`);
      const students = studentsForDepartment(info.departmentId);
      const selected = new Set((info.members || []).map(x => String(x.id)));
      if (rosterSelect) rosterSelect.innerHTML = students.map(x =>
        `<option value="${x.id}" ${selected.has(String(x.id)) ? 'selected' : ''}>${escapeHtml(x.fullName || x.username)}</option>`).join('');
      const summary = $('[data-class-summary]');
      if (summary) summary.textContent = `${info.name}: ${faNumber(selected.size)} عضو فعلی`;
      if (rosterWrap) rosterWrap.hidden = false;
    }));

    $('[data-class-roster-save]')?.addEventListener('click', () => safe(async () => {
      const classId = classSelect?.value;
      if (!classId) throw new Error('ابتدا کلاس را انتخاب کنید.');
      const studentIds = Array.from(rosterSelect?.selectedOptions || []).map(x => x.value);
      const result = await request(`/classes/${classId}/members`, {
        method: 'PUT', body: JSON.stringify({ studentIds })
      });
      const selectedClass = classes.find(x => String(x.id) === String(classId));
      if (selectedClass) selectedClass.memberCount = result.memberCount;
      renderClasses(classId);
      const summary = $('[data-class-summary]');
      if (summary) summary.textContent = `${selectedClass?.name || 'کلاس'}: ${faNumber(result.memberCount)} عضو`;
      notify('اعضای کلاس به‌روزرسانی شدند.', 'ok');
    }));

    $('[data-class-create]')?.addEventListener('click', () => safe(async () => {
      const name = $('[data-class-name]')?.value.trim();
      const departmentId = classDepartment?.value;
      const studentIds = Array.from($$('[data-class-members] option:checked')).map(x => x.value);
      if (!name || !departmentId) throw new Error('نام و دپارتمان کلاس را وارد کنید.');
      const created = await request('/classes', {
        method: 'POST', body: JSON.stringify({ name, departmentId, studentIds })
      });
      classes.push({ id: created.id, name: created.name, departmentId, memberCount: created.memberCount });
      renderClasses(created.id);
      if ($('[data-class-name]')) $('[data-class-name]').value = '';
      notify('کلاس و اعضای آن ثبت شدند.', 'ok');
    }));
  }

  async function sendNotification() {
    const audience = $('[data-notification-audience]')?.value;
    const title = $('[data-notification-title]')?.value.trim();
    const message = $('[data-notification-message]')?.value.trim();
    if (!title || !message) return notify('عنوان و متن اعلان را وارد کنید.', 'warn');
    const payload = { audience, title, message };
    if (audience === 'Department') payload.departmentId = $('[data-notification-department]')?.value;
    if (audience === 'User') payload.userId = $('[data-notification-user]')?.value;
    if (audience === 'Class') payload.classId = $('[data-notification-class]')?.value;
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
    if (departmentField) departmentField.value = existing?.departmentId == null ? '' : String(existing.departmentId);
    const activeField = $('[data-user-active]', modal);
    if (activeField) activeField.checked = existing ? Boolean(existing.isActive) : true;
    const usernameField = fields.find(field => field.hasAttribute('data-username-field'));
    if (usernameField) { usernameField.value = existing?.username || ''; usernameField.readOnly = Boolean(id); }
    const passwordField = fields.find(field => field.type === 'password');
    if (passwordField) {
      passwordField.value = '';
      const passwordWrapper = passwordField.closest('.field');
      if (passwordWrapper) passwordWrapper.hidden = Boolean(id);
    }
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
      const departmentIdValue = departmentField?.value || '';
      const departmentId = departmentIdValue ? Number(departmentIdValue) : null;
      const isActive = $('[data-user-active]', modal)?.checked ?? true;
      if (id) await request(`/users/${id}`, { method: 'PUT', body: JSON.stringify({ fullName, email, mobile, departmentId, isActive }) });
      else {
        if (!password || password.length < 8) return notify('رمز عبور باید حداقل ۸ کاراکتر باشد.', 'warn');
        await request('/users', { method: 'POST', body: JSON.stringify({ username, password, fullName, role, email, mobile, departmentId }) });
      }
      modal.classList.remove('show');
      await loadUsers();
    }, id ? 'اطلاعات کاربر ویرایش شد.' : 'کاربر ساخته شد.');
  }

  async function loadExamWizard() {
    if (!document.body.matches('[data-active="exams"]') || !$('#wizardSteps')) return;
    const [departments, lookups, questions, me, classes] = await Promise.all([
      request('/departments').catch(() => []),
      request('/questions/lookups'),
      request('/questions'),
      request('/auth/me').catch(() => null),
      request('/canonical/classes').catch(() => [])
    ]);
    const departmentWrap = $('#deptChoices');
    if (departmentWrap) {
      const role = String(me?.role || currentUser()?.role || '').toLowerCase();
      const visibleDepartments = role === 'superadmin' ? departments.filter(x => x.isActive !== false) : departments.filter(x => Number(x.id) === Number(me?.departmentId));
      departmentWrap.innerHTML = visibleDepartments.length
        ? visibleDepartments.map((dept, index) => `<div class="choice-card ${index === 0 ? 'selected' : ''}" data-id="${dept.id}" data-value="${escapeHtml(dept.name)}"><div class="mohr sm"><div class="mohr-inner"><b>${escapeHtml(dept.name.slice(0, 2))}</b></div></div><b>${escapeHtml(dept.name)}</b></div>`).join('')
        : '<p class="text-soft">دپارتمان متصل به حساب شما پیدا نشد؛ ابتدا اتصال حساب به دپارتمان را بررسی کنید.</p>';
    }
    const classSelect = $('#examClass');
    if (classSelect) {
      classSelect.innerHTML = '<option value="">انتخاب کلاس آزمون</option>' +
        classes.map(item => `<option value="${item.id}" data-department-id="${item.departmentId}">${escapeHtml(item.name)} — ${escapeHtml(item.department || '')} (${Number(item.memberCount || 0)} نفر)</option>`).join('');
      if (!classes.length) classSelect.innerHTML = '<option value="">کلاسی ثبت نشده است؛ ابتدا کلاس ایجاد کنید</option>';
      classSelect.onchange = () => {
        const selected = classSelect.selectedOptions[0];
        const deptId = Number(selected?.dataset.departmentId || 0);
        const departmentCard = $('#deptChoices .choice-card').find(card => Number(card.dataset.id) === deptId);
        if (departmentCard) {
          $$('#deptChoices .choice-card').forEach(card => card.classList.toggle('selected', card === departmentCard));
          const preview = $('#prevDept');
          if (preview) preview.textContent = departmentCard.dataset.value || departmentCard.textContent.trim();
        }
      };
    }
    const createClassButton = $('[data-create-class]');
    if (createClassButton && !createClassButton.dataset.bound) {
      createClassButton.dataset.bound = 'true';
      createClassButton.addEventListener('click', async () => {
        const departmentCard = $('#deptChoices .choice-card.selected');
        if (!departmentCard?.dataset.id) return notify('ابتدا دپارتمان مقصد را انتخاب کنید.', 'warn');
        const name = prompt('نام کلاس جدید را وارد کنید:');
        if (!name?.trim()) return;
        await safe(async () => {
          await request('/canonical/classes', {
            method: 'POST',
            body: JSON.stringify({ name: name.trim(), departmentId: Number(departmentCard.dataset.id) })
          });
          await window.MftApi.refresh();
          const select = $('#examClass');
          const created = [...(select?.options || [])].find(option =>
            option.textContent.startsWith(name.trim() + ' — ') &&
            Number(option.dataset.departmentId) === Number(departmentCard.dataset.id));
          if (created) select.value = created.value;
          notify('کلاس ایجاد شد. اکنون اعضای آن را در بخش مدیریت کلاس‌ها تعیین کنید.', 'ok');
        });
      });
    }
    const categorySelect = $('#examCategory');
    if (categorySelect) categorySelect.innerHTML = '<option value="">انتخاب دسته‌بندی آزمون</option>' +
      lookups.categories.map(x => `<option value="${x.id}">${escapeHtml(x.name)}</option>`).join('');
    const questionWrap = $('#manualPick');
    const renderQuestions = () => {
      const categoryId = Number(categorySelect?.value || 0);
      const skillIds = new Set(lookups.skills.filter(s => !categoryId || Number(s.examCategoryId) === categoryId).map(s => Number(s.id)));
      const available = questions.filter(q => skillIds.has(Number(q.skillId)));
      if (questionWrap) questionWrap.innerHTML = available.map(question => {
        const type = question.questionTypeCode || '';
        const typeName = ({ MultipleChoice: 'چهارگزینه‌ای', Essay: 'تشریحی', Matching: 'وصل‌کردنی' })[type] || question.questionTypeName || type;
        return `<label class="qpick-row"><input type="checkbox" data-question-type="${escapeHtml(type)}" data-skill-id="${question.skillId}" value="${question.id}"><span style="flex:1">${escapeHtml(question.text)}</span><span class="badge badge-info">${escapeHtml(question.skillName || 'مهارت')}</span><span class="badge badge-muted">${escapeHtml(typeName)}</span></label>`;
      }).join('') || '<p class="text-soft">برای این دسته‌بندی هنوز سؤالی در بانک canonical ثبت نشده است.</p>';
    };
    if (categorySelect) categorySelect.onchange = renderQuestions;
    renderQuestions();
    const teacherWrap = $('#teacherChoices');
    if (teacherWrap) {
      const name = me?.fullName || currentUser()?.fullName || 'کاربر واردشده';
      teacherWrap.innerHTML = `<div class="choice-card selected" data-id="${me?.id || ''}" data-value="${escapeHtml(name)}"><div class="mohr sm"><div class="mohr-inner"><b>${escapeHtml(name.slice(0, 2))}</b></div></div><b>${escapeHtml(name)} (حساب فعلی)</b></div>`;
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
      $$('[data-username-field]', modal).forEach(field => { field.readOnly = false; });
      $$('input[type="password"]', modal).forEach(field => {
        field.value = '';
        const wrapper = field.closest('.field');
        if (wrapper) wrapper.hidden = false;
      });
      $$('input[type="radio"]', modal).forEach((field, index) => { field.checked = index === 0; });
      $$('select', modal).forEach(select => { select.selectedIndex = 0; });
      const activeField = $('[data-user-active]', modal);
      if (activeField) activeField.checked = true;
      if ($('[data-question-difficulty]', modal)) $('[data-question-difficulty]', modal).value = 'Medium';
      if ($('#mcqOptions', modal)) $('#mcqOptions', modal).style.display = 'block';
      if ($('#matchingOptions', modal)) $('#matchingOptions', modal).hidden = true;
    });
    document.addEventListener('click', event => {
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
      const target = event.target.closest('[data-user-delete], [data-user-edit], [data-question-delete], [data-question-edit], [data-department-delete], [data-department-edit], [data-notification-delete], [data-notification-read], [data-exam-start], [data-exam-continue], [data-result-view]');
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
        } else if (target.dataset.examContinue) {
          location.href = `online-exam.html?examId=${encodeURIComponent(target.dataset.examContinue)}&attemptId=${encodeURIComponent(target.dataset.attemptId)}`;
        } else if (target.dataset.examStart) {
          target.disabled = true;
          try {
            const examId = target.dataset.examStart;
            const attempt = await request(`/canonical/exams/${encodeURIComponent(examId)}/attempt/start`, { method: 'POST' });
            location.href = `online-exam.html?examId=${encodeURIComponent(examId)}&attemptId=${encodeURIComponent(attempt.attemptId)}`;
          } catch (error) {
            target.disabled = false;
            if (/قبلاً تلاش|شرکت مجدد|تلاش ثبت شده/i.test(error.message)) {
              notify('برای این آزمون قبلاً تلاش ثبت شده است؛ امکان شرکت مجدد وجود ندارد.', 'warn');
              await loadExams();
              return;
            }
            throw error;
          }
        } else if (target.dataset.resultView) {
          const result = await request(`/canonical/results/${target.dataset.resultView}`);
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
        $$$('#deptChoices .choice-card').forEach(card => card.classList.toggle('selected', card === newCard));
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
      { name: 'password', label: 'رمز عبور (حداقل ۸ کاراکتر)', type: 'password' },
      { name: 'mobile', label: 'شماره تماس (الزامی)' }
    ]);
    if (!values) return;
    const created = await request('/users', {
      method: 'POST',
      body: JSON.stringify({ username: values.username, password: values.password, fullName: values.fullName, role: 'Department', email: null, mobile: values.mobile, departmentId: null })
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
      { name: 'password', label: 'رمز عبور (حداقل ۸ کاراکتر)', type: 'password' },
      { name: 'mobile', label: 'شماره تماس (الزامی)' }
    ]);
    if (!values) return;
    const { fullName, username } = values;
    const department = $('#deptChoices .choice-card.selected');
    const departmentId = department?.dataset.id ? Number(department.dataset.id) : null;
    if (role === 'superadmin' && !departmentId) return notify('ابتدا دپارتمان مقصد را انتخاب یا ایجاد کنید.', 'warn');
    const created = await request('/users', {
      method: 'POST',
      body: JSON.stringify({ username, password: values.password, fullName, role: 'Teacher', email: null, mobile: values.mobile, departmentId })
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
    const [question, lookups] = await Promise.all([
      request(`/questions/${encodeURIComponent(id)}`),
      request('/questions/lookups')
    ]);
    const modal = $('#questionModal');
    if (!modal) return;
    modal.dataset.editId = String(id);
    $('[data-question-text]', modal).value = question.text || '';
    $('#qType', modal).value = lookups.questionTypes.find(x => Number(x.id) === Number(question.questionTypeId))?.code || 'MultipleChoice';
    $('[data-question-difficulty]', modal).value = ({1:'Easy',2:'Medium',3:'Hard'})[Number(question.difficultyLevel)] || 'Medium';
    $('#questionSkill', modal).innerHTML = '<option value="">انتخاب مهارت</option>' +
      lookups.skills.map(x => `<option value="${x.id}">${escapeHtml(x.name)}</option>`).join('');
    $('#questionSkill', modal).value = String(question.skillId);
    $('#questionPoints', modal).value = question.points || 1;
    $$('[data-question-option]', modal).forEach((field, index) => field.value = question.options?.[index]?.optionText || '');
    $$('[data-match-left]', modal).forEach((field, index) => field.value = question.matchingPairs?.[index]?.leftItem || '');
    $$('[data-match-right]', modal).forEach((field, index) => field.value = question.matchingPairs?.[index]?.rightItem || '');
    $('#mcqOptions', modal).style.display = $('#qType', modal).value === 'MultipleChoice' ? 'block' : 'none';
    $('#matchingOptions', modal).hidden = $('#qType', modal).value !== 'Matching';
    const correctIndex = question.options?.findIndex(x => x.isCorrect) ?? -1;
    if ($('#correctOption', modal)) $('#correctOption', modal).value = correctIndex >= 0 ? String(correctIndex + 1) : '';
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
      if (document.body.dataset.active === 'students') attachSearch('main .table-toolbar .search-box input', loadUsers);
      if (document.body.dataset.active === 'teachers') attachSearch('main .table-toolbar .search-box input', loadUsers);
      if (document.body.dataset.active === 'departments') attachSearch('main .table-toolbar .search-box input', loadDepartments);
      if (document.body.dataset.active === 'bank') attachSearch('main .table-toolbar .search-box input', loadQuestions);
      attachGlobalSearch();
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

  async function objectUrl(path) {
    const headers = new Headers();
    if (token()) headers.set('Authorization', `Bearer ${token()}`);
    const response = await fetch(`${API_BASE}${path}`, { headers });
    if (!response.ok) {
      const payload = await response.json().catch(() => null);
      throw new Error(payload?.message || `خطای دریافت فایل (${response.status})`);
    }
    return URL.createObjectURL(await response.blob());
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
    objectUrl,
    logout() { clearSession(); location.href = 'login.html'; },
    refresh: init
  };
  document.addEventListener('DOMContentLoaded', () => { init().catch(error => console.error('Mft initialization failed:', error)); });
})();
