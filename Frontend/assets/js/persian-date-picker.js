(() => {
'use strict';
const fa = n => Number(n).toLocaleString('fa-IR');
const parts = d => Object.fromEntries(new Intl.DateTimeFormat('en-US-u-ca-persian',{year:'numeric',month:'numeric',day:'numeric'}).formatToParts(d).filter(x=>x.type!=='literal').map(x=>[x.type,Number(x.value)]));
const iso = d => d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0');
const parse = v => { const m=String(v||'').match(/^(\d{4})-(\d{2})-(\d{2})/); return m?new Date(+m[1],+m[2]-1,+m[3]):null; };
const monthDays = (y,m) => { const a=[],s=new Date(y+621,2,15),e=new Date(y+622,2,25); for(let d=new Date(s);d<=e;d.setDate(d.getDate()+1)){const p=parts(d);if(p.year===y&&p.month===m)a.push(new Date(d));} return a; };
const fmt = d => new Intl.DateTimeFormat('fa-IR-u-ca-persian',{year:'numeric',month:'long',day:'numeric'}).format(d);
const nativeD=Date.prototype.toLocaleDateString,nativeS=Date.prototype.toLocaleString;
if(!Date.prototype.__mftFaPersianCalendar){
 Date.prototype.toLocaleDateString=function(l,o){return String(l||'').toLowerCase().startsWith('fa')?nativeD.call(this,l,{calendar:'persian',numberingSystem:'arabext',...(o||{})}):nativeD.call(this,l,o);};
 Date.prototype.toLocaleString=function(l,o){return String(l||'').toLowerCase().startsWith('fa')?nativeS.call(this,l,{calendar:'persian',numberingSystem:'arabext',...(o||{})}):nativeS.call(this,l,o);};
 Object.defineProperty(Date.prototype,'__mftFaPersianCalendar',{value:true});
}
function mount(input){
 if(input.dataset.jalaliEnhanced)return; input.dataset.jalaliEnhanced='1';
 const isDT=input.type==='datetime-local',initial=input.value,initialDate=parse(initial),initialTime=isDT&&initial.includes('T')?initial.split('T')[1].slice(0,5):'10:00';
 const wrap=document.createElement('div');wrap.style.cssText='position:relative;display:flex;align-items:center;gap:8px;width:100%;';
 input.parentNode.insertBefore(wrap,input);wrap.appendChild(input);
 input.style.cssText+=';position:absolute;width:1px;height:1px;opacity:0;pointer-events:none;left:0';input.tabIndex=-1;input.setAttribute('aria-hidden','true');
 const visible=document.createElement('input');visible.type='text';visible.className=input.className||'input';visible.readOnly=true;visible.placeholder='انتخاب تاریخ شمسی';visible.style.cssText='min-width:0;flex:1;cursor:pointer';visible.value=initialDate?fmt(initialDate):'';wrap.appendChild(visible);
 let time=null;if(isDT){time=document.createElement('input');time.type='time';time.className=input.className||'input';time.style.cssText='width:125px;flex:0 0 125px;direction:ltr';time.value=initialTime;wrap.appendChild(time);}
 const pop=document.createElement('div');pop.style.cssText='display:none;position:absolute;top:calc(100% + 7px);right:0;z-index:2000;width:290px;max-width:calc(100vw - 32px);padding:12px;background:var(--surface,#fff);color:var(--text,#172b4d);border:1px solid var(--border,#dce3ed);border-radius:14px;box-shadow:0 12px 32px rgba(0,0,0,.18)';wrap.appendChild(pop);
 let selected=initialDate||new Date(),view=parts(selected);
 const close=()=>pop.style.display='none';
 const setDate=d=>{selected=d;visible.value=fmt(d);input.value=isDT?iso(d)+'T'+(time.value||'10:00'):iso(d);input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));close();};
 const render=()=>{
  const days=monthDays(view.year,view.month),offset=(days[0].getDay()+1)%7;
  const heading=new Intl.DateTimeFormat('fa-IR-u-ca-persian',{year:'numeric',month:'long'}).format(days[0]);
  const week=['ش','ی','د','س','چ','پ','ج'];let cells=week.map(x=>'<span class="mft-cal-weekday">'+x+'</span>').join('');
  for(let i=0;i<offset;i++)cells+='<span></span>';
  days.forEach(d=>{const p=parts(d),active=iso(d)===iso(selected);cells+='<button type="button" data-day="'+p.day+'" class="mft-cal-day'+(active?' active':'')+'">'+fa(p.day)+'</button>';});
  pop.innerHTML='<div style="display:flex;align-items:center;justify-content:space-between;gap:8px;margin-bottom:10px"><button type="button" data-shift="-1" class="mft-cal-nav">‹</button><b>'+heading+'</b><button type="button" data-shift="1" class="mft-cal-nav">›</button></div><div class="mft-cal-grid">'+cells+'</div>';
  pop.querySelectorAll('[data-shift]').forEach(b=>b.addEventListener('click',()=>{let m=view.month+Number(b.dataset.shift),y=view.year;if(m<1){m=12;y--;}if(m>12){m=1;y++;}view={year:y,month:m,day:1};render();}));
  pop.querySelectorAll('[data-day]').forEach(b=>b.addEventListener('click',()=>{const d=days.find(x=>parts(x).day===Number(b.dataset.day));if(d)setDate(d);}));
 };
 visible.addEventListener('click',()=>{if(pop.style.display==='block')close();else{view=parts(parse(input.value)||selected||new Date());render();pop.style.display='block';}});
 if(time)time.addEventListener('change',()=>{const d=parse(input.value)||selected;if(d){input.value=iso(d)+'T'+(time.value||'10:00');input.dispatchEvent(new Event('input',{bubbles:true}));input.dispatchEvent(new Event('change',{bubbles:true}));}});
 document.addEventListener('click',e=>{if(!wrap.contains(e.target))close();});
 input.addEventListener('change',()=>{const d=parse(input.value);if(d){selected=d;visible.value=fmt(d);}});
}
const style=document.createElement('style');style.textContent='.mft-cal-grid{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));gap:4px;text-align:center}.mft-cal-weekday{font-size:11px;color:var(--text-soft,#64748b);padding:5px 0;font-weight:700}.mft-cal-day,.mft-cal-nav{border:0;border-radius:8px;background:var(--surface-2,#f1f5f9);color:var(--text,#172b4d);padding:8px 2px;cursor:pointer;font:inherit}.mft-cal-day:hover,.mft-cal-nav:hover,.mft-cal-day.active{background:var(--primary,#1769e0);color:#fff}.mft-cal-nav{width:32px;height:32px;padding:0;font-size:20px;line-height:1}';document.head.appendChild(style);
const init=()=>document.querySelectorAll('input[type="date"],input[type="datetime-local"]').forEach(mount);
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',init);else init();
window.MftPersianDate={format:fmt};
})();