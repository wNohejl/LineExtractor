// Popovers and menus: the part of their keyboard and pointer behaviour that needs the DOM.
//
// DeskPopover and DeskMenu keep everything they can in C#, where it can be tested — which
// popover is open, which menu item holds the Tab stop, where an arrow or a typed letter goes.
// What is left is here, because .NET cannot see it: a press that lands outside the panel, an
// Escape pressed while focus is still on the opener, a panel that would run past the bottom of
// the window, and where focus should go once the panel it was in has been removed.
//
// No scrim. The menus this replaced each laid an invisible layer over the desk to catch the
// outside click, which also ate the click — a second menu took two presses to open. Listening
// on the document in the capture phase sees the press without stopping it, so the click that
// dismisses one popover goes on to open the next.
//
// One popover at a time, here as in C#: `current` is the open one, and opening another
// retires its listeners quietly. DeskPopovers tells the .NET side.
//
// Styling is controls.css under "Popover" and "Menu".

const MARGIN = 8;
const TYPEAHEAD_MS = 500;
const NAV = ['ArrowDown', 'ArrowUp', 'Home', 'End'];
const ITEMS = '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]';

let current = null;

/// The element focus goes back to: the one that named itself, or the anchor's first element.
function opener(root) {
    return root.querySelector('[data-popover-return="true"]') ?? root.firstElementChild;
}

function teardown(state) {
    document.removeEventListener('pointerdown', state.onPointerDown, true);
    document.removeEventListener('keydown', state.onKeyDown);
    opener(state.root)?.removeAttribute('aria-controls');

    if (current === state) current = null;
}

/// Opens away from the edge the panel would cross. Measured once, on open: the panel is sized to
/// its content and the desk does not move under an open menu.
///
/// Rectangles and innerHeight are both visual pixels, so the comparison holds under the Window
/// manager's UI zoom without converting anything.
///
/// The edges measured against are the desk window's when the opener sits in one, and the
/// viewport's otherwise (the header's menus). A popover opened from a narrow window's toolbar
/// would otherwise flip towards the viewport's far side and run out of its own window, where
/// the window body clips it.
function flip(panel, root) {
    panel.removeAttribute('data-flip');

    const rect = panel.getBoundingClientRect();
    const anchor = root.getBoundingClientRect();
    const bounds = boundsOf(root);
    const edges = [];

    // Up only when there is more room above than below; a panel taller than both stays down,
    // where it scrolls inside its own max-height.
    if (rect.bottom > bounds.bottom - MARGIN && anchor.top - bounds.top > bounds.bottom - anchor.bottom) {
        edges.push('up');
    }

    if (rect.right > bounds.right - MARGIN) edges.push('end');
    else if (rect.left < bounds.left + MARGIN) edges.push('start');

    if (edges.length) panel.setAttribute('data-flip', edges.join(' '));
}

function boundsOf(root) {
    const host = root.closest('.win__body');

    if (host) return host.getBoundingClientRect();

    return { top: 0, left: 0, right: window.innerWidth, bottom: window.innerHeight };
}

export function open(panel, root, dotNetRef) {
    if (!panel || !root) return;

    if (current) teardown(current);

    const state = { panel, root };

    state.onPointerDown = event => {
        if (root.contains(event.target)) return;

        // Outside: focus goes wherever the press puts it, so nothing is returned to the opener.
        teardown(state);
        dotNetRef?.invokeMethodAsync('OnDismiss');
    };

    // Escape from inside the panel is the menu's, in C#. This catches the one from outside it —
    // the opener still focused after a pointer open, before the menu took focus.
    state.onKeyDown = event => {
        if (event.key !== 'Escape' || panel.contains(event.target)) return;

        event.preventDefault();
        teardown(state);
        opener(root)?.focus({ preventScroll: true });
        dotNetRef?.invokeMethodAsync('OnDismiss');
    };

    document.addEventListener('pointerdown', state.onPointerDown, true);
    document.addEventListener('keydown', state.onKeyDown);

    if (panel.id) opener(root)?.setAttribute('aria-controls', panel.id);

    flip(panel, root);
    current = state;
}

/// The .NET side closed the popover — a choice, Escape in the menu, another popover opening.
/// By now the panel is gone from the DOM, and if focus was in it, focus went with it: the
/// browser parks it on <body>, where the next key does nothing. That is the case where it goes
/// back to the opener. Focus the operator has put somewhere real is left there.
export function close(root) {
    if (!current || current.root !== root) return;

    const state = current;
    teardown(state);

    const active = document.activeElement;
    const lost = !active || active === document.body || !active.isConnected || state.panel.contains(active);

    if (lost) opener(root)?.focus({ preventScroll: true });
}

// ---- Menu ------------------------------------------------------------------------------

const wired = new WeakSet();
const typed = new WeakMap();

function items(menu) {
    return [...menu.querySelectorAll(ITEMS)].filter(item => !item.disabled);
}

function enabled(item) {
    return item.getAttribute('aria-disabled') !== 'true';
}

/// DeskMenuItems are moved by DeskMenu in C#; anything else is moved from here.
function managed(menu) {
    return menu.querySelector('.menu__item') !== null;
}

function rove(list, target) {
    for (const item of list) item.tabIndex = item === target ? 0 : -1;
    target?.focus();
}

function typeahead(menu, list, at, key) {
    const now = performance.now();
    const last = typed.get(menu);
    const run = last && now - last.at <= TYPEAHEAD_MS ? last.text + key : key;
    typed.set(menu, { text: run, at: now });

    // Pressing one letter repeatedly walks the items that start with it.
    const search = (new Set(run).size === 1 ? run[0] : run).toLocaleLowerCase();
    const start = search.length === 1 ? at + 1 : at;

    for (let step = 0; step < list.length; step++) {
        const candidate = list[(start + step) % list.length];
        if (candidate.textContent.trim().toLocaleLowerCase().startsWith(search)) return candidate;
    }

    return null;
}

function onMenuKey(menu, event) {
    if (event.target.closest('[role="menu"]') !== menu) return;

    if (managed(menu)) {
        // C# moves the focus; the browser must not also scroll the panel.
        if (NAV.includes(event.key)) event.preventDefault();
        return;
    }

    const list = items(menu);
    if (!list.length) return;

    const at = Math.max(0, list.indexOf(document.activeElement));
    let next = null;

    switch (event.key) {
        case 'ArrowDown': next = list[(at + 1) % list.length]; break;
        case 'ArrowUp': next = list[(at - 1 + list.length) % list.length]; break;
        case 'Home': next = list[0]; break;
        case 'End': next = list[list.length - 1]; break;
        default:
            if (event.key.length === 1 && event.key.trim() && !event.ctrlKey && !event.metaKey && !event.altKey) {
                next = typeahead(menu, list, at, event.key);
            }
    }

    if (!next) return;

    event.preventDefault();
    rove(list, next);
}

/// Wires a menu once, on its first render. A menu of foreign items gets its roving tabindex and
/// its first focus from here; a menu of DeskMenuItems has both from C#.
export function menu(element) {
    if (!element || wired.has(element)) return;

    wired.add(element);
    element.addEventListener('keydown', event => onMenuKey(element, event));

    if (!managed(element)) {
        const list = items(element);
        rove(list, list.find(enabled) ?? list[0]);
    }
}
