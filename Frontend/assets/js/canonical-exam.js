/* Canonical online exam client: server-owned attempt, deadline and one-time submission. */
(async function () {
  'use strict';
  const api = window.MftApi;
  const params = new URLSearchParams(location.search);
  const examId = params.get('examId');
  const attemptId = params.get('attemptId');
  const root = document.querySelector('.exam-layout > div:first-child .card');
  const titleNode = document.querySelector('.exam-topbar b');
  const timerNode = document.getElementById('examTimer');
  const nav = document.getElementById('examNavGrid');
  const progress = document.getElementById('examProgress');
  const progressLabel = document.getElementById('examProgressLabel');
  const numberLabel = document.getElementById('examQNumber');
  const saveLabel = document.querySelector('#autosaveIndicator span');
  const fa = n => Number(n || 0).toLocaleString('fa-IR');
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const toast = (m,t='info') => window.showToast?.(m,t);
  let exam, sections=[], questions=[], answers=new Map(), current=0, deadline=0, remainingAtSyncMs=0, syncMonotonic=0, timer=null, saveTimer=null, saving=false, pending=false, finalizing=false, finished=false, uploadingPdf=false, uploadedPdf=false;

  function flatQuestions(data) {
    return (data.sections || []).flatMap(section => (section.questions || []).map(q => ({
      ...q, sectionId: section.id, type: q.questionType || '', points: q.points,
      options: q.options || [], leftItems: q.matchingLeftItems || [], rightItems: q.matchingRightItems || []
    })));
  }
  function valueFor(q) {
    if (answers.has(q.examQuestionId)) return answers.get(q.examQuestionId);
    if (/multiplechoice/i.test(q.type)) return { selectedOptionId: null };
    if (/matching/i.test(q.type)) return { pairs: q.leftItems.map(leftItem => ({ leftItem, rightItem: '' })) };
    if (/essay/i.test(q.type)) return {};
    return { text: '' };
  }
  function answerText(q) {
    const value=valueFor(q);
    if (/multiplechoice/i.test(q.type)) return value.selectedOptionId ? (q.options.find(o=>o.id===value.selectedOptionId)?.optionText || '—') : 'بدون پاسخ';
    if (/matching/i.test(q.type)) return (value.pairs || []).filter(p=>p.rightItem).length ? 'جفت‌ها انتخاب شده‌اند' : 'بدون پاسخ';
    if (/essay/i.test(q.type)) return uploadedPdf ? 'پاسخ در فایل PDF بارگذاری شده' : 'فایل PDF پاسخ بارگذاری نشده';
    return value.text || 'بدون پاسخ';
  }
  function answered(q) {
    const v=valueFor(q);
    if (/multiplechoice/i.test(q.type)) return !!v.selectedOptionId;
    if (/matching/i.test(q.type)) return (v.pairs || []).some(p=>p.rightItem);
    if (/essay/i.test(q.type)) return uploadedPdf;
    return !!String(v.text || '').trim() || (!!exam?.examPdfAvailable && uploadedPdf);
  }
  function updateProgress() {
    const count=questions.filter(answered).length;
    if(progress) progress.style.width=`${questions.length ? Math.round(count/questions.length*100):0}%`;
    if(progressLabel) progressLabel.textContent=`${fa(count)} از ${fa(questions.length)} پاسخ داده شده`;
    if(numberLabel) numberLabel.textContent=questions.length?`سؤال ${fa(current+1)} از ${fa(questions.length)}`:'';
    if(nav) [...nav.children].forEach((b,i)=>b.classList.toggle('answered',answered(questions[i])));
  }
  function render() {
    const q=questions[current]; if(!q || !root) return;
    root.querySelectorAll('[data-canonical-question], [data-canonical-answer]').forEach(n=>n.remove());
    const heading=root.querySelector('h3');
    if(heading) heading.textContent=q.text || 'بدون متن';
    const badge=root.querySelector('.badge'); if(badge) badge.textContent=/multiplechoice/i.test(q.type)?'چهارگزینه‌ای':/matching/i.test(q.type)?'وصل‌کردنی':'تشریحی';
    const v=valueFor(q);
    let anchor=heading;
    const add=(node)=>{node.dataset.canonicalAnswer='1';anchor.after(node);anchor=node;};
    if(/multiplechoice/i.test(q.type)) {
      q.options.forEach((option,index)=>{
        const label=document.createElement('label'); label.className='option-row';
        const radio=document.createElement('input'); radio.type='radio';radio.name='canonicalAnswer';radio.checked=Number(v.selectedOptionId)===Number(option.id);
        radio.addEventListener('change',()=>{answers.set(q.examQuestionId,{selectedOptionId:option.id});changed();});
        const letter=document.createElement('span');letter.className='option-letter';letter.textContent=['الف','ب','ج','د','هـ','و'][index]||fa(index+1);
        const text=document.createElement('span');text.textContent=option.optionText;
        label.append(radio,letter,text);add(label);
      });
    } else if(/matching/i.test(q.type)) {
      const selected=new Map((v.pairs||[]).map(p=>[p.leftItem,p.rightItem]));
      q.leftItems.forEach(left=>{
        const wrap=document.createElement('div');wrap.className='field';wrap.style.marginBottom='12px';
        const label=document.createElement('label');label.textContent=left;
        const select=document.createElement('select');select.className='input';select.style.width='100%';
        const empty=document.createElement('option');empty.value='';empty.textContent='انتخاب مورد مرتبط';select.append(empty);
        q.rightItems.forEach(right=>{const option=document.createElement('option');option.value=right;option.textContent=right;select.append(option);});
        select.value=selected.get(left)||'';
        select.addEventListener('change',()=>{
          selected.set(left,select.value);
          const pairs=q.leftItems.map(item=>({leftItem:item,rightItem:selected.get(item)||''}));
          answers.set(q.examQuestionId,{pairs});changed();
        });
        wrap.append(label,select);add(wrap);
      });
    } else if (/essay/i.test(q.type)) {
      const note=document.createElement('div');note.className='alert alert-info mt-16';
      note.textContent='پاسخ این سؤال باید فقط در قالب فایل PDF بارگذاری شود. امکان تایپ پاسخ یا انتخاب گزینه وجود ندارد.';
      add(note);
    } else {
      const note=document.createElement('div');note.className='alert alert-warn mt-16';note.textContent='نوع این سؤال در سامانه پشتیبانی نمی‌شود.';add(note);
    }
    const prev=document.getElementById('examPrev'),next=document.getElementById('examNext');
    if(prev) prev.disabled=current===0;
    if(next) next.textContent=current===questions.length-1?'آخرین سؤال':'سؤال بعد ▶';
    if(nav) [...nav.children].forEach((b,i)=>b.classList.toggle('current',i===current));
    updateProgress();
  }
  function renderNav() {
    if(!nav)return;nav.replaceChildren();
    questions.forEach((q,i)=>{const b=document.createElement('button');b.type='button';b.className='exam-nav-btn';b.textContent=fa(i+1);b.addEventListener('click',()=>{current=i;render();});nav.append(b);});
  }
  function requestPayload() {
    return questions.map(q=>({examQuestionId:q.examQuestionId,value:valueFor(q)}));
  }
  function changed() {
    updateProgress();
    if(saveLabel)saveLabel.textContent='ذخیره در صف...';
    clearTimeout(saveTimer);saveTimer=setTimeout(saveAnswers,500);
  }
  async function saveAnswers() {
    if(finished)return;
    if(saving){pending=true;return;}
    saving=true;pending=false;if(saveLabel)saveLabel.textContent='در حال ذخیره...';
    try{
      await api.request(`/canonical/exams/${encodeURIComponent(examId)}/attempt/${encodeURIComponent(attemptId)}/answers`,{method:'PUT',body:JSON.stringify({answers:requestPayload()})});
      if(saveLabel)saveLabel.textContent='ذخیره شد';
    }catch(error){if(saveLabel)saveLabel.textContent='ذخیره ناموفق';toast(error.message,'danger');}
    finally{saving=false;if(pending&&!finished)saveAnswers();}
  }
  function formatTime(seconds){const s=Math.max(0,seconds);return `${String(Math.floor(s/60)).padStart(2,'0')}:${String(s%60).padStart(2,'0')}`;}
  function startClock(){
    const tick=()=>{const elapsed=(window.performance?.now?.() ?? Date.now())-syncMonotonic;const left=Math.max(0,Math.floor((remainingAtSyncMs-elapsed)/1000));if(timerNode)timerNode.textContent=formatTime(left);
      if(left===300)toast('۵ دقیقه تا پایان آزمون باقی مانده است.','warn');
      if(left<=0){clearInterval(timer);toast('زمان آزمون تمام شد؛ پاسخ‌ها در حال ثبت نهایی هستند.','danger');submit(true);}
    };
    tick();timer=setInterval(tick,1000);
  }
  async function submit(auto=false){
    if(finished||finalizing)return;
    if(uploadingPdf){toast('تا پایان بارگذاری فایل پاسخ صبر کنید.','warn');return;}
    if((exam?.examPdfAvailable || questions.some(q=>/essay/i.test(q.type))) && !uploadedPdf && !auto){toast('برای پاسخ به سؤال تشریحی، ابتدا فایل PDF پاسخ‌نامه را بارگذاری کنید.','warn');studentPdfInput?.focus();return;}
    if(!auto&&!confirm('پس از ثبت نهایی، امکان شرکت مجدد در این آزمون وجود ندارد. پاسخ‌ها ثبت نهایی شوند؟'))return;
    finalizing=true;clearTimeout(saveTimer);clearInterval(timer);
    const button=document.getElementById('confirmFinishBtn');if(button){button.disabled=true;button.textContent='در حال ثبت...';}
    try{
      while(saving) await new Promise(resolve=>setTimeout(resolve,40));
      const result=await api.request(`/canonical/exams/${encodeURIComponent(examId)}/attempt/${encodeURIComponent(attemptId)}/submit`,{method:'POST',body:JSON.stringify({answers:requestPayload(),autoSubmit:auto})});
      finished=true;
      if(root)root.innerHTML='<div class="alert alert-ok">آزمون با موفقیت ثبت نهایی شد. امکان شرکت مجدد در این آزمون وجود ندارد.</div><p class="mt-16">وضعیت تصحیح: '+(result.requiresManualGrading?'در انتظار تصحیح پاسخ‌های تشریحی':'تصحیح خودکار انجام شد')+'</p><a class="btn btn-primary mt-16" href="dashboard-student.html">بازگشت به داشبورد</a>';
      document.querySelector('.exam-nav-panel')?.remove();
      toast('آزمون ثبت نهایی شد.','ok');
    }catch(error){finalizing=false;if(button){button.disabled=false;button.textContent='بله، پایان آزمون';}toast(error.message||'ثبت نهایی ناموفق بود.','danger');}
  }
  document.getElementById('examNext')?.addEventListener('click',()=>{current=Math.min(current+1,questions.length-1);render();});
  document.getElementById('examPrev')?.addEventListener('click',()=>{current=Math.max(0,current-1);render();});
  document.getElementById('finishExamBtn')?.addEventListener('click',()=>{
    const unanswered=questions.filter(q=>!answered(q)).length;
    const summary=document.getElementById('finishSummary');
    if(summary)summary.textContent=unanswered?`${fa(unanswered)} سؤال بدون پاسخ است. با این حال می‌توانید آزمون را ثبت نهایی کنید؛ سؤال‌های بی‌پاسخ نمره صفر می‌گیرند.`:'همه سؤال‌ها پاسخ داده شده‌اند. آیا آزمون را ثبت نهایی می‌کنید؟';
    document.getElementById('finishModal')?.classList.add('show');
  });
  document.getElementById('confirmFinishBtn')?.addEventListener('click',()=>submit(false));
  document.getElementById('downloadExamPdf')?.addEventListener('click',async()=>{
    try{await api.download(`/exam-files/exams/${encodeURIComponent(examId)}`);}
    catch(error){toast(error.message||'دریافت PDF آزمون ناموفق بود.','danger');}
  });
  const studentPdfBox=document.getElementById('studentPdfUpload');
  const studentPdfStatus=document.getElementById('studentPdfStatus');
  const studentPdfInput=document.getElementById('studentAnswerPdf');
  document.getElementById('uploadStudentPdf')?.addEventListener('click',async()=>{
    const file=studentPdfInput?.files?.[0];
    if(!file){if(studentPdfStatus)studentPdfStatus.textContent='ابتدا فایل PDF پاسخ را انتخاب کنید.';return;}
    if(file.size>20*1024*1024){if(studentPdfStatus)studentPdfStatus.textContent='حجم فایل نباید بیشتر از ۲۰ مگابایت باشد.';return;}
    const form=new FormData();form.append('file',file);
    const button=document.getElementById('uploadStudentPdf');
    uploadingPdf=true;if(button)button.disabled=true;
    if(studentPdfStatus)studentPdfStatus.textContent='در حال بارگذاری...';
    try{
      const result=await api.request(`/exam-files/attempts/${encodeURIComponent(attemptId)}`,{method:'POST',body:form});
      uploadedPdf=true;
      if(studentPdfStatus)studentPdfStatus.textContent=`بارگذاری شد: ${result.fileName}`;
      updateProgress();
      toast('فایل پاسخ با موفقیت بارگذاری شد.','ok');
    }catch(error){
      if(studentPdfStatus)studentPdfStatus.textContent=error.message||'بارگذاری فایل ناموفق بود.';
      toast(error.message||'بارگذاری فایل ناموفق بود.','danger');
    }finally{uploadingPdf=false;if(button)button.disabled=false;}
  });

  try{
    if(!api||!examId||!attemptId)throw new Error('شناسه آزمون یا تلاش معتبر نیست.');
    const data=await api.request(`/canonical/exams/${encodeURIComponent(examId)}/attempt/${encodeURIComponent(attemptId)}`);
    exam={...data,examPdfAvailable:data.examPdfAvailable===true};sections=data.sections||[];questions=flatQuestions(data);
    const downloadButton=document.getElementById('downloadExamPdf');
    if(downloadButton)downloadButton.hidden=!exam.examPdfAvailable;
    const pdfViewer=document.getElementById('examPdfViewer');
    if(exam.examPdfAvailable&&pdfViewer){
      pdfViewer.hidden=false;
      const name=document.getElementById('examPdfName');if(name)name.textContent=data.examPdfFileName||'فایل صورت آزمون';
      const frame=document.getElementById('examPdfFrame');
      if(frame)frame.src=await api.objectUrl(`/exam-files/exams/${encodeURIComponent(examId)}`);
    }
    const hasEssayQuestions=questions.some(q=>/essay/i.test(q.type));
    if(studentPdfBox)studentPdfBox.hidden=!(exam.examPdfAvailable||hasEssayQuestions);
    if(exam.examPdfAvailable||hasEssayQuestions){
      const label=studentPdfBox?.querySelector('label');if(label)label.textContent='بارگذاری پاسخ‌نامه PDF (حداکثر ۲۰ مگابایت) — برای سؤال تشریحی الزامی است';
      try{const info=await api.request(`/exam-files/attempts/${encodeURIComponent(attemptId)}/info`);uploadedPdf=true;if(studentPdfStatus)studentPdfStatus.textContent=`فایل ثبت‌شده: ${info.fileName}`;}catch{uploadedPdf=false;}
    }
    deadline=new Date(data.deadline).getTime();
    const serverNow=data.serverNowUtc ? Date.parse(data.serverNowUtc) : Date.now();
    remainingAtSyncMs=deadline-serverNow;
    syncMonotonic=window.performance?.now?.() ?? Date.now();
    if(!Number.isFinite(deadline)||!Number.isFinite(remainingAtSyncMs)||remainingAtSyncMs<0)throw new Error('مهلت آزمون از سرور معتبر دریافت نشد.');
    if(titleNode)titleNode.textContent=data.title||'آزمون آنلاین';
    const saved=data.savedAnswers||{};
    Object.entries(saved).forEach(([id,json])=>{try{answers.set(Number(id),JSON.parse(json));}catch{}});
    if(data.isPdfOnly){
      if(titleNode)titleNode.textContent=data.title||'آزمون PDFمحور';
      root.querySelectorAll('.badge, #flagQuestion, .option-row, .exam-layout .alert-warn, #examPrev, #examNext').forEach(node=>node.hidden=true);
      const heading=root.querySelector('h3');if(heading)heading.textContent='آزمون PDFمحور';
      const intro=document.createElement('p');intro.className='text-soft';intro.textContent='فایل صورت آزمون را دریافت کنید، پاسخ‌ها را در یک فایل PDF آماده و پیش از ثبت نهایی بارگذاری کنید.';
      heading?.after(intro);
      if(studentPdfBox){studentPdfBox.hidden=false;studentPdfBox.querySelector('label').textContent='فایل پاسخ آزمون (PDF، حداکثر ۲۰ مگابایت؛ برای ثبت نهایی الزامی)';}
      document.querySelector('.exam-nav-panel')?.remove();
      if(progressLabel)progressLabel.textContent='آزمون PDFمحور';
      if(progress)progress.style.width='100%';
    }else{
      if(!questions.length)throw new Error('برای این آزمون سؤالی تعریف نشده است.');
      renderNav();render();
    }
    startClock();
  }catch(error){
    if(root)root.innerHTML='<div class="alert alert-danger">'+esc(error.message||'بارگذاری آزمون ناموفق بود.')+'</div><a class="btn btn-outline mt-16" href="dashboard-student.html">بازگشت به داشبورد</a>';
    toast(error.message||'بارگذاری آزمون ناموفق بود.','danger');
  }
})();
