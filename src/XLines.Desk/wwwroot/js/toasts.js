// The toast stack's focus (DeskToastStack.razor). A notice dismissed from the keyboard takes its
// key with it, and a focused element that leaves the page leaves focus on <body>. So the stack
// remembers where focus came from when it entered, and when the key under focus goes it moves
// focus on: to the next notice's dismiss key, or back where it came from when none is left.

const cameFrom = new WeakMap();

export function watch(section) {
    if (!section || section.__deskToasts) return;
    section.__deskToasts = true;

    // Which focus the component hears (DeskToastStack's @onfocusin and @onfocusout, which Blazor
    // hears at the document, after this). Focus the keyboard put on a key holds the stack and
    // fans it; focus a pointer's press put there is only the browser focusing what it clicked, and
    // is not passed on, so it neither holds the clocks nor fans the stack once the pointer leaves.
    // A focus this script moves itself (refocus, after a key pressed from the keyboard) counts as
    // the keyboard's whatever the browser decides to show.
    section.addEventListener('focusin', (e) => {
        const from = e.relatedTarget;
        if (from instanceof HTMLElement && !section.contains(from)) cameFrom.set(section, from);

        const moved = section.__deskToastsMoving;
        section.__deskToastsMoving = false;
        if (!moved && !shown(e.target)) e.stopPropagation();
    });

    // Tab from key to key inside the stack is a focusout and then a focusin. Heard as two, the
    // stack let go between them — folding, and spending an Escape's fold — and fanned again a
    // moment later. A focusout whose next stop is still inside the stack is no change, and goes
    // no further.
    section.addEventListener('focusout', (e) => {
        if (e.relatedTarget instanceof Node && section.contains(e.relatedTarget)) e.stopPropagation();
    });

    // Escape that folds the fanned stack is the stack's: the document-level Escape of an open
    // popover elsewhere (popover.js) reads defaultPrevented and leaves its panel open. Blazor
    // still hears the key, since nothing stops it going up.
    // Folding hides every notice but the front one, so focus on an older notice's key would fall
    // to <body>: it goes to the count's key, which folded the stack and unfolds it again.
    section.addEventListener('keydown', (e) => {
        if (e.key !== 'Escape' || !section.classList.contains('toasts--fanned')) return;

        e.preventDefault();

        const at = document.activeElement instanceof Element ? document.activeElement.closest('.toast') : null;
        if (at && section.contains(at) && !at.classList.contains('toast--front')) {
            const count = section.querySelector('.toast__count');
            if (count instanceof HTMLElement) {
                section.__deskToastsMoving = true;
                count.focus();
                section.__deskToastsMoving = false;
            }
        }
    });

    // The fanned list scrolls past half the screen. It fans with the front notice in view: the
    // newest is at the top, and a list left scrolled down on its last visit would open on the
    // oldest.
    if (typeof MutationObserver === 'function') {
        let fanned = section.classList.contains('toasts--fanned');
        new MutationObserver(() => {
            const now = section.classList.contains('toasts--fanned');
            if (now && !fanned) {
                const list = section.querySelector('.toasts__list');
                if (list) list.scrollTop = 0;
            }
            fanned = now;
        }).observe(section, { attributes: true, attributeFilter: ['class'] });
    }
}

/// Whether the browser shows focus on the element: the keyboard's focus, not a pointer's.
function shown(el) {
    try {
        return el instanceof Element && el.matches(':focus-visible');
    } catch {
        return true; // a browser without :focus-visible: every focus counts, as before
    }
}

export function refocus(section, id) {
    if (!section) return;

    // A key whose action put focus somewhere real — a window it opened, a field it focused — has
    // said where focus goes; only focus that fell to <body> with the key, or is still on the stack,
    // is moved on.
    const active = document.activeElement;
    if (active && active !== document.body && active.isConnected && !section.contains(active)) return;

    const next = id == null ? null : section.querySelector(`[data-toast-id="${id}"] .toast__dismiss`);
    if (next) {
        section.__deskToastsMoving = true;
        next.focus();
        section.__deskToastsMoving = false;
        return;
    }

    const back = cameFrom.get(section);
    if (back && back.isConnected && !back.closest('[inert]')) {
        back.focus();
        return;
    }

    // Nowhere to go back to: the header's first key (the wordmark's menu), where Tab starts;
    // failing that, the desk, which takes programmatic focus (tabindex="-1", Desk.razor).
    const key = document.querySelector('.hdr button:not([disabled])');
    if (key instanceof HTMLElement && !key.closest('[inert]') && !section.contains(key)) {
        key.focus();
        return;
    }

    const desk = document.querySelector('[data-desk]');
    if (desk instanceof HTMLElement) desk.focus();
}
