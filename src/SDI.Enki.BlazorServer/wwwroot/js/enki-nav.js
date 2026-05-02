// Navigation feedback — two layers:
//   1. Top-bar cyan sliver (#enki-nav-progress) — always on, subtle peripheral indicator
//   2. Center-screen ring overlay (#enki-loading-overlay) — deliberate "something's happening"
//      for heavier page transitions, fades in after a short delay to avoid flashing on fast navs
//
// Both are controlled by the same two triggers:
//   - add class on same-origin link click / form submit
//   - remove class on Blazor's enhancedload event (or pageshow as a fallback)
//
// Pattern is rendering-mode agnostic — works for static SSR, InteractiveServer,
// and full page refreshes.

(() => {
    const bar     = () => document.getElementById('enki-nav-progress');
    const overlay = () => document.getElementById('enki-loading-overlay');

    // Small delay before showing the overlay — if the nav completes in
    // under ~180ms we never show it, avoiding a jarring flash for fast
    // transitions. The top-bar sliver still fires immediately.
    let overlayTimer = null;
    const OVERLAY_DELAY = 180;

    const start = () => {
        bar()?.classList.add('is-loading');
        clearTimeout(overlayTimer);
        overlayTimer = setTimeout(() => {
            overlay()?.classList.add('is-loading');
        }, OVERLAY_DELAY);
    };

    const stop = () => {
        clearTimeout(overlayTimer);
        bar()?.classList.remove('is-loading');
        overlay()?.classList.remove('is-loading');
    };

    document.addEventListener('click', (e) => {
        const a = e.target.closest('a[href]');
        if (!a) return;
        if (a.hasAttribute('download')) return;
        if (a.target && a.target !== '_self') return;
        try {
            const url = new URL(a.href, location.href);
            if (url.origin !== location.origin) return;
            // Anchor link on the same page — no nav happens.
            if (url.pathname === location.pathname && url.hash) return;
        } catch { return; }
        start();
    }, true);

    document.addEventListener('submit', (e) => {
        if (!(e.target instanceof HTMLFormElement)) return;
        start();

        // Interactive Blazor forms (@rendermode InteractiveServer +
        // <EditForm OnValidSubmit="...">) call preventDefault on the
        // submit event so no real HTTP POST happens — OnValidSubmit
        // runs in the circuit and the page just re-renders. Without
        // an enhancedload / pageshow event, our stop() never fires
        // and the overlay sticks. Catch that case by checking
        // defaultPrevented and hide the overlay if so.
        //
        // Why setTimeout(0) and not queueMicrotask: for InteractiveServer
        // EditForm, Blazor's preventDefault doesn't run synchronously
        // inside its event listener — it runs in a microtask scheduled
        // *after* ours, so a queueMicrotask check fires too early and
        // sees defaultPrevented=false. setTimeout(0) defers to the next
        // task tick, by which point all microtasks (including Blazor's
        // continuation) have drained and defaultPrevented reflects the
        // final state. This was issue #34 — Tubular/Formation/
        // CommonMeasure validation 400s left the spinner stuck because
        // we hid the overlay too early to ever see the prevent.
        setTimeout(() => {
            if (e.defaultPrevented) stop();
        }, 0);
    }, true);

    // Blazor Web App enhanced navigation — fires on every nav-complete.
    if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
        Blazor.addEventListener('enhancedload', stop);
    }
    // Covers full page refreshes and bfcache restores.
    window.addEventListener('pageshow', stop);
})();
