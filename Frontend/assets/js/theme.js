/* ==========================================================================
   theme.js — light / dark mode persistence + toggle wiring
   ========================================================================== */
(function () {
  const STORAGE_KEY = "edu-portal-theme";

  function getPreferred() {
    const saved = localStorage.getItem(STORAGE_KEY);
    if (saved) return saved;
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  function apply(theme) {
    document.documentElement.setAttribute("data-theme", theme);
    localStorage.setItem(STORAGE_KEY, theme);
    document.querySelectorAll("[data-theme-icon]").forEach((btn) => {
      btn.innerHTML = theme === "dark" ? ICONS.sun : ICONS.moon;
    });
  }

  const ICONS = {
    sun: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><circle cx="12" cy="12" r="4.5"/><path d="M12 2.5v2.5M12 19v2.5M4.2 4.2l1.8 1.8M18 18l1.8 1.8M2.5 12H5M19 12h2.5M4.2 19.8L6 18M18 6l1.8-1.8"/></svg>',
    moon: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 14.5A8.5 8.5 0 1 1 9.5 4a7 7 0 0 0 10.5 10.5Z"/></svg>',
  };

  // Apply immediately (before paint flash)
  apply(getPreferred());

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-theme-toggle]").forEach((btn) => {
      if (btn.dataset.themeWired === "true") return;
      btn.dataset.themeWired = "true";
      btn.addEventListener("click", () => {
        const current = document.documentElement.getAttribute("data-theme");
        apply(current === "dark" ? "light" : "dark");
      });
    });
    // Topbar is rendered by app.js during DOMContentLoaded; keep newly
    // inserted theme buttons wired and their icons synchronized.
    const syncThemeControls = () => {
      document.querySelectorAll("[data-theme-icon]").forEach((btn) => {
        btn.innerHTML = document.documentElement.getAttribute("data-theme") === "dark" ? ICONS.sun : ICONS.moon;
      });
      document.querySelectorAll("[data-theme-toggle]").forEach((btn) => {
        if (btn.dataset.themeWired === "true") return;
        btn.dataset.themeWired = "true";
        btn.addEventListener("click", () => {
          const current = document.documentElement.getAttribute("data-theme");
          apply(current === "dark" ? "light" : "dark");
        });
      });
    };
    syncThemeControls();
    new MutationObserver(syncThemeControls).observe(document.body, { childList: true, subtree: true });
    apply(document.documentElement.getAttribute("data-theme") || getPreferred());
  });
})();
