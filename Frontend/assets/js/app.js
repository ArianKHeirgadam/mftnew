/* ==========================================================================
   app.js — application shell + shared UI component behaviors
   Sidebar & topbar are rendered from a single config so every dashboard
   page stays a thin template (mirrors how this will later become a
   Django {% include %} partial fed by request.user.role).
   ========================================================================== */

const ICON = {
  grid: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><rect x="3" y="3" width="8" height="8" rx="2"/><rect x="13" y="3" width="8" height="8" rx="2"/><rect x="3" y="13" width="8" height="8" rx="2"/><rect x="13" y="13" width="8" height="8" rx="2"/></svg>',
  building: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 21V5a1 1 0 0 1 1-1h9a1 1 0 0 1 1 1v16"/><path d="M15 10h4a1 1 0 0 1 1 1v10"/><path d="M8 8h1M8 12h1M8 16h1M4 21h16"/></svg>',
  students: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="m12 3 9 4.5-9 4.5-9-4.5Z"/><path d="M7 10v5c0 1.7 2.2 3 5 3s5-1.3 5-3v-5"/></svg>',
  teacher: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="8" r="3.2"/><path d="M5 20c0-3.6 3.1-6.5 7-6.5s7 2.9 7 6.5"/></svg>',
  bank: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 10h16M4 10 12 4l8 6M6 10v9M18 10v9M10 10v9M14 10v9M3 19h18"/></svg>',
  exam: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M7 3h8l4 4v14H7z"/><path d="M15 3v4h4M9 12h6M9 16h6M9 8h2"/></svg>',
  result: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M12 3a9 9 0 1 0 9 9"/><path d="m9 12 2 2 4-5M21 3v6h-6"/></svg>',
  report: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M4 20V10M10 20V4M16 20v-7M4 20h16"/></svg>',
  bell: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M18 9a6 6 0 0 0-12 0c0 5-2 6-2 6h16s-2-1-2-6Z"/><path d="M10 20a2 2 0 0 0 4 0"/></svg>',
  settings: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><circle cx="12" cy="12" r="3.2"/><path d="M19.4 13.5a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.9 2.9l-.1-.1a1.7 1.7 0 0 0-1.9-.3 1.7 1.7 0 0 0-1 1.6V20a2 2 0 1 1-4 0v-.2a1.7 1.7 0 0 0-1-1.6 1.7 1.7 0 0 0-1.9.3l-.1.1a2 2 0 1 1-2.9-2.9l.1-.1a1.7 1.7 0 0 0 .3-1.9 1.7 1.7 0 0 0-1.6-1H4a2 2 0 1 1 0-4h.2a1.7 1.7 0 0 0 1.6-1 1.7 1.7 0 0 0-.3-1.9l-.1-.1a2 2 0 1 1 2.9-2.9l.1.1a1.7 1.7 0 0 0 1.9.3H10a1.7 1.7 0 0 0 1-1.6V4a2 2 0 1 1 4 0v.2a1.7 1.7 0 0 0 1 1.6c.6.26 1.35.17 1.9-.3l.1-.1a2 2 0 1 1 2.9 2.9l-.1.1a1.7 1.7 0 0 0-.3 1.9c.26.6.86 1 1.5 1H20a2 2 0 1 1 0 4h-.2a1.7 1.7 0 0 0-1.6 1Z"/></svg>',
  logout: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"><path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4M16 17l5-5-5-5M21 12H9"/></svg>',
  chevron: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="m6 9 6 6 6-6"/></svg>',
  search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="11" cy="11" r="7"/><path d="m20 20-3.2-3.2"/></svg>',
  menu: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h16"/></svg>',
  profile: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="8" r="3.5"/><path d="M5 20c0-3.9 3.1-7 7-7s7 3.1 7 7"/></svg>',
  ok: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="m8 12 3 3 5-6"/></svg>',
  info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><circle cx="12" cy="12" r="9"/><path d="M12 8h.01M11 12h1v5h1"/></svg>',
  warn: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M12 3 2 20h20Z"/><path d="M12 9v5M12 17h.01"/></svg>',
};

/* ---------------- Navigation config per role ---------------- */
const NAV_BY_ROLE = {
  superadmin: [
    {
      section: "اصلی", items: [
        { key: "dashboard", label: "داشبورد", href: "dashboard-superadmin.html", icon: ICON.grid },
      ]
    },
    {
      section: "مدیریت آموزشی", items: [
        { key: "departments", label: "دپارتمان‌ها", href: "departments.html", icon: ICON.building },
        { key: "managers", label: "مدیران", href: "managers.html", icon: ICON.teacher },
        { key: "students", label: "دانشجویان", href: "students.html", icon: ICON.students },
        { key: "teachers", label: "اساتید", href: "teachers.html", icon: ICON.teacher },
        { key: "bank", label: "بانک سوالات", href: "question-bank.html", icon: ICON.bank },
        { key: "exams", label: "آزمون‌ها", href: "create-exam.html", icon: ICON.exam },
        { key: "results", label: "نتایج", href: "results.html", icon: ICON.result },
        { key: "attendance", label: "حضور و غیاب", href: "attendance.html", icon: ICON.students },
      ]
    },
    {
      section: "سامانه", items: [
        { key: "reports", label: "گزارش‌ها", href: "reports.html", icon: ICON.report },
        { key: "notifications", label: "اعلان‌ها", href: "notifications.html", icon: ICON.bell, badge: 5 },
        { key: "settings", label: "تنظیمات", href: "settings.html", icon: ICON.settings },
      ]
    },
  ],
  department: [
    {
      section: "اصلی", items: [
        { key: "dashboard", label: "داشبورد دپارتمان", href: "dashboard-department.html", icon: ICON.grid },
      ]
    },
    {
      section: "دپارتمان مهندسی کامپیوتر", items: [
        { key: "students", label: "دانشجویان", href: "students.html", icon: ICON.students },
        { key: "teachers", label: "اساتید", href: "teachers.html", icon: ICON.teacher },
        { key: "bank", label: "بانک سوالات", href: "question-bank.html", icon: ICON.bank },
        { key: "exams", label: "آزمون‌ها", href: "create-exam.html", icon: ICON.exam },
        { key: "results", label: "نتایج", href: "results.html", icon: ICON.result },
        { key: "attendance", label: "حضور و غیاب", href: "attendance.html", icon: ICON.students },
      ]
    },
    {
      section: "سامانه", items: [
        { key: "reports", label: "گزارش‌ها", href: "reports.html", icon: ICON.report },
        { key: "notifications", label: "اعلان‌ها", href: "notifications.html", icon: ICON.bell, badge: 2 },
        { key: "settings", label: "تنظیمات", href: "settings.html", icon: ICON.settings },
      ]
    },
  ],
  teacher: [
    {
      section: "اصلی", items: [
        { key: "dashboard", label: "داشبورد استاد", href: "dashboard-teacher.html", icon: ICON.grid },
      ]
    },
    {
      section: "آموزش", items: [
        { key: "bank", label: "بانک سوالات", href: "question-bank.html", icon: ICON.bank },
        { key: "exams", label: "ساخت آزمون", href: "create-exam.html", icon: ICON.exam },
        { key: "results", label: "تصحیح و نتایج", href: "results.html", icon: ICON.result },
        { key: "attendance", label: "حضور و غیاب", href: "attendance.html", icon: ICON.students },
      ]
    },
    {
      section: "سامانه", items: [
        { key: "notifications", label: "اعلان‌ها", href: "notifications.html", icon: ICON.bell, badge: 3 },
        { key: "settings", label: "تنظیمات", href: "settings.html", icon: ICON.settings },
      ]
    },
  ],
  student: [
    {
      section: "اصلی", items: [
        { key: "dashboard", label: "داشبورد", href: "dashboard-student.html", icon: ICON.grid },
      ]
    },
    {
      section: "آزمون‌ها", items: [
        { key: "exams", label: "آزمون‌های من", href: "dashboard-student.html", icon: ICON.exam },
        { key: "results", label: "نتایج من", href: "results.html", icon: ICON.result },
      ]
    },
    {
      section: "سامانه", items: [
        { key: "notifications", label: "اعلان‌ها", href: "notifications.html", icon: ICON.bell, badge: 1 },
        { key: "settings", label: "تنظیمات", href: "settings.html", icon: ICON.settings },
      ]
    },
  ],
};

const ROLE_LABEL = {
  superadmin: "مدیر کل سامانه",
  department: "مدیر دپارتمان",
  teacher: "استاد",
  student: "دانشجو",
};

function getMftSessionUser() {
  try {
    return JSON.parse(sessionStorage.getItem("mft.currentUser") || localStorage.getItem("mft.currentUser") || "null");
  } catch {
    return null;
  }
}

function renderSidebar() {
  const root = document.getElementById("sidebar-root");
  if (!root) return;
  const storedUser = getMftSessionUser();
  const role = String(storedUser?.role || document.body.dataset.role || "student").toLowerCase();
  document.body.dataset.role = role;
  const active = document.body.dataset.active || "dashboard";
  const groups = NAV_BY_ROLE[role] || NAV_BY_ROLE.student;

  const navHtml = groups.map(g => `
    <div class="nav-section-title">${g.section}</div>
    <div class="nav-list">
      ${g.items.map(it => `
        <a class="nav-link ${it.key === active ? "active" : ""}" href="${it.href}">
          ${it.icon}
          <span class="nav-label">${it.label}</span>
          ${it.badge ? `<span class="badge-count">${it.badge}</span>` : ""}
        </a>
      `).join("")}
    </div>
  `).join("");

  root.innerHTML = `
    <aside class="sidebar" id="sidebar">
      <div class="brand">
        <div><img class="mohr-avatar" src="/assets/img/logo.jpeg" alt="logo" srcset="" style="width:60px; height:60px"></div>
        <div class="brand-text">
          <b>سامانه دیباگران تبریز</b>
          <span>پلتفرم آزمون‌های آنلاین</span>
        </div>
      </div>
      <nav>${navHtml}</nav>
      <div class="sidebar-foot">
        <a class="nav-link" href="login.html"> ${ICON.logout} <span class="nav-label">خروج از حساب</span></a>
        
      </div>
      <button class="collapse-btn" id="collapseBtn" type="button">
          ${ICON.chevron}
          <span class="sidebar-foot-text">جمع‌کردن منو</span>
        </button>
    </aside>
    <div class="scrim" id="scrim"></div>
  `;

  document.getElementById("collapseBtn").addEventListener("click", () => {
    document.getElementById("sidebar").classList.toggle("collapsed");
  });
  document.getElementById("scrim").addEventListener("click", closeMobileSidebar);
}

function openMobileSidebar() {
  document.getElementById("sidebar")?.classList.add("mobile-open");
  document.getElementById("scrim")?.classList.add("show");
}
function closeMobileSidebar() {
  document.getElementById("sidebar")?.classList.remove("mobile-open");
  document.getElementById("scrim")?.classList.remove("show");
}

function renderTopbar() {
  const root = document.getElementById("topbar-root");
  if (!root) return;
  const title = root.dataset.title || "داشبورد";
  const crumb = root.dataset.crumb || "";
  const storedUser = getMftSessionUser();
  const role = String(storedUser?.role || document.body.dataset.role || "student").toLowerCase();
  document.body.dataset.role = role;
  const showSearch = root.dataset.search !== "false";

  root.innerHTML = `
    <header class="topbar">
      <button class="icon-btn menu-toggle" id="menuToggle" aria-label="باز کردن منو">${ICON.menu}</button>
      ${showSearch ? `
      <div class="search-box">
        ${ICON.search}
        <input type="text" placeholder="جستجو در سامانه...">
      </div>` : `<div class="page-head" style="margin:0"><div><div class="breadcrumb">${crumb}</div><h1 style="font-size:18px">${title}</h1></div></div>`}
      <div class="topbar-spacer"></div>
      <div class="topbar-right">
        <button class="icon-btn" data-theme-toggle data-theme-icon aria-label="تغییر پوسته"></button>
        <div class="dropdown">
          <button class="icon-btn" data-dropdown-toggle="notifDrop" aria-label="اعلان‌ها">
            ${ICON.bell}<span class="dot-ping"></span>
          </button>
          <div class="dropdown-menu" id="notifDrop" style="width:300px">
            <div style="padding:10px 12px;font-weight:700;font-size:13px">اعلان‌های اخیر</div>
            <a href="notifications.html">📝 نمره‌ی آزمون میان‌ترم اعلام شد</a>
            <a href="notifications.html">📅 آزمون ساختمان داده فردا برگزار می‌شود</a>
            <a href="notifications.html">✅ ثبت‌نام شما تایید شد</a>
            <hr>
            <a href="notifications.html" style="justify-content:center;color:var(--primary)">مشاهده همه</a>
          </div>
        </div>
        <div class="dropdown">
          <div class="profile-chip" data-dropdown-toggle="profileDrop" style="cursor:pointer">
            <div class="mohr-avatar" style="width:36px; height:36px;"><span class="initials">${role === "superadmin" ? "مک" : role === "department" ? "مد" : role === "teacher" ? "استا" : "دان"}</span></div>
            <div class="who"><b>${storedUser?.fullName || storedUser?.username || (role === "superadmin" ? "مدیر سامانه" : role === "department" ? "مدیر دپارتمان" : role === "teacher" ? "استاد" : "دانشجو")}</b><span>${ROLE_LABEL[role] || "کاربر"}</span></div>
          </div>
          <div class="dropdown-menu" id="profileDrop">
            <a href="profile.html">${ICON.profile} پروفایل من</a>
            <a href="settings.html">${ICON.settings} تنظیمات</a>
            <hr>
            <a href="login.html" style="color:var(--danger)">${ICON.logout} خروج از حساب</a>
          </div>
        </div>
      </div>
    </header>
  `;
  document.getElementById("menuToggle")?.addEventListener("click", openMobileSidebar);
}

/* ---------------- Dropdowns ---------------- */
function initDropdowns() {
  document.addEventListener("click", (e) => {
    const trigger = e.target.closest("[data-dropdown-toggle]");
    document.querySelectorAll(".dropdown-menu.show").forEach((m) => {
      if (!trigger || m.id !== trigger.dataset.dropdownToggle) m.classList.remove("show");
    });
    if (trigger) {
      const menu = document.getElementById(trigger.dataset.dropdownToggle);
      menu?.classList.toggle("show");
    } else if (!e.target.closest(".dropdown-menu")) {
      document.querySelectorAll(".dropdown-menu.show").forEach((m) => m.classList.remove("show"));
    }
  });
}

/* ---------------- Modals ---------------- */
function initModals() {
  document.querySelectorAll("[data-modal-open]").forEach((btn) => {
    btn.addEventListener("click", () => {
      document.getElementById(btn.dataset.modalOpen)?.classList.add("show");
    });
  });
  document.querySelectorAll("[data-modal-close]").forEach((btn) => {
    btn.addEventListener("click", () => {
      btn.closest(".modal-overlay")?.classList.remove("show");
    });
  });
  document.querySelectorAll(".modal-overlay").forEach((ov) => {
    ov.addEventListener("click", (e) => { if (e.target === ov) ov.classList.remove("show"); });
  });
}

/* ---------------- Tabs ---------------- */
function initTabs() {
  document.querySelectorAll("[data-tabs]").forEach((wrap) => {
    const btns = wrap.querySelectorAll(".tab-btn");
    btns.forEach((btn) => {
      btn.addEventListener("click", () => {
        btns.forEach((b) => b.classList.remove("active"));
        wrap.querySelectorAll(".tab-panel").forEach((p) => p.classList.remove("active"));
        btn.classList.add("active");
        document.getElementById(btn.dataset.tab)?.classList.add("active");
      });
    });
  });
}

/* ---------------- Accordion ---------------- */
function initAccordion() {
  document.querySelectorAll(".accordion-head").forEach((head) => {
    head.addEventListener("click", () => {
      head.closest(".accordion-item").classList.toggle("open");
    });
  });
}

/* ---------------- Password visibility toggle ---------------- */
function initPasswordToggles() {
  document.querySelectorAll("[data-toggle-password]").forEach((btn) => {
    btn.addEventListener("click", () => {
      const input = document.getElementById(btn.dataset.togglePassword);
      if (!input) return;
      input.type = input.type === "password" ? "text" : "password";
      btn.classList.toggle("is-visible");
    });
  });
}

/* ---------------- Toasts ---------------- */
function ensureToastStack() {
  let stack = document.querySelector(".toast-stack");
  if (!stack) {
    stack = document.createElement("div");
    stack.className = "toast-stack";
    document.body.appendChild(stack);
  }
  return stack;
}
function showToast(message, type = "info") {
  const stack = ensureToastStack();
  const el = document.createElement("div");
  el.className = `toast ${type}`;
  const icon = type === "ok" ? ICON.ok : type === "warn" ? ICON.warn : type === "danger" ? ICON.warn : ICON.info;
  el.innerHTML = icon;
  const text = document.createElement("span");
  text.textContent = String(message ?? "");
  el.appendChild(text);
  stack.appendChild(el);
  setTimeout(() => {
    el.style.transition = "opacity .3s, transform .3s";
    el.style.opacity = "0";
    el.style.transform = "translateY(10px)";
    setTimeout(() => el.remove(), 300);
  }, 3600);
}
window.showToast = showToast;

/* ---------------- Simple validation helper ---------------- */
function setFieldError(fieldEl, message) {
  fieldEl.classList.add("invalid");
  const err = fieldEl.querySelector(".error-msg");
  if (err) err.textContent = message;
}
function clearFieldError(fieldEl) {
  fieldEl.classList.remove("invalid");
}
window.setFieldError = setFieldError;
window.clearFieldError = clearFieldError;

/* ---------------- Boot ---------------- */
document.addEventListener("DOMContentLoaded", () => {
  renderSidebar();
  renderTopbar();
  initDropdowns();
  initModals();
  initTabs();
  initAccordion();
  initPasswordToggles();
});
