// Dragging for floating dialogs.
//
// Same reasoning as windowing.js: Blazor Server runs over a SignalR circuit, so a
// @onpointermove handler would put a network round trip inside every frame of a drag and the
// dialog would visibly lag the cursor. The element's own style is mutated at pointer rate and
// .NET is told once, on pointer-up, where it ended up.
//
// Dialogs differ from desk windows in one way that matters: they may be dragged anywhere,
// which is the whole reason they exist (ADR 0007 — arbitrary placement is what modals and
// popovers are for). They are still kept on the desk — the area between the header and the
// footer — because a dialog dragged under the chrome is a dialog whose title bar, and so its
// close key, you cannot reach.

const MARGIN = 8;
const STEP = 26;

const placed = new WeakMap();

// Under the Window manager's UI scale (CSS zoom on <html>) a rectangle, a pointer's clientX and
// the window's innerWidth are visual pixels, while style.left and offsetWidth are layout pixels.
// Everything below is worked out in layout pixels — the space the dialog is positioned in — by
// dividing the visual measurements by the zoom at the dialog.
function zoomOf(element) {
    return element?.currentCSSZoom || 1;
}

/// The rectangle a dialog may occupy, in layout pixels: the desk between the header and the
/// footer, or the viewport when there is no desk (a test page, a phone).
function deskOf(dialog) {
    const zoom = zoomOf(dialog);
    const desk = document.querySelector('.desk')?.getBoundingClientRect();
    const box = desk ?? { top: 0, left: 0, right: window.innerWidth, bottom: window.innerHeight };

    return {
        top: box.top / zoom + MARGIN,
        left: box.left / zoom + MARGIN,
        right: box.right / zoom - MARGIN,
        bottom: box.bottom / zoom - MARGIN
    };
}

/// Keeps a dialog inside the desk: as tall and as wide as the desk at most, its body scrolling
/// for the rest, and moved back in by as much as it has grown or the window has shrunk. Sizes
/// are layout pixels (offsetWidth/offsetHeight), which the open animation's scale leaves alone.
function fit(dialog) {
    if (!dialog?.isConnected) return;

    const area = deskOf(dialog);

    dialog.style.maxHeight = `${Math.max(120, area.bottom - area.top)}px`;
    dialog.style.maxWidth = `${Math.max(240, area.right - area.left)}px`;

    const width = dialog.offsetWidth;
    const height = dialog.offsetHeight;
    const left = parseFloat(dialog.style.left) || area.left;
    const top = parseFloat(dialog.style.top) || area.top;

    dialog.style.left = `${Math.max(area.left, Math.min(left, area.right - width))}px`;
    dialog.style.top = `${Math.max(area.top, Math.min(top, area.bottom - height))}px`;
}

function refitAll() {
    for (const dialog of document.querySelectorAll('.deskdialog')) fit(dialog);
}

window.addEventListener('resize', refitAll, { passive: true });

export function attachDrag(handle, dialog, dotNetRef) {
    if (!handle || !dialog) return;

    handle.addEventListener('pointerdown', event => beginDrag(event, handle, dialog, dotNetRef));
}

function beginDrag(event, handle, dialog, dotNetRef) {
    // Left button only, and never from a control inside the title bar.
    if (event.button !== 0 || event.target.closest('button')) return;

    event.preventDefault();

    // preventDefault stops the text selection a drag would otherwise make, and with it the
    // focus a press would have given: grabbing a dialog's bar has to leave it focused, or Escape
    // lands on the page instead of the dialog the operator is holding.
    dialog.focus({ preventScroll: true });

    const zoom = zoomOf(dialog);
    const start = dialog.getBoundingClientRect();
    const offsetX = (event.clientX - start.left) / zoom;
    const offsetY = (event.clientY - start.top) / zoom;

    dialog.classList.add('deskdialog--dragging');
    document.body.classList.add('dialog-dragging');

    const area = deskOf(dialog);

    const move = e => {
        const maxLeft = area.right - dialog.offsetWidth;
        const maxTop = area.bottom - dialog.offsetHeight;

        const left = Math.min(Math.max(area.left, e.clientX / zoom - offsetX), Math.max(area.left, maxLeft));
        const top = Math.min(Math.max(area.top, e.clientY / zoom - offsetY), Math.max(area.top, maxTop));

        dialog.style.left = `${left}px`;
        dialog.style.top = `${top}px`;
    };

    const end = () => {
        window.removeEventListener('pointermove', move);
        window.removeEventListener('pointerup', end);
        window.removeEventListener('pointercancel', end);

        dialog.classList.remove('deskdialog--dragging');
        document.body.classList.remove('dialog-dragging');

        // One call, with the final rectangle. The DOM has been ahead of the .NET model for the
        // whole gesture, which is safe because nothing else writes these properties meanwhile.
        dotNetRef?.invokeMethodAsync('OnMoved', dialog.offsetLeft, dialog.offsetTop);
    };

    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', end);
    window.addEventListener('pointercancel', end);

    // After the listeners, and inside a try: called first, a throw on a stale pointer id
    // aborts the gesture once the element has already taken its dragging class, and the dialog
    // latches into a drag it cannot perform. This was a real bug in the window manager.
    try {
        handle.setPointerCapture(event.pointerId);
    } catch {
        /* capture is an optimisation; the window listeners above carry the gesture regardless */
    }
}

/// After a dialog closes, gives focus to the frontmost one still open — so Escape, pressed again,
/// closes the next dialog down instead of landing on the page.
///
/// Waits for the closing dialog to leave the DOM: .NET disposes it around the render batch that
/// removes it, in no guaranteed order, and while it is still there focus is still inside it.
/// Leaves focus alone if the operator has since put it somewhere real, or no dialog is left.
export function focusFrontAfter(closing) {
    const started = performance.now();

    const tick = () => {
        if (closing?.isConnected && performance.now() - started < 1000) {
            requestAnimationFrame(tick);
            return;
        }

        const active = document.activeElement;
        if (active && active !== document.body && active.isConnected && !closing?.contains(active)) return;

        const front = [...document.querySelectorAll('.deskdialog')]
            .filter(d => d !== closing)
            .sort((a, b) => (parseInt(b.style.zIndex) || 0) - (parseInt(a.style.zIndex) || 0))[0];

        front?.focus({ preventScroll: true });
    };

    requestAnimationFrame(tick);
}

/// Places a new dialog over the window it was opened from, the way a macOS sheet hangs from
/// its window's title bar (HIG, Sheets): centred on that window, just under its bar, so the
/// operator sees which window asked for it. A second dialog from the same window cascades a
/// step down and across rather than covering the first exactly.
///
/// It was a fixed spot — 120 across, 90 down — whatever window opened it, with a viewport-high
/// cap: a wizard launched from the right-hand window landed over the left one, and its foot
/// fell under the footer. Now it is sized to the desk, its body scrolls, and it keeps fitting
/// as its content arrives: the wager wizard's steps load after the dialog is drawn, and a
/// ResizeObserver moves the dialog up by however much it grew past the desk's foot.
export function place(dialog, index) {
    if (!dialog) return;

    const zoom = zoomOf(dialog);
    const area = deskOf(dialog);
    const host = dialog.closest('.win');
    const hostBox = host?.getBoundingClientRect();
    const barBox = host?.querySelector('.win__bar')?.getBoundingClientRect();

    // Sized before it is placed, so the centring uses the width it will actually have.
    dialog.style.maxHeight = `${Math.max(120, area.bottom - area.top)}px`;
    dialog.style.maxWidth = `${Math.max(240, area.right - area.left)}px`;

    const width = dialog.offsetWidth;
    const offset = index * STEP;

    const left = hostBox
        ? (hostBox.left + hostBox.width / 2) / zoom - width / 2 + offset
        : area.left + offset;
    const top = barBox
        ? barBox.bottom / zoom + MARGIN + offset
        : area.top + offset;

    dialog.style.left = `${left}px`;
    dialog.style.top = `${top}px`;
    fit(dialog);

    if (!placed.has(dialog) && 'ResizeObserver' in window) {
        const observer = new ResizeObserver(() => fit(dialog));
        observer.observe(dialog);
        placed.set(dialog, observer);
    }
}

/// Kept for callers that still say cascade: the same placement.
export const cascade = place;

/// Stops watching a dialog that is closing.
export function release(dialog) {
    placed.get(dialog)?.disconnect();
    placed.delete(dialog);
}
