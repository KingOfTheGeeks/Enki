// Thin wrapper around the native <dialog> element so Razor pages can open
// and close modals from a Blazor click handler without each page rolling
// its own JS interop. Use `enkiDialog.open(id)` / `enkiDialog.close(id)`.
//
// Why a module: page authors should not be invoking
// `document.getElementById(...).showModal()` directly via JS.InvokeVoidAsync —
// that scatters DOM-ID literals across .razor + .cs and makes the contract
// hard to find. One named entry point keeps the surface obvious.

(() => {
    const find = (id) => {
        const el = document.getElementById(id);
        if (!el || !(el instanceof HTMLDialogElement)) {
            console.warn(`enkiDialog: no <dialog id="${id}"> found`);
            return null;
        }
        return el;
    };

    window.enkiDialog = {
        open: (id) => { find(id)?.showModal(); },
        close: (id) => { find(id)?.close(); },
    };
})();
