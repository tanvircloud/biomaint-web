// ==========================================================================
// BioMaint Theme + UI (single file)
// - Light/Dark/Auto with persistence (key: biomaint-theme)
// - Updates <meta name="theme-color">
// - Reacts to OS changes (with Safari fallback)
// - Sets data-bs-theme on <html>
// - Cross-tab sync via storage
// - UI wiring: sticky header, burger sync, testimonials arrows, reveal-on-scroll,
//   smooth in-page anchors (auto-close mobile menu), optional logos marquee,
//   theme button (icon + cycle)
// - Exposes: window.BioTheme { init, set, getSaved, getEffective, cycle, onChange }
//            window.BioUI    { init }
// ==========================================================================

(function (w, d) {
  'use strict';

  const KEY = 'biomaint-theme';
  let inited = false;

  // ---------- helpers ----------
  const hasMM = !!w.matchMedia;
  const mqDark = hasMM ? w.matchMedia('(prefers-color-scheme: dark)') : null;
  const mqLight = hasMM ? w.matchMedia('(prefers-color-scheme: light)') : null;
  const prefersDark = () => !!(mqDark && mqDark.matches);

  const safeGetItem = (k) => { try { return w.localStorage.getItem(k); } catch { return null; } };
  const safeSetItem = (k, v) => { try { w.localStorage.setItem(k, v); } catch { } };
  const normalize = (v) => (v === 'light' || v === 'dark' || v === 'auto') ? v : 'auto';

  // ---------- core ----------
  function resolve(mode) {
    if (mode === 'light' || mode === 'dark') return mode;
    return prefersDark() ? 'dark' : 'light'; // auto
  }

  function setAttrs(effective) {
    d.documentElement.setAttribute('data-bs-theme', effective);
    try { d.documentElement.style.colorScheme = effective; } catch { }

    const meta = d.querySelector("meta[name='theme-color']");
    if (meta) meta.setAttribute('content', effective === 'dark' ? '#0a0a0a' : '#10a37f');
  }

  function apply(mode) {
    const effective = resolve(mode);
    setAttrs(effective);
    d.dispatchEvent(new CustomEvent('biothemechange', { detail: { mode, effective } }));
  }

  function getSaved() { return normalize(safeGetItem(KEY)) || 'auto'; }
  function getEffective() { return resolve(getSaved()); }
  function set(mode) { const m = normalize(mode); safeSetItem(KEY, m); apply(m); }

  function init() {
    if (inited) return;
    inited = true;

    // initial paint (avoid FOUC)
    apply(getSaved());

    // react to OS changes in Auto
    const reactIfAuto = () => { if (getSaved() === 'auto') apply('auto'); };
    mqDark?.addEventListener?.('change', reactIfAuto);
    mqLight?.addEventListener?.('change', reactIfAuto);
    mqDark?.addListener?.(reactIfAuto);   // Safari fallback
    mqLight?.addListener?.(reactIfAuto);

    // when tab becomes visible (mobile/app resume)
    d.addEventListener('visibilitychange', () => {
      if (d.visibilityState === 'visible' && getSaved() === 'auto') {
        (w.queueMicrotask || Promise.resolve().then.bind(Promise.resolve()))(() => apply('auto'));
      }
    });

    // cross-tab sync
    w.addEventListener('storage', (e) => { if (e.key === KEY) apply(getSaved()); });

    // fire init event for listeners
    d.dispatchEvent(new CustomEvent('biothemeinit', { detail: { mode: getSaved(), effective: getEffective() } }));
  }

  function cycle() {
    const cur = getSaved();
    const next = cur === 'light' ? 'dark' : (cur === 'dark' ? 'auto' : 'light');
    set(next);
    return next;
  }

  // public API
  w.BioTheme = {
    init, set, getSaved, getEffective, cycle, onChange: (cb) => {
      if (typeof cb !== 'function') return () => { };
      const handler = (e) => cb(e.detail);
      d.addEventListener('biothemechange', handler);
      return () => d.removeEventListener('biothemechange', handler);
    }
  };

  // auto-init immediately (safe/idempotent)
  init();

})(window, document);


// --------------------------------------------------------------------------
// UI wiring for marketing site (Blazor-safe)
// --------------------------------------------------------------------------
(function (w, d) {
  'use strict';
  const $ = (sel, root = d) => root.querySelector(sel);
  const $$ = (sel, root = d) => Array.from(root.querySelectorAll(sel));
  const hasBootstrap = () => !!w.bootstrap;

  // Run an initializer once a selector exists; also works for nodes added later
  function whenReady(selector, initFn, { once = true } = {}) {
    if (typeof initFn !== 'function') return;
    const tryInit = (root = d) => {
      const el = $(selector, root);
      if (!el || el.dataset.init) return false;
      initFn(el);
      if (once) el.dataset.init = '1';
      return true;
    };
    if (tryInit()) return;
    const mo = new MutationObserver((muts) => {
      for (const m of muts) {
        for (const n of m.addedNodes) {
          if (!(n instanceof Element)) continue;
          if (n.matches(selector) || n.querySelector(selector)) {
            if (tryInit(n) && once) { mo.disconnect(); return; }
          }
        }
      }
    });
    mo.observe(d.body, { childList: true, subtree: true });
  }

  function initStickyHeader() {
    const header = $('header') || $('.bm-header') || $('.navbar.sticky-top');
    if (!header) return;
    let ticking = false;
    const onScroll = () => {
      if (ticking) return;
      ticking = true;
      w.requestAnimationFrame(() => {
        const scrolled = (w.scrollY || d.documentElement.scrollTop || d.body?.scrollTop || 0) > 2;
        header.classList.toggle('scrolled', scrolled);
        ticking = false;
      });
    };
    onScroll();
    w.addEventListener('scroll', onScroll, { passive: true });
  }

  function initBurgerSync() {
    whenReady('#navbarNav', () => {
      const toggler = $('#bmToggler');
      const menu = $('#navbarNav');
      if (!(toggler && menu)) return;
      menu.addEventListener('show.bs.collapse', () => toggler.classList.add('open'));
      menu.addEventListener('shown.bs.collapse', () => toggler.setAttribute('aria-expanded', 'true'));
      menu.addEventListener('hide.bs.collapse', () => toggler.classList.remove('open'));
      menu.addEventListener('hidden.bs.collapse', () => toggler.setAttribute('aria-expanded', 'false'));
    });
  }

  function initTestimonials() {
    whenReady('#testiTrack', () => {
      const track = $('#testiTrack');
      const prev = $('#tPrev');
      const next = $('#tNext');
      if (!(track && prev && next)) return;
      const step = () => Math.min(track.clientWidth * 0.9, 600);
      prev.addEventListener('click', () => track.scrollBy({ left: -step(), behavior: 'smooth' }));
      next.addEventListener('click', () => track.scrollBy({ left: step(), behavior: 'smooth' }));
    });
  }

  // Reveal-on-scroll that keeps watching for future .reveal nodes
  function initReveal() {
    const seen = new WeakSet();
    let io;
    try {
      io = new IntersectionObserver((entries) => {
        for (const e of entries) {
          if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target); }
        }
      }, { threshold: .12 });
    } catch {
      io = { observe: el => el.classList.add('in'), unobserve: () => { } };
    }
    const bind = (el) => {
      if (!el || seen.has(el) || el.classList.contains('in')) return;
      seen.add(el);
      io.observe(el);
    };
    $$('.reveal').forEach(bind); // initial pass
    const mo = new MutationObserver((muts) => {
      for (const m of muts) {
        for (const n of m.addedNodes) {
          if (!(n instanceof Element)) continue;
          if (n.matches('.reveal')) bind(n);
          $$('.reveal', n).forEach(bind);
        }
      }
    });
    mo.observe(d.body, { childList: true, subtree: true });
  }

  function initSmoothAnchors() {
    d.addEventListener('click', (e) => {
      const a = e.target.closest?.('a[href^="#"]');
      if (!a) return;
      const id = a.getAttribute('href');
      if (!id || id.length <= 1) return;
      const target = d.getElementById(id.slice(1));
      if (!target) return;

      e.preventDefault();
      target.scrollIntoView({ behavior: 'smooth', block: 'start' });

      const menu = $('#navbarNav');
      if (menu && menu.classList.contains('show') && hasBootstrap()) {
        const collapse = bootstrap.Collapse.getOrCreateInstance(menu);
        collapse.hide();
      }
      if (history.pushState) history.pushState(null, '', id);
    });
  }

  function initLogosTrack() {
    whenReady('#logosTrack', () => {
      const track = $('#logosTrack');
      const total = track.scrollWidth;
      if (total < w.innerWidth * 1.8) {
        const clone = track.cloneNode(true);
        clone.id = '';
        track.parentElement?.appendChild(clone);
      }
    });
  }

  function initThemeControls() {
    // Bind when available, and re-bind if Blazor re-renders
    whenReady('#themeMenuBtn', (menuBtn) => {
      if (menuBtn.dataset.init) return;
      menuBtn.dataset.init = '1';

      const root = menuBtn.closest('nav') || document;
      const menu = root.querySelector('#themeMenu');
      const curIcon = root.querySelector('#themeCurIcon');
      const curLabel = root.querySelector('#themeCurLabel');
      const items = menu ? Array.from(menu.querySelectorAll('[data-mode]')) : [];

      // Icon mapping by SELECTED MODE (not effective)
      const ICON_BY_MODE = {
        light: 'bi bi-sun',
        dark: 'bi bi-moon-stars',
        auto: 'bi bi-circle-half'
      };

      const render = () => {
        const mode = (window.BioTheme?.getSaved?.() || 'auto');
        const eff = (window.BioTheme?.getEffective?.() || 'light');

        // Button face reflects the chosen MODE
        if (curIcon) curIcon.className = `${ICON_BY_MODE[mode]} me-2`;
        if (curLabel) curLabel.textContent = (mode === 'auto') ? 'Auto' : (mode === 'dark' ? 'Dark' : 'Light');

        menuBtn.dataset.mode = mode;
        menuBtn.title = `Theme: ${mode} (effective ${eff})`;
        menuBtn.setAttribute('aria-label', menuBtn.title);

        // Highlight active item
        for (const el of items) {
          const active = el.getAttribute('data-mode') === mode;
          el.classList.toggle('active', active);
          el.setAttribute('aria-checked', active ? 'true' : 'false');
        }
      };

      if (menu) {
        menu.addEventListener('click', (e) => {
          const btn = e.target.closest('[data-mode]');
          if (!btn) return;
          e.preventDefault();
          e.stopPropagation();

          const m = btn.getAttribute('data-mode');
          if (m) window.BioTheme?.set?.(m);           // persists + dispatches event
          if (window.bootstrap) {
            const dd = bootstrap.Dropdown.getOrCreateInstance(menuBtn);
            dd.hide();
          }
        }, { passive: false });
      }

      document.addEventListener('biothemeinit', render);
      document.addEventListener('biothemechange', render);
      render();
    }, { once: false });
  }

  // Keeps the footer from flashing by sizing <main> to fill the viewport
  function initLayoutSizing() {
    // Re-run if Blazor swaps out DOM; don't double-bind the same <main>
    whenReady('main.bm-main, main', (mainEl) => {
      if (!mainEl || mainEl.dataset.sized) return;
      mainEl.dataset.sized = '1';

      const headerEl = $('header, .bm-header, .navbar.sticky-top');
      const footerEl = $('footer, .bm-footer, .site-footer');

      const vv = window.visualViewport;
      const ro = new ResizeObserver(update);

      if (headerEl) ro.observe(headerEl);
      if (footerEl) ro.observe(footerEl);

      window.addEventListener('resize', update, { passive: true });
      if (vv) vv.addEventListener('resize', update, { passive: true });

      function viewportH() {
        return (vv && vv.height) || window.innerHeight || document.documentElement.clientHeight;
      }

      function update() {
        const hH = headerEl?.offsetHeight || 0;
        const fH = footerEl?.offsetHeight || 0;
        const min = Math.max(0, viewportH() - hH - fH);
        const v = `${min}px`;
        if (mainEl.style.minHeight !== v) mainEl.style.minHeight = v;
      }

      // first paint
      update();
    }, { once: false });
  }


  function init() {
    initStickyHeader();
    initBurgerSync();
    initTestimonials();
    initReveal();          // ← resilient to Blazor’s late rendering
    initSmoothAnchors();
    initLogosTrack();
    initThemeControls();
      // NEW: ensure footer doesn't flash during page load
    initLayoutSizing();
  }

  w.BioUI = { init };

  if (d.readyState === 'loading') {
    d.addEventListener('DOMContentLoaded', init, { once: true });
  } else {
    init();
  }

})(window, document);
