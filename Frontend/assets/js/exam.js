/* Server-driven online exam runtime. No demo questions or fabricated answers. */
(async function () {
  const api = window.MftApi;
  const timerEl = document.getElementById('examTimer');
  const progressEl = document.getElementById('examProgress');
  const progressLabel = document.getElementById('examProgressLabel');
  const navGrid = document.getElementById('examNavGrid');
  const qNumberEl = document.getElementById('examQNumber');
  const autosaveEl = document.getElementById('autosaveIndicator');
  const titleEl = document.querySelector('.exam-topbar b');
  const card = document.querySelector('.exam-layout > div:first-child .card');
  const questionTitle = card?.querySelector('h3');
  const optionContainer = questionTitle?.parentElement;
  const attemptKey = 'mft.activeAttempt';
  let attempt = null, questions = [], current = 0, remaining = 0, allowQuestionNavigation = true;
  let answers = new Map(), flagged = new Set(), saveTimer = null, saving = false, savePending = false, submitted = false, intervalId = null;

  const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const fa = value => Number(value || 0).toLocaleString('fa-IR');
  const fmt = seconds => `${String(Math.floor(Math.max(0, seconds) / 60)).padStart(2, '0')}:${String(Math.max(0, seconds) % 60).padStart(2, '0')}`;
  const toast = (message, type='info') => window.showToast?.(message, type);

  function markSaving(state, label) {
    if (!autosaveEl) return;
    autosaveEl.classList.toggle('saving', state);
    const span = autosaveEl.querySelector('span'); if (span) span.textContent = label;
  }
  function answeredCount() { return [...answers.values()].filter(v => String(v ?? '').trim() !== '').length; }
  function updateProgress() {
    const count = answeredCount(), total = questions.length;
    if (progressEl) progressEl.style.width = `${total ? Math.round(count / total * 100) : 0}%`;
    if (progressLabel) progressLabel.textContent = `${fa(count)} از ${fa(total)} پاسخ داده شده`;
    if (qNumberEl) qNumberEl.textContent = total ? `سوال ${fa(current + 1)} از ${fa(total)}` : 'سؤالی برای این آزمون ثبت نشده است.';
  }
  function buildNav() {
    if (!navGrid) return;
    navGrid.style.display = allowQuestionNavigation ? '' : 'none';
    if (!allowQuestionNavigation) { navGrid.replaceChildren(); return; }
    navGrid.replaceChildren();
    questions.forEach((q, i) => {
      const btn = document.createElement('button'); btn.type = 'button'; btn.textContent = fa(i + 1); btn.className = 'exam-nav-btn';
      if (i === current) btn.classList.add('current');
      if (String(answers.get(q.id) ?? '').trim() !== '') btn.classList.add('answered');
      if (flagged.has(i)) btn.classList.add('flagged');
      btn.addEventListener('click', () => renderQuestion(i)); navGrid.appendChild(btn);
    });
  }
  function queueSave() {
    if (submitted || !attempt?.attemptId) return;
    clearTimeout(saveTimer); markSaving(true, 'در انتظار ذخیره...');
    saveTimer = setTimeout(saveAnswers, 450);
  }
  async function saveAnswers() {
    if (submitted || !attempt?.attemptId) return;
    if (saving) { savePending = true; return; }
    saving = true; savePending = false; markSaving(true, 'در حال ذخیره...');
    try {
      await api.request(`/exams/attempts/${attempt.attemptId}/answers`, {method:'PUT', body:JSON.stringify({answers:questions.filter(q => answers.has(q.id)).map(q => ({questionId:q.id, answer:answers.get(q.id)}))})});
      markSaving(false, 'ذخیره شد');
    } catch (e) { markSaving(false, 'ذخیره ناموفق'); toast(e.message, 'danger'); }
    finally { saving = false; if (savePending && !submitted) saveAnswers(); }
  }
  function renderQuestion(index) {
    if (!questions.length) return;
    current = Math.min(Math.max(index, 0), questions.length - 1);
    const q = questions[current];
    if (questionTitle) questionTitle.textContent = q.text || 'بدون متن';
    const badge = card?.querySelector('.badge'); if (badge) badge.textContent = /matching/i.test(q.type || '') ? 'وصل‌کردنی' : /essay|text/i.test(q.type || '') ? 'تشریحی' : 'چهارگزینه‌ای';
    card?.querySelectorAll('label.option-row').forEach(el => el.remove());
    const options = Array.isArray(q.options) ? q.options : [];
    const oldTextarea = card?.querySelector('#examEssayAnswer'); oldTextarea?.remove();
    card?.querySelectorAll('[data-matching-row]').forEach(el => el.remove());
    const oldHint = card?.querySelector('[data-no-options]'); oldHint?.remove();
    const currentAnswer = answers.get(q.id) ?? '';
    if (q.type === 'Matching') {
      let selected = {};
      try { selected = JSON.parse(currentAnswer || '{}'); } catch { selected = {}; }
      const choices = Array.isArray(q.matchingOptions) ? q.matchingOptions : [];
      let matchingAnchor = questionTitle;
      options.forEach(left => {
        const row = document.createElement('div'); row.dataset.matchingRow = 'true'; row.className = 'field';
        const label = document.createElement('label'); label.textContent = left;
        const select = document.createElement('select'); select.className = 'select'; select.style.width = '100%';
        const empty = document.createElement('option'); empty.value = ''; empty.textContent = 'انتخاب مورد مرتبط'; select.append(empty);
        choices.forEach(right => { const option = document.createElement('option'); option.value = right; option.textContent = right; select.append(option); });
        select.value = selected[left] || '';
        select.addEventListener('change', () => { selected[left] = select.value; answers.set(q.id, JSON.stringify(selected)); updateProgress(); buildNav(); queueSave(); });
        row.append(label, select); matchingAnchor?.after(row); matchingAnchor = row;
      });
    } else if (options.length) {
      let anchor = questionTitle;
      options.forEach((option, idx) => {
        const label = document.createElement('label'); label.className = 'option-row';
        const radio = document.createElement('input'); radio.type = 'radio'; radio.name = 'examOption'; radio.value = String(option); radio.style.accentColor = 'var(--primary)'; radio.checked = currentAnswer === String(option);
        radio.addEventListener('change', () => { answers.set(q.id, radio.value); updateProgress(); buildNav(); queueSave(); });
        const letter = document.createElement('span'); letter.className = 'option-letter'; letter.textContent = ['الف','ب','ج','د','هـ','و'][idx] || fa(idx + 1);
        const text = document.createElement('span'); text.textContent = option;
        label.append(radio, letter, text); anchor?.after(label); anchor = label;
      });
    } else {
      const textarea = document.createElement('textarea'); textarea.id = 'examEssayAnswer'; textarea.className = 'input'; textarea.rows = 6; textarea.style.width = '100%'; textarea.placeholder = 'پاسخ خود را وارد کنید...'; textarea.value = currentAnswer;
      textarea.addEventListener('input', () => { answers.set(q.id, textarea.value); updateProgress(); buildNav(); queueSave(); });
      questionTitle?.after(textarea);
      const hint = document.createElement('p'); hint.dataset.noOptions = 'true'; hint.className = 'text-soft text-sm mt-8'; hint.textContent = 'این سؤال گزینه‌ای ندارد؛ پاسخ به‌صورت تشریحی ذخیره می‌شود.'; textarea.after(hint);
    }
    const flagBtn = document.getElementById('flagQuestion'); if (flagBtn) { flagBtn.classList.toggle('active', flagged.has(current)); flagBtn.setAttribute('aria-pressed', String(flagged.has(current))); }
    const prev = document.getElementById('examPrev'); if (prev) { prev.disabled = current === 0 || !allowQuestionNavigation; prev.style.display = allowQuestionNavigation ? '' : 'none'; }
    const next = document.getElementById('examNext'); if (next) next.textContent = current === questions.length - 1 ? 'آخرین سؤال' : 'سوال بعد ▶';
    updateProgress(); buildNav();
  }
  async function submitExam() {
    if (submitted || !attempt?.attemptId) return;
    submitted = true; clearTimeout(saveTimer); clearInterval(intervalId);
    const confirmBtn = document.getElementById('confirmFinishBtn'); if (confirmBtn) { confirmBtn.disabled = true; confirmBtn.textContent = 'در حال ثبت...'; }
    try {
      const result = await api.request(`/exams/attempts/${attempt.attemptId}/submit`, {method:'POST', body:JSON.stringify({answers:questions.filter(q => answers.has(q.id)).map(q => ({questionId:q.id, answer:answers.get(q.id)}))})});
      sessionStorage.setItem('mft.lastResult', JSON.stringify(result)); sessionStorage.removeItem(attemptKey);
      location.href = `results.html?attemptId=${encodeURIComponent(result.id)}`;
    } catch (e) { submitted = false; if (confirmBtn) { confirmBtn.disabled = false; confirmBtn.textContent = 'بله، پایان آزمون'; } toast(e.message || 'ثبت نهایی آزمون ناموفق بود.', 'danger'); }
  }
  function startTimer() {
    if (timerEl) timerEl.textContent = fmt(remaining);
    intervalId = setInterval(() => {
      if (submitted) return;
      remaining = Math.max(0, remaining - 1);
      if (timerEl) timerEl.textContent = fmt(remaining);
      if (remaining === 300) toast('۵ دقیقه تا پایان زمان آزمون باقی مانده است', 'warn');
      if (remaining === 60) toast('یک دقیقه تا پایان زمان آزمون!', 'danger');
      if (remaining <= 0) { clearInterval(intervalId); toast('زمان آزمون به پایان رسید؛ پاسخ‌ها در حال ثبت هستند.', 'danger'); submitExam(); }
    }, 1000);
  }

  document.getElementById('examNext')?.addEventListener('click', () => renderQuestion(current + 1));
  document.getElementById('examPrev')?.addEventListener('click', () => renderQuestion(current - 1));
  document.getElementById('flagQuestion')?.addEventListener('click', () => { if (flagged.has(current)) flagged.delete(current); else flagged.add(current); renderQuestion(current); });
  document.getElementById('finishExamBtn')?.addEventListener('click', () => {
    const unanswered = questions.length - answeredCount();
    const msg = document.getElementById('finishSummary');
    if (msg) msg.textContent = unanswered ? `${fa(unanswered)} سؤال بی‌پاسخ دارید. آیا مطمئن هستید که می‌خواهید آزمون را نهایی کنید؟` : 'همه سؤال‌ها پاسخ داده شده‌اند. آیا آزمون را نهایی می‌کنید؟';
    document.getElementById('finishModal')?.classList.add('show');
  });
  document.getElementById('confirmFinishBtn')?.addEventListener('click', submitExam);

  try {
    if (!api) throw new Error('لایه اتصال به سرویس در دسترس نیست.');
    const raw = sessionStorage.getItem(attemptKey);
    if (!raw) { toast('آزمون فعالی پیدا نشد؛ از صفحه آزمون‌ها دوباره شروع کنید.', 'warn'); location.href = 'dashboard-student.html'; return; }
    attempt = JSON.parse(raw);
    if (!attempt?.attemptId || !Array.isArray(attempt.questions)) throw new Error('اطلاعات آزمون معتبر نیست؛ آزمون را دوباره شروع کنید.');
    questions = attempt.questions;
    allowQuestionNavigation = attempt.exam?.allowQuestionNavigation !== false;
    if (!questions.length) throw new Error('برای این آزمون سؤالی ثبت نشده است.');
    (attempt.answers || []).forEach(a => { if (a.answer != null) answers.set(a.questionId, a.answer); });
    const duration = Math.max(1, Number(attempt.exam?.durationMinutes || 45) * 60);
    const startedAt = attempt.startedAt ? new Date(attempt.startedAt).getTime() : Date.now();
    remaining = Math.max(1, duration - Math.floor((Date.now() - startedAt) / 1000));
    if (titleEl) titleEl.textContent = attempt.exam?.title || 'آزمون آنلاین';
    const warning = card?.querySelector('.alert'); if (warning) warning.textContent = 'توجه: پس از ثبت نهایی یا پایان زمان آزمون، امکان تغییر پاسخ‌ها وجود ندارد.';
    renderQuestion(0); startTimer();
  } catch (e) { toast(e.message || 'بارگذاری آزمون ناموفق بود.', 'danger'); if (questionTitle) questionTitle.textContent = 'بارگذاری آزمون ناموفق بود'; }
})();
