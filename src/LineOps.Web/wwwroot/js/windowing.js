// Two gestures on the desk, deliberately on two different hit targets so neither is ever
// triggered by accident:
//
//   - drag a TAB (a window's title bar)  -> reorders the row
//   - drag a DIVIDER (the gutter between two expanded columns) -> resizes the row
//
// Both run entirely in the browser and tell .NET only the final result, on pointerup.
// Blazor Server runs over a SignalR circuit, so routing every pointermove through it would
// put a network round trip inside each frame and the interaction would visibly lag the
// cursor. The DOM is therefore ahead of the .NET model mid-gesture; nothing else writes
// these properties during a drag, so the eventual commit simply reconciles them.

let dotNet = null;

export function initialise(dotNetRef) {
    dotNet = dotNetRef;
    window.addEventListener('resize', reportViewport, { passive: true });
    reportViewport();
}

export function reportViewport() {
    const desk = document.querySelector('[data-desk]');
    if (!desk || !dotNet) return;

    const rect = desk.getBoundingClientRect();
    dotNet.invokeMethodAsync('OnViewportChanged', rect.width, rect.height);
}

// ---------------------------------------------------------------- divider (resize) ----

/** Wires one divider. Safe to call repeatedly — rebinds cleanly on re-render. */
export function attachDivider(handle, leftId, rightId) {
    if (!handle) return;

    detachDivider(handle);

    const onDown = (e) => beginSplit(e, handle, leftId, rightId);
    handle.addEventListener('pointerdown', onDown);
    handle.__lineopsSplit = onDown;
}

export function detachDivider(handle) {
    if (!handle || !handle.__lineopsSplit) return;

    handle.removeEventListener('pointerdown', handle.__lineopsSplit);
    handle.__lineopsSplit = null;
}

function beginSplit(event, handle, leftId, rightId) {
    if (event.button !== 0) return;

    const left = document.getElementById(`win-${leftId}`);
    const right = document.getElementById(`win-${rightId}`);
    if (!left || !right) return;

    event.preventDefault();

    const startX = event.clientX;
    const leftStart = left.offsetWidth;
    const rightStart = right.offsetWidth;
    const rightOrigin = parseFloat(right.style.left) || 0;

    const leftMin = parseFloat(left.dataset.minWidth) || 260;
    const rightMin = parseFloat(right.dataset.minWidth) || 260;

    // Neither column may cross the other's minimum, so the travel is bounded up front
    // rather than clamped per-frame — the divider simply stops where it must.
    const lowerBound = leftMin - leftStart;
    const upperBound = rightStart - rightMin;

    handle.classList.add('divider--active');
    document.body.classList.add('splitting');

    const onMove = (e) => {
        const delta = Math.min(Math.max(e.clientX - startX, lowerBound), upperBound);

        left.style.width = `${leftStart + delta}px`;
        right.style.left = `${rightOrigin + delta}px`;
        right.style.width = `${rightStart - delta}px`;
        handle.style.transform = `translateX(${delta}px)`;
    };

    const onUp = () => {
        handle.removeEventListener('pointermove', onMove);
        handle.removeEventListener('pointerup', onUp);
        handle.removeEventListener('pointercancel', onUp);

        handle.classList.remove('divider--active');
        document.body.classList.remove('splitting');

        // The server recomputes every column from weights, which will place this handle
        // again — drop the visual offset so it does not double up.
        handle.style.transform = '';

        if (dotNet) {
            dotNet.invokeMethodAsync(
                'OnSplitCommitted', leftId, rightId, left.offsetWidth, right.offsetWidth);
        }
    };

    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onUp);
    handle.addEventListener('pointercancel', onUp);

    capture(handle, event.pointerId);
}

// ------------------------------------------------------------------ tabs (reorder) ----

const DRAG_THRESHOLD = 6;

/**
 * Wires every window's title bar inside the desk as a reorder handle. Idempotent — each
 * handle is marked once it is wired, so calling this again after a render only picks up
 * newly-opened windows.
 */
export function attachAllTabs(containerSelector) {
    const container = document.querySelector(containerSelector);
    if (!container) return;

    container.querySelectorAll('.win').forEach((win) => {
        const handle = win.querySelector('[data-tab-handle]');
        if (!handle || handle.__lineopsTabWired) return;

        handle.__lineopsTabWired = true;
        handle.addEventListener('pointerdown', (e) => beginTabDrag(e, win, handle));
    });
}

function beginTabDrag(event, win, handle) {
    // Only the primary button, and never when the press started on a title-bar control
    // (minimise/maximise/close) — those are clicks, not drags.
    if (event.button !== 0 || event.target.closest('[data-window-control]')) return;

    const desk = document.querySelector('[data-desk]');
    if (!desk) return;

    const startX = event.clientX;
    const originLeft = parseFloat(win.style.left) || 0;
    const width = win.offsetWidth;
    const deskRect = desk.getBoundingClientRect();

    let dragging = false;

    const onMove = (e) => {
        const dx = e.clientX - startX;

        if (!dragging) {
            if (Math.abs(dx) < DRAG_THRESHOLD) return;

            // Past the threshold: this is a drag, not a click. Lift the tab so it visibly
            // floats in front of whatever it passes over while it looks for a new slot.
            dragging = true;
            win.classList.add('win--dragging-tab');
            document.body.classList.add('tab-dragging');
        }

        const min = 0;
        const max = Math.max(min, deskRect.width - width);
        win.style.left = `${Math.min(Math.max(originLeft + dx, min), max)}px`;
    };

    const onUp = (e) => {
        handle.removeEventListener('pointermove', onMove);
        handle.removeEventListener('pointerup', onUp);
        handle.removeEventListener('pointercancel', onUp);

        if (!dragging)
            return;

        win.classList.remove('win--dragging-tab');
        document.body.classList.remove('tab-dragging');

        const pointerCenter = e.clientX;
        const orderedIds = computeDropOrder(desk, win.id, pointerCenter);

        if (dotNet && orderedIds) {
            dotNet.invokeMethodAsync('OnReordered', orderedIds);
        }
    };

    handle.addEventListener('pointermove', onMove);
    handle.addEventListener('pointerup', onUp);
    handle.addEventListener('pointercancel', onUp);

    capture(handle, event.pointerId);
}

/**
 * Every other tab's position is read live from the DOM — they never moved during the drag,
 * only the dragged one did — and the dragged id is spliced back in wherever the pointer
 * ended up relative to their midpoints.
 */
function computeDropOrder(desk, draggedElementId, pointerCenterX) {
    const siblings = [...desk.querySelectorAll('.win')].filter((el) => el.id !== draggedElementId);

    let index = siblings.length;

    for (let i = 0; i < siblings.length; i++) {
        const rect = siblings[i].getBoundingClientRect();
        const center = rect.left + rect.width / 2;

        if (pointerCenterX < center) {
            index = i;
            break;
        }
    }

    const orderedElementIds = siblings.map((el) => el.id);
    orderedElementIds.splice(index, 0, draggedElementId);

    // Strip the "win-" prefix the DOM id carries back down to the manager's own id.
    return orderedElementIds.map((id) => id.replace(/^win-/, ''));
}

// ---------------------------------------------------------------------------- shared ----

/**
 * Pointer capture keeps a drag alive when the cursor outruns its small handle, which it
 * routinely does. Attempted after the move listeners are registered and never allowed to
 * throw: on a stale or synthetic pointer id it raises NotFoundError, and if that propagated
 * it would abort the gesture right after the element took its "dragging" class — it would
 * look like it started and then refuse to move.
 */
function capture(element, pointerId) {
    try {
        element.setPointerCapture(pointerId);
    } catch {
        // Without capture the gesture still works while the cursor stays over the handle.
    }
}
