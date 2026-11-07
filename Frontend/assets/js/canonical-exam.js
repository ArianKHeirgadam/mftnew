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
  let exam, sections=[], essaySections=[], questions=[], answers=new Map(), current=0, deadline=0, remainingAtSyncMs=0, syncMonotonic=0, timer=null, saveTimer=null, saving=false, pending=false, finalizing=false, finished=false, uploadingPdf=false, uploadedPdf=false;

  function flatQuestions(data) {
    return (data.sections || []).flatMap(section => (section.questions || []).map(q => {
      const rawType = q.questionType || q.questionTypeCode || q.type || '';
      const normalizedType = /^(mcq|multiplechoice|multiple_choice)$/i.test(String(rawType).trim())
        ? 'MultipleChoice'
        : String(rawType).trim();
      return {
        ...q,
        sectionId: section.id,
        type: normalizedType,
        points: q.points,
        options: Array.isArray(q.options) ? q.options : [],
        leftItems: Array.isArray(q.matchingLeftItems) ? q.matchingLeftItems : [],
        rightItems: Array.isArray(q.matchingRightItems) ? q.matchingRightItems : []
      };
    }));
  }
  function valueFor(q) {
    if (answers.has(q.examQuestionId)) return answers.get(q.examQuestionId);
    if (/multiplechoice/i.test(q.type)) return { selectedOptionId: null };
    if (/matching/i.test(q.type)) return { pairs: q.leftItems.map(leftItem => ({ leftItemId: Number(leftItem.id), rightItemId: null })) };
    if (/essay/i.test(q.type)) return {};
    return { text: '' };
  }
  function answerText(q) {
    const value=valueFor(q);
    if (/multiplechoice/i.test(q.type)) return value.selectedOptionId ? (q.options.find(o=>o.id===value.selectedOptionId)?.optionText || '—') : 'بدون پاسخ';
    if (/matching/i.test(q.type)) return (value.pairs || []).some(p=>p.rightItemId != null || p.rightItem?.id != null) ? 'جفت‌ها انتخاب شده‌اند' : 'بدون پاسخ';
    if (/essay/i.test(q.type)) return String(value.text || '').trim() || 'بدون پاسخ';
    return value.text || 'بدون پاسخ';
  }
  function answered(q) {
    const v=valueFor(q);
    if (/multiplechoice/i.test(q.type)) return !!v.selectedOptionId;
    if (/matching/i.test(q.type)) return (v.pairs || []).some(p=>p.rightItemId != null || p.rightItem?.id != null);
    if (/essay/i.test(q.type)) return !!String(v.text || '').trim();
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
    const heading=document.getElementById('canonicalQuestionHeading');
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
        const text=document.createElement('span');text.textContent=option.optionText ?? option.text ?? '';
        label.append(radio,letter,text);add(label);
      });
    } else if(/matching/i.test(q.type)) {
      // Matching items are API objects ({ id, text }), never DOM string values.
      const selected=new Map((v.pairs||[]).map(p=>[
        Number(p.leftItemId ?? p.leftItem?.id),
        p.rightItemId ?? p.rightItem?.id ?? null
      ]));
      q.leftItems.forEach(left=>{
        const wrap=document.createElement('div');wrap.className='field';wrap.style.marginBottom='12px';
        const label=document.createElement('label');label.textContent=left.text ?? '';
        const select=document.createElement('select');select.className='input';select.style.width='100%';
        const empty=document.createElement('option');empty.value='';empty.textContent='انتخاب مورد مرتبط';select.append(empty);
        q.rightItems.forEach(right=>{
          const option=document.createElement('option');
          option.value=String(right.id);
          option.textContent=right.text ?? '';
          select.append(option);
        });
        const selectedId=selected.get(Number(left.id));
        select.value=selectedId == null ? '' : String(selectedId);
        select.addEventListener('change',()=>{
          const rightId=select.value ? Number(select.value) : null;
          selected.set(Number(left.id),rightId);
          const pairs=q.leftItems.map(item=>({
            leftItemId:Number(item.id),
            rightItemId:selected.get(Number(item.id)) ?? null
          })).filter(pair=>pair.rightItemId != null);
          answers.set(q.examQuestionId,{pairs});changed();
        });
        wrap.append(label,select);add(wrap);
      });
    } else if (/essay/i.test(q.type)) {
      const field=document.createElement('div');field.className='field mt-16';
      const label=document.createElement('label');label.textContent='پاسخ تشریحی';
      const textarea=document.createElement('textarea');textarea.className='input';textarea.rows=7;textarea.style.width='100%';textarea.placeholder='پاسخ خود را اینجا بنویسید...';textarea.value=String(v.text||'');
      textarea.addEventListener('input',()=>{answers.set(q.examQuestionId,{text:textarea.value});changed();});
      field.append(label,textarea);add(field);
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
  async function renderEssaySections() {
    const area=document.getElementById('essaySectionArea');
    const list=document.getElementById('essaySectionCards');
    if(!area||!list||!essaySections.length)return;
    area.hidden=false;
    list.replaceChildren();
    for(const section of essaySections){
      const card=document.createElement('article');
      card.className='card card-pad mt-16';
      card.style.marginBottom='16px';
      const heading=document.createElement('h4');
      heading.textContent='بخش تشریحی '+fa(section.displayOrder||essaySections.indexOf(section)+1);
      card.append(heading);
      const paperName=document.createElement('p');
      paperName.className='text-soft text-sm mt-8';
      paperName.textContent=section.questionPaper?.originalFileName||'PDF صورت سؤال';
      card.append(paperName);
      const frame=document.createElement('iframe');
      frame.title='PDF صورت سؤال بخش تشریحی';
      frame.style.cssText='width:100%;height:520px;border:1px solid var(--border);border-radius:10px;background:#fff;margin-top:10px';
      try{frame.src=await api.objectUrl('/exam-files/sections/'+encodeURIComponent(section.id)+'/question-paper?attemptId='+encodeURIComponent(attemptId));}
      catch(error){paperName.textContent='دریافت PDF صورت سؤال ناموفق بود: '+(error.message||'خطای نامشخص');}
      card.append(frame);
      const uploadTitle=document.createElement('label');
      uploadTitle.className='field mt-16';uploadTitle.textContent='فایل پاسخ این بخش (فقط PDF)';
      const input=document.createElement('input');
      input.type='file';input.accept='.pdf,application/pdf';input.className='input';input.style.width='100%';
      uploadTitle.append(input);card.append(uploadTitle);
      const status=document.createElement('p');status.className='text-soft text-sm mt-8';
      status.textContent=section.studentAnswerPdf?.originalFileName?'پاسخ بارگذاری‌شده: '+section.studentAnswerPdf.originalFileName:'هنوز پاسخی برای این بخش بارگذاری نشده است.';
      card.append(status);
      if(section.studentAnswerPdf){
        const download=document.createElement('a');download.className='btn btn-outline btn-sm mt-8';
        download.textContent='دریافت PDF پاسخ بارگذاری‌شده';download.target='_blank';download.rel='noopener';
        try{download.href=await api.objectUrl('/exam-files/sections/'+encodeURIComponent(section.id)+'/attempts/'+encodeURIComponent(attemptId)+'/answer');card.append(download);}catch{}
      }
      const button=document.createElement('button');button.type='button';button.className='btn btn-primary mt-12';
      button.textContent=section.studentAnswerPdf?'جایگزینی PDF پاسخ':'بارگذاری PDF پاسخ';
      button.addEventListener('click',async()=>{
        const file=input.files?.[0];
        if(!file){status.textContent='ابتدا فایل PDF پاسخ را انتخاب کنید.';return;}
        if(!/\.pdf$/i.test(file.name)){status.textContent='فقط فایل با پسوند PDF پذیرفته می‌شود.';return;}
        const form=new FormData();form.append('file',file);
        uploadingPdf=true;button.disabled=true;status.textContent='در حال بارگذاری PDF پاسخ...';
        try{
          const result=await api.request('/exam-files/sections/'+encodeURIComponent(section.id)+'/attempts/'+encodeURIComponent(attemptId)+'/answer',{method:'POST',body:form});
          section.studentAnswerPdf={originalFileName:result.fileName,fileSize:result.size,uploadedAt:result.uploadedAt};
          status.textContent='بارگذاری شد: '+result.fileName;button.textContent='جایگزینی PDF پاسخ';input.value='';
          const link=document.createElement('a');link.className='btn btn-outline btn-sm mt-8';link.textContent='دریافت PDF پاسخ بارگذاری‌شده';link.target='_blank';link.rel='noopener';
          link.href=await api.objectUrl('/exam-files/sections/'+encodeURIComponent(section.id)+'/attempts/'+encodeURIComponent(attemptId)+'/answer');
          card.insertBefore(link,button);updateEssayProgress();toast('PDF پاسخ این بخش بارگذاری شد.','ok');
        }catch(error){status.textContent=error.message||'بارگذاری فایل ناموفق بود.';toast(status.textContent,'danger');}
        finally{uploadingPdf=false;button.disabled=false;}
      });
      card.append(button);list.append(card);
    }
    updateEssayProgress();
  }
  function updateEssayProgress(){
    if(!essaySections.length)return;
    const uploaded=essaySections.filter(s=>!!s.studentAnswerPdf).length;
    if(progressLabel)progressLabel.textContent='PDF پاسخ '+fa(uploaded)+' از '+fa(essaySections.length)+' بخش بارگذاری شده';
    if(progress)progress.style.width=(uploaded/essaySections.length*100)+'%';
  }
  function requestPayload() {
    return questions.map(q=>{
      const value=valueFor(q);
      if(/matching/i.test(q.type)){
        return {
          examQuestionId:q.examQuestionId,
          value:{pairs:(value.pairs||[])
            .map(pair=>({
              leftItemId:Number(pair.leftItemId ?? pair.leftItem?.id),
              rightItemId:pair.rightItemId ?? pair.rightItem?.id
            }))
            .filter(pair=>Number.isSafeInteger(pair.leftItemId) && Number.isSafeInteger(Number(pair.rightItemId)))
            .map(pair=>({...pair,rightItemId:Number(pair.rightItemId)}))}
        };
      }
      return {examQuestionId:q.examQuestionId,value};
    });
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
    if(exam?.isPdfOnly && !uploadedPdf && !auto){toast('برای ثبت نهایی، ابتدا فایل PDF پاسخ را بارگذاری کنید.','warn');studentPdfInput?.focus();return;}
    if(!auto&&essaySections.some(section=>!section.studentAnswerPdf)){toast('برای ثبت نهایی، PDF پاسخ تمام بخش‌های تشریحی را بارگذاری کنید.','warn');return;}
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
    exam={...data,examPdfAvailable:data.examPdfAvailable===true};sections=data.sections||[];essaySections=sections.filter(section=>String(section.sectionType||'').toLowerCase()==='essay');questions=flatQuestions(data);
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
    if(studentPdfBox)studentPdfBox.hidden=!exam.isPdfOnly;
    if(exam.isPdfOnly){
      const label=studentPdfBox?.querySelector('label');if(label)label.textContent='فایل پاسخ آزمون (PDF، حداکثر ۲۰ مگابایت؛ برای ثبت نهایی الزامی)';
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
    await renderEssaySections();
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
    }else if(questions.length){
      renderNav();render();
      if(essaySections.length)updateEssayProgress();
    }else if(essaySections.length){
      const heading=root.querySelector('h3');if(heading)heading.textContent='بخش‌های تشریحی آزمون';
      root.querySelectorAll('.badge, #flagQuestion, #examPrev, #examNext').forEach(node=>node.hidden=true);
      document.querySelector('.exam-nav-panel')?.remove();
      if(numberLabel)numberLabel.textContent='بخش‌های تشریحی';
      updateEssayProgress();
    }else{
      throw new Error('برای این آزمون سؤال یا بخش PDF تعریف نشده است.');
    }
    startClock();
  }catch(error){
    if(root)root.innerHTML='<div class="alert alert-danger">'+esc(error.message||'بارگذاری آزمون ناموفق بود.')+'</div><a class="btn btn-outline mt-16" href="dashboard-student.html">بازگشت به داشبورد</a>';
    toast(error.message||'بارگذاری آزمون ناموفق بود.','danger');
  }
})();
