// Robust Turnstile wrapper with cooldown, visibility & offline guards.
// API: init(siteKey, elementId, dotNetRef?), getResponse(), getOrWaitToken(ms),
// reset(), destroy(), setDotNet(ref?)
(function () {
  let widgetId = null;
  let rendering = false;
  let dotnet = null;

  // Backoff after errors to avoid blink loops
  let backoffMs = 3000;           // starts at 3s
  const backoffMax = 30000;       // caps at 30s
  const backoffResetMs = 20000;   // if no errors for 20s, shrink backoff

  let lastErrorAt = 0;
  let online  = navigator.onLine;
  let visible = !document.hidden;

  // A single pending reset timer (avoid multiple concurrent resets)
  let pendingResetTimer = null;

  // Visibility / connectivity
  window.addEventListener('online',  () => { online = true;  tryResume(); });
  window.addEventListener('offline', () => { online = false; cancelPendingReset(); });
  document.addEventListener('visibilitychange', () => {
    visible = !document.hidden;
    if (visible) tryResume(); else cancelPendingReset();
  });

  function setDotNet(ref) { dotnet = ref || null; }
  function notify(evt) {
    if (dotnet && dotnet.invokeMethodAsync) {
      try { dotnet.invokeMethodAsync('OnCaptchaStatus', evt); } catch {}
    }
  }

  function waitForTurnstile(maxMs = 8000) {
    return new Promise((resolve, reject) => {
      if (window.turnstile) return resolve();
      let elapsed = 0;
      const iv = setInterval(() => {
        if (window.turnstile) { clearInterval(iv); resolve(); }
        elapsed += 50;
        if (elapsed >= maxMs) { clearInterval(iv); reject(new Error("Turnstile API not loaded")); }
      }, 50);
    });
  }

  function getResponse() {
    if (!window.turnstile || widgetId === null) return "";
    try { return turnstile.getResponse(widgetId) || ""; } catch { return ""; }
  }

  async function getOrWaitToken(timeoutMs = 7000) {
    const start = Date.now();
    let tok = getResponse();
    if (tok) return tok;
    while (Date.now() - start < timeoutMs) {
      await new Promise(r => setTimeout(r, 150));
      tok = getResponse();
      if (tok) return tok;
    }
    return "";
  }

  // Only render when host element is actually laid out
  function whenVisible(el) {
    return new Promise(resolve => {
      if (el.offsetParent || el.getClientRects().length) return resolve();
      requestAnimationFrame(() => resolve());
    });
  }

  function ensureMinHeight(el) {
    // Prevent visual gap while Turnstile reloads internally
    const computed = getComputedStyle(el);
    if (parseInt(computed.minHeight || "0", 10) < 70) {
      el.style.minHeight = "78px";
    }
  }

  async function renderInto(el, siteKey) {
    await whenVisible(el);
    ensureMinHeight(el);

    const base = { sitekey: siteKey, theme: 'auto', appearance: 'always' };
    const handlers = {
      callback: () => { shrinkBackoff(); cancelPendingReset(); notify('token'); },
      'expired-callback': () => notify('expired'),
      'error-callback': onError
    };

    try {
      widgetId = turnstile.render(el, Object.assign({}, base, handlers, {
        size: 'flexible',
        retry: 'never' // we handle retries/backoff ourselves
      }));
      el.classList.add('cf-flex');
    } catch {
      widgetId = turnstile.render(el, Object.assign({}, base, handlers, {
        size: 'normal',
        retry: 'never'
      }));
      el.classList.remove('cf-flex');
    }
    notify('rendered');
  }

  async function init(siteKey, elementId, dotNetRef) {
    if (rendering) return;
    rendering = true;
    try {
      setDotNet(dotNetRef);

      const el = document.getElementById(elementId);
      if (!el) return;

      if (!online) return; // wait for connectivity
      await waitForTurnstile();

      // Remove existing (route back, etc.)
      if (widgetId !== null) {
        try { turnstile.remove(widgetId); } catch {}
        widgetId = null;
      }

      // If not in viewport yet, render once it is (prevents 300031)
      if ('IntersectionObserver' in window) {
        const obs = new IntersectionObserver((entries) => {
          const e = entries[0];
          if (e && e.isIntersecting) {
            obs.disconnect();
            renderInto(el, siteKey);
          }
        }, { root: null, threshold: 0 });
        obs.observe(el);
      } else {
        await renderInto(el, siteKey);
      }
    } finally {
      rendering = false;
    }
  }

  function scheduleReset() {
    cancelPendingReset();
    pendingResetTimer = setTimeout(() => {
      pendingResetTimer = null;
      if (!online || !visible || widgetId === null || !window.turnstile) return;
      try { turnstile.reset(widgetId); notify('reset'); } catch {}
    }, backoffMs);
  }

  function cancelPendingReset() {
    if (pendingResetTimer !== null) {
      clearTimeout(pendingResetTimer);
      pendingResetTimer = null;
    }
  }

  function tryResume() {
    // If we had recent errors and the page became visible/online again,
    // schedule a gentle reset using the current backoff.
    if (Date.now() - lastErrorAt < backoffResetMs && widgetId !== null) {
      scheduleReset();
    }
  }

  function onError() {
    notify('error');
    lastErrorAt = Date.now();

    // Exponential backoff (up to backoffMax)
    scheduleReset();
    backoffMs = Math.min(backoffMs * 2, backoffMax);

    // If errors stop for a while, shrink backoff automatically
    setTimeout(() => {
      if (Date.now() - lastErrorAt >= backoffResetMs) backoffMs = 3000;
    }, backoffResetMs + 200);
  }

  function shrinkBackoff() {
    // Successful callback: relax backoff
    backoffMs = 3000;
  }

  function reset() {
    cancelPendingReset();
    if (widgetId !== null && window.turnstile) {
      try { turnstile.reset(widgetId); } catch {}
    }
    notify('reset');
  }

  function destroy() {
    cancelPendingReset();
    if (widgetId !== null && window.turnstile) {
      try { turnstile.remove(widgetId); } catch {}
    }
    widgetId = null;
    notify('destroy');
  }

  window.BioMaintLogin = { init, getResponse, getOrWaitToken, reset, destroy, setDotNet };
})();
