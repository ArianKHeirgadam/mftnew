/* Charts are rendered only from API data; no synthetic/demo series are shown. */
(() => {
  'use strict';
  let charts = [];

  function themeColors() {
    const dark = document.documentElement.getAttribute('data-theme') === 'dark';
    return {
      primary: dark ? '#2DD4BF' : '#0F766E',
      gold: '#C9A227',
      grid: dark ? '#233252' : '#E3E8F0',
      text: dark ? '#8D9AB4' : '#6B7686'
    };
  }

  function renderCharts() {
    charts.forEach(chart => chart.destroy());
    charts = [];
    const data = window.MftChartData || {};
    const colors = themeColors();
    if (typeof Chart !== 'undefined') {
      Chart.defaults.font.family = 'Vazirmatn';
      Chart.defaults.color = colors.text;
    }

    const activityCanvas = document.getElementById('chartActivity');
    if (typeof Chart !== 'undefined' && activityCanvas && Array.isArray(data.activity) && data.activity.length) {
      charts.push(new Chart(activityCanvas, {
        type: 'line',
        data: {
          labels: data.activity.map(item => new Date(item.date).toLocaleDateString('fa-IR')),
          datasets: [{ label: 'تعداد تلاش‌ها', data: data.activity.map(item => item.count), borderColor: colors.primary, backgroundColor: colors.primary + '22', fill: true, tension: 0.3 }]
        },
        options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } }
      }));
    }

    const departmentCanvas = document.getElementById('chartDepartments');
    if (typeof Chart !== 'undefined' && departmentCanvas && Array.isArray(data.departments) && data.departments.length) {
      charts.push(new Chart(departmentCanvas, {
        type: 'bar',
        data: {
          labels: data.departments.map(item => item.department),
          datasets: [{ label: 'دانشجویان', data: data.departments.map(item => item.students), backgroundColor: colors.primary, borderRadius: 6 }]
        },
        options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true } } }
      }));
    }

    const performanceCanvas = document.getElementById('chartPerformance');
    if (performanceCanvas) {
      const host = performanceCanvas.parentElement;
      let emptyState = host.querySelector('[data-performance-empty]');
      if (!Array.isArray(data.performance) || data.performance.length === 0) {
        if (!emptyState) {
          emptyState = document.createElement('div');
          emptyState.dataset.performanceEmpty = 'true';
          emptyState.textContent = 'هنوز نمرهٔ ثبت‌شده‌ای برای نمایش روند وجود ندارد.';
          emptyState.style.cssText = 'position:absolute;inset:0;display:flex;align-items:center;justify-content:center;text-align:center;padding:16px;color:var(--text-muted,#7b8794);font-size:13px;pointer-events:none';
          host.style.position = 'relative';
          host.appendChild(emptyState);
        }
      } else if (emptyState) {
        emptyState.remove();
      }
    }
    if (performanceCanvas && Array.isArray(data.performance) && data.performance.length && typeof Chart !== 'undefined') {
      charts.push(new Chart(performanceCanvas, {
        type: 'line',
        data: { labels: data.performance.map(item => item.label), datasets: [{ label: 'نمره', data: data.performance.map(item => item.score), borderColor: colors.gold, backgroundColor: colors.gold, tension: 0.3, pointRadius: 4 }] },
        options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true }, x: { grid: { display: false } } } }
      }));
    }

    if (performanceCanvas) {
      const host = performanceCanvas.parentElement;
      let fallback = host.querySelector('[data-performance-fallback]');
      if (typeof Chart === 'undefined' && Array.isArray(data.performance) && data.performance.length) {
        if (!fallback) { fallback = document.createElement('ol'); fallback.dataset.performanceFallback = 'true'; fallback.style.cssText = 'margin:8px 0 0;padding:0 22px;display:grid;gap:8px;font-size:13px'; host.appendChild(fallback); }
        fallback.innerHTML = data.performance.map(item => '<li style="display:flex;justify-content:space-between;gap:12px"><span>' + String(item.label).replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch])) + '</span><b>' + Number(item.score).toLocaleString('fa-IR') + '</b></li>').join('');
      } else if (fallback) fallback.remove();
    }

    const resultCanvas = document.getElementById('chartResult');
    if (typeof Chart !== 'undefined' && resultCanvas && data.resultCounts) {
      charts.push(new Chart(resultCanvas, {
        type: 'doughnut',
        data: { labels: ['پاسخ صحیح', 'پاسخ نادرست', 'در انتظار تصحیح'], datasets: [{ data: [data.resultCounts.correct, data.resultCounts.incorrect, data.resultCounts.pending], backgroundColor: [colors.primary, '#D64550', colors.grid], borderWidth: 0 }] },
        options: { responsive: true, maintainAspectRatio: false, cutout: '72%', plugins: { legend: { position: 'bottom' } } }
      }));
    }
  }

  document.addEventListener('mft:data-ready', renderCharts);
  document.addEventListener('DOMContentLoaded', () => setTimeout(renderCharts, 100));
})();
