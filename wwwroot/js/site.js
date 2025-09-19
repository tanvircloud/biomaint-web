// Robust Offcanvas glue for #mainNav
(function () {
  function ensureOffcanvas() {
    var el = document.getElementById('mainNav');
    if (!el || !window.bootstrap || !bootstrap.Offcanvas) return false;

    // Force rebuild: dispose any old instance then re-create
    var inst = bootstrap.Offcanvas.getInstance(el);
    if (inst) inst.dispose();
    bootstrap.Offcanvas.getOrCreateInstance(el);

    return true;
  }

  function wireDismiss() {
    var el = document.getElementById('mainNav');
    if (!el) return;
    el.querySelectorAll('a[href], button, [data-bs-dismiss="offcanvas"]').forEach(function (n) {
      n.addEventListener('click', function () {
        try { bootstrap.Offcanvas.getInstance(el)?.hide(); } catch { }
      });
    });
  }

  // Programmatic toggle fallback (needed for Blazor re-rendered DOM)
  document.addEventListener('click', function (e) {
    var btn = e.target && e.target.closest('[data-bs-toggle="offcanvas"][data-bs-target="#mainNav"]');
    if (!btn) return;
    var panel = document.getElementById('mainNav');
    if (panel && window.bootstrap && bootstrap.Offcanvas) {
      bootstrap.Offcanvas.getOrCreateInstance(panel).toggle();
      e.preventDefault();
    }
  }, true);

  function tryInit(retries) {
    if (ensureOffcanvas()) { wireDismiss(); return; }
    if (retries <= 0) return;
    setTimeout(function () { tryInit(retries - 1); }, 250);
  }

  // Run after DOM + after Blazor
  document.addEventListener('DOMContentLoaded', function () { tryInit(40); });
  window.addEventListener('load', function () { tryInit(40); });
  document.addEventListener('blazor:navigation-ended', function () { tryInit(10); });
})();
