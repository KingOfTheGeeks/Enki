// Real-time form validation — fires on focus-leave (blur) and re-fires on
// further input once a field has been marked invalid.
//
// Pattern:
//   - HTML5 validation attributes (required, pattern, type=email, maxlength)
//     on the input itself do the actual checking via input.checkValidity().
//   - We only add/remove the `.is-user-invalid` class and a sibling
//     `.enki-field-msg` span with the error text. CSS in enki-theme.css
//     styles those.
//   - Inputs can carry a `data-error` attribute with a custom message;
//     otherwise we fall back to input.validationMessage from the browser.
//
// Works with Blazor static SSR (no interactivity required) and rehooks on
// every enhanced navigation so newly-loaded forms are also watched.

(() => {
    const FIELD_SELECTOR = 'input.enki-form-input, textarea.enki-form-textarea';

    const render = (input) => {
        const parent = input.parentElement;
        if (!parent) return;
        let msg = parent.querySelector(':scope > .enki-field-msg');

        if (!input.checkValidity()) {
            input.classList.add('is-user-invalid');
            if (!msg) {
                msg = document.createElement('div');
                msg.className = 'enki-field-msg enki-form-error';
                input.insertAdjacentElement('afterend', msg);
            }
            msg.textContent = input.dataset.error || input.validationMessage;
        } else {
            input.classList.remove('is-user-invalid');
            msg?.remove();
        }
    };

    const wire = (input) => {
        if (input.dataset.enkiWired === '1') return;
        input.dataset.enkiWired = '1';

        // Initial check: fire on blur.
        input.addEventListener('blur', () => render(input));
        // Once dirty: update live on each keystroke so the user sees the
        // error clear the moment they type something valid.
        input.addEventListener('input', () => {
            if (input.classList.contains('is-user-invalid')) render(input);
        });
    };

    const wireAll = () => {
        document.querySelectorAll(FIELD_SELECTOR).forEach(wire);
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', wireAll);
    } else {
        wireAll();
    }

    // Rewire on enhanced navigation — new page, new inputs.
    if (typeof Blazor !== 'undefined' && Blazor.addEventListener) {
        Blazor.addEventListener('enhancedload', wireAll);
    }
})();
