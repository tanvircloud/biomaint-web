// ==========================================================================
// BioMaint Theme runtime (Light / Dark / Auto)
// - Safe localStorage usage (private browsing friendly)
// - Updates <meta name="theme-color"> for mobile browser chrome
// - Reacts to OS scheme changes (with Safari fallback)
// - Sets data-bs-theme for Bootstrap 5.3
// - Cross-tab sync via storage event
// - Tiny API: init, set, getSaved, getEffective, cycle, onChange
// ==========================================================================

(function (w, d) {
  'use strict';

  const KEY = 'biomaint-theme';
  let inited = false; // idempotent init

  // ---------------------- Helpers -----------------------------------------
  const hasMM = !!w.matchMedia;
  const mqDark  = hasMM ? w.matchMedia('(prefers-color-scheme: dark)')  : null;
  const mqLight = hasMM ? w.matchMedia('(prefers-color-scheme: light)') : null;

  function prefersDark() {
    return !!(mqDark && mqDark.matches);
  }

  function resolve(mode) {
    if (mode === 'light' || mode === 'dark') return mode;
    return prefersDark() ? 'dark' : 'light'; // auto fallback
  }

  function safeGetItem(k) {
    try { return w.localStorage.getItem(k); }
    catch { return null; }
  }

  function safeSetItem(k, v) {
    try { w.localStorage.setItem(k, v); }
    catch { /* ignore in private mode */ }
  }

  function normalizeSaved(v) {
    // Accept only light/dark/auto. Anything else becomes "auto".
    return (v === 'light' || v === 'dark' || v === 'auto') ? v : 'auto';
  }

  // ---------------------- Core --------------------------------------------
  function setAttrs(effective) {
    // Apply data-theme (our CSS) and data-bs-theme (Bootstrap 5.3)
    d.documentElement.setAttribute('data-theme', effective);
    d.documentElement.setAttribute('data-bs-theme', effective);

    // Hint UA controls (scrollbars, form controls in some browsers)
    try { d.documentElement.style.colorScheme = effective; } catch {}

    // Update meta theme-color (for mobile browser chrome)
    const meta = d.querySelector("meta[name='theme-color']");
    if (meta) {
      meta.setAttribute('content', effective === 'dark' ? '#0a0a0a' : '#10a37f');
    }
  }

  function apply(mode) {
    const effective = resolve(mode);
    setAttrs(effective);

    // Fire event so Blazor/JS can listen
    const detail = { mode, effective };
    d.dispatchEvent(new CustomEvent('biothemechange', { detail }));
  }

  function getSaved() {
    return normalizeSaved(safeGetItem(KEY)) || 'auto';
  }

  function getEffective() {
    return resolve(getSaved());
  }

  function set(mode) {
    const m = normalizeSaved(mode);
    safeSetItem(KEY, m);
    apply(m);
  }

  function init() {
    if (inited) return;
    inited = true;

    // 1) Apply immediately on boot
    apply(getSaved());

    // 2) React to OS changes only if Auto mode
    const reactIfAuto = () => { if (getSaved() === 'auto') apply('auto'); };

    // Modern browsers
    mqDark?.addEventListener?.('change', reactIfAuto);
    mqLight?.addEventListener?.('change', reactIfAuto);

    // Safari/older fallback
    mqDark?.addListener?.(reactIfAuto);
    mqLight?.addListener?.(reactIfAuto);

    // 3) Handle mobile/app visibility resumes
    d.addEventListener('visibilitychange', () => {
      if (d.visibilityState === 'visible' && getSaved() === 'auto') {
        (w.queueMicrotask || Promise.resolve().then.bind(Promise.resolve()))(() => apply('auto'));
      }
    });

    // 4) Cross-tab sync
    w.addEventListener('storage', (e) => {
      if (e.key === KEY) apply(getSaved());
    });

    // 5) Initial event for listeners
    d.dispatchEvent(new CustomEvent('biothemeinit', { detail: { mode: getSaved(), effective: getEffective() } }));
  }

  // ---------------------- Public API --------------------------------------
  function cycle() {
    const cur = getSaved();
    const next = cur === 'light' ? 'dark' : (cur === 'dark' ? 'auto' : 'light');
    set(next);
    return next;
  }

  function onChange(cb) {
    if (typeof cb !== 'function') return () => {};
    const handler = (e) => cb(e.detail);
    d.addEventListener('biothemechange', handler);
    return () => d.removeEventListener('biothemechange', handler);
  }

  // Expose API for Blazor/Interop
  w.BioTheme = { init, set, getSaved, getEffective, cycle, onChange };

  // Auto-initialize
  init();
})(window, document);

// --------------------------------------------------------------------------
// Note: navInit / offcanvas glue was removed. Bootstrap handles nav collapse.
// --------------------------------------------------------------------------
