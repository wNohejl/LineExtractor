// Dragging for floating dialogs.
//
// Same reasoning as windowing.js: Blazor Server runs over a SignalR circuit, so a
// @onpointermove handler would put a network round trip inside every frame of a drag and the
// dialog would visibly lag the cursor. The element's own style is mutated at pointer rate and
// .NET is told once, on pointer-up, where it ended up.
//
// Dialogs differ from desk windows in one way that matters: they may be dragged anywhere,
// which is the whole reason they exist (ADR 0007 — arbitrary placement is what modals and
// popovers are for). They are still clamped to the viewport, because a dialog dragged off the
// edge is a dialog you cannot close.

const MARGIN = 8;

export function attachDrag(handle, dialog, dotNetRef) {
    if (!handle || !dialog) return;

    handle.addEventListener('pointerdown', event => beginDrag(event, handle, dialog, dotNetRef));
}

function beginDrag(event, handle, dialog, dotNetRef) {
    // Left button only, and never from a control inside the title bar.
    if (event.button !== 0 || event.target.closest('button')) return;

    event.preventDefault();

    const start = dialog.getBoundingClientRect();
    const offsetX = event.clientX - start.left;
    const offsetY = event.clientY - start.top;

    dialog.classList.add('deskdialog--dragging');
    document.body.classList.add('dialog-dragging');

    const move = e => {
        const maxLeft = window.innerWidth - dialog.offsetWidth - MARGIN;
        const maxTop = window.innerHeight - dialog.offsetHeight - MARGIN;

        const left = Math.min(Math.max(MARGIN, e.clientX - offsetX), Math.max(MARGIN, maxLeft));
        const top = Math.min(Math.max(MARGIN, e.clientY - offsetY), Math.max(MARGIN, maxTop));

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

/// Places a new dialog without covering the last one exactly.
export function cascade(dialog, index) {
    if (!dialog) return;

    const step = 26;
    const left = Math.min(120 + index * step, Math.max(MARGIN, window.innerWidth - dialog.offsetWidth - MARGIN));
    const top = Math.min(90 + index * step, Math.max(MARGIN, window.innerHeight - dialog.offsetHeight - MARGIN));

    dialog.style.left = `${left}px`;
    dialog.style.top = `${top}px`;
}
