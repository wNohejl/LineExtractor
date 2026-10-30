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
//
// The viewport is reported on the same principle. A browser window dragged wider fires resize
// several times a frame, and each report is a trip over the circuit and a relayout in .NET, so
// resizing is coalesced to one report a frame (onResize, below).

let dotNet = null;

export function initialise(dotNetRef) {
    dotNet = dotNetRef;
    window.addEventListener('resize', onResize, { passive: true });
    window.removeEventListener('keydown', deskHotkeys);
    window.addEventListener('keydown', deskHotkeys);
    reportViewport();
}

// The desk's chords, in one handler. This file only recognises them; what each one does — which
// window moves, what opens, whether there is anything to undo — is decided in Desk.razor, where
// a test can reach it.
//
//   Ctrl/Cmd+K             command palette   (anywhere, fields included)
//   Ctrl/Cmd+,             settings          (anywhere, fields included)
//   Ctrl/Cmd+Z             undo              (not in a field)
//   Ctrl/Cmd+Shift+Z, Ctrl+Y  redo           (not in a field)
//   Ctrl+Alt+Left/Right    move the focused window one place (not in a field)
//   Ctrl/Cmd+[ and ]       the focused window's Back and Forward (not in a field), as Finder
//                          and Safari have them; the keys on each window's bar do the same
//
// The palette and settings work from inside a field because that is where a hand already on the
// keyboard usually is, and neither chord means anything to a field. Undo and the arrows do — a
// field's own undo, a word-jump — so in a field they are left to it, unprevented. The browser's
// own uses of Ctrl+K (address-bar search) and Ctrl+, (nothing, in most) are the ones given up.
function deskHotkeys(e) {
    if (e.defaultPrevented || !dotNet) return;

    const mod = e.ctrlKey || e.metaKey;
    if (!mod) return;

    const key = e.key.length === 1 ? e.key.toLowerCase() : e.key;

    if (!e.altKey && !e.shiftKey && key === 'k') {
        return send(e, 'OnPaletteRequested');
    }

    if (!e.altKey && !e.shiftKey && key === ',') {
        return send(e, 'OnSettingsRequested');
    }

    if (inField(e.target)) return;

    if (!e.altKey && !e.shiftKey && key === 'z') {
        return send(e, 'OnUndoRequested');
    }

    if (!e.altKey && ((e.shiftKey && key === 'z') || (e.ctrlKey && !e.shiftKey && key === 'y'))) {
        return send(e, 'OnRedoRequested');
    }

    if (e.ctrlKey && e.altKey && !e.shiftKey && !e.metaKey
        && (key === 'ArrowLeft' || key === 'ArrowRight')) {
        return send(e, 'OnMoveWindowRequested', key === 'ArrowLeft' ? -1 : 1);
    }

    if (!e.altKey && !e.shiftKey && (key === '[' || key === ']')) {
        return send(e, 'OnPageRequested', key === '[' ? -1 : 1);
    }
}

function send(e, method, ...args) {
    e.preventDefault();
    dotNet.invokeMethodAsync(method, ...args);
}

function inField(target) {
    return target instanceof Element
        && (target.isContentEditable || target.closest('input, textarea, select') !== null);
}

// The desk is laid out in layout pixels, and under the UI scale (CSS zoom on <html>) that is not
// what getBoundingClientRect or a pointer's clientX report — both come back in visual pixels,
// while offsetWidth and anything written to style.left are layout pixels. Every place below that
// turns a measured rectangle or a pointer distance into a width or a position divides by this.
// currentCSSZoom is the zoom in effect at the element, whoever set it, so a browser's own zoom
// on top (Ctrl +/−) does not enter into it; engines without it have no CSS zoom to undo.
function zoomOf(element) {
    return element?.currentCSSZoom || 1;
}

export function reportViewport() {
    const desk = document.querySelector('[data-desk]');
    if (!desk || !dotNet) return;

    const rect = desk.getBoundingClientRect();
    const zoom = zoomOf(desk);
    dotNet.invokeMethodAsync('OnViewportChanged', rect.width / zoom, rect.height / zoom);
}

// One report a frame, however many resize events arrive before it. The first event of a burst
// asks for a frame and the rest find it already asked for; the desk is measured when the frame
// comes, after every event in the burst, so the size reported is the last one — nothing is
// dropped from the trailing edge. An event after that frame asks for the next. A hidden tab gets
// no frames, so a resize there is reported on the first frame it is shown, before it is seen.
// applyScale does not come through here: a scale is one change, and is reported at once.
let resizePending = false;

function onResize() {
    if (resizePending) return;
    resizePending = true;

    requestAnimationFrame(() => {
        resizePending = false;
        reportViewport();
    });
}

// ------------------------------------------------------------------ scale (apply) ----

// The Window manager's two scales, set where the stylesheet reads them (desk.css, "Scale").
// The desk's width in layout pixels changes with the UI scale, so the viewport is reported again
// straight away rather than waiting for a resize that is not coming. Anything else that measured
// the page — desk-glide.js's marble — hears desk:rescaled, since no DOM changed for it to see.
export function applyScale(ui, text) {
    const root = document.documentElement.style;
    root.setProperty('--ui-scale', String(ui));
    root.setProperty('--text-scale', String(text));
    reportViewport();
    window.dispatchEvent(new Event('desk:rescaled'));
}

// ---------------------------------------------------------------- layout (persist) ----

// The desk's layout lives in localStorage so a reload, or a restarted server, puts the row
// back as it was. Guarded like the theme: storage throws outright in a private window or under
// blocked site data, and a forgotten layout is not worth a broken desk.

export function loadLayout(key) {
    try {
        return localStorage.getItem(key);
    } catch {
        return null;
    }
}

export function saveLayout(key, json) {
    try {
        localStorage.setItem(key, json);
    } catch {
        /* forgotten, not fatal */
    }
}

// ---------------------------------------------------------------- divider (resize) ----

/** Wires one divider. Safe to call repeatedly — rebinds cleanly on re-render. */
export function attachDivider(handle, leftId, rightId) {
    if (!handle) return;

    detachDivider(handle);

    const onDown = (e) => beginSplit(e, handle, leftId, rightId);
    handle.addEventListener('pointerdown', onDown);
    handle.__deskSplit = onDown;
}

export function detachDivider(handle) {
    if (!handle || !handle.__deskSplit) return;

    handle.removeEventListener('pointerdown', handle.__deskSplit);
    handle.__deskSplit = null;
}

function beginSplit(event, handle, leftId, rightId) {
    if (event.button !== 0) return;

    const left = document.getElementById(`win-${leftId}`);
    const right = document.getElementById(`win-${rightId}`);
    if (!left || !right) return;

    event.preventDefault();

    const startX = event.clientX;
    const zoom = zoomOf(handle);
    const leftStart = left.offsetWidth;
    const rightStart = right.offsetWidth;
    const rightOrigin = parseFloat(right.style.left) || 0;

    const leftMin = parseFloat(left.dataset.minWidth) || 260;
    const rightMin = parseFloat(right.dataset.minWidth) || 260;

    // Neither column may cross the other's minimum, so the travel is bounded up front
    // rather than clamped per-frame — the divider simply stops where it must. On a desk too
    // narrow for both minimums the columns already sit under them; there a column may grow
    // but not shrink further, rather than the bounds crossing and the divider jumping.
    const lowerBound = Math.min(leftMin - leftStart, 0);
    const upperBound = Math.max(rightStart - rightMin, 0);

    handle.classList.add('divider--active');
    document.body.classList.add('splitting');

    const onMove = (e) => {
        const delta = Math.min(Math.max((e.clientX - startX) / zoom, lowerBound), upperBound);

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
        if (!handle || handle.__deskTabWired) return;

        handle.__deskTabWired = true;
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
    const zoom = zoomOf(win);
    const originLeft = parseFloat(win.style.left) || 0;
    const width = win.offsetWidth;
    const deskWidth = desk.getBoundingClientRect().width / zoom;

    let dragging = false;

    const onMove = (e) => {
        const dx = (e.clientX - startX) / zoom;

        if (!dragging) {
            if (Math.abs(e.clientX - startX) < DRAG_THRESHOLD) return;

            // Past the threshold: this is a drag, not a click. Lift the tab so it visibly
            // floats in front of whatever it passes over while it looks for a new slot.
            dragging = true;
            win.classList.add('win--dragging-tab');
            document.body.classList.add('tab-dragging');
        }

        const min = 0;
        const max = Math.max(min, deskWidth - width);
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

// ------------------------------------------------------------------ history ----
//
// The desk is one route, so the browser's Back button used to leave the application, and
// nothing inside it went back either: a window closed to make room was simply gone. The desk
// keeps its own history (WindowManager.Back), and each step it takes is mirrored here as a
// browser history entry carrying the depth it was taken at. Back, Forward, Alt+Left and the
// mouse's back key then all arrive as popstate, and .NET walks the desk to that depth.

export function trackHistory(depth) {
    history.replaceState({ ...(history.state ?? {}), deskDepth: depth }, '');
    window.addEventListener('popstate', onPopState);
}

export function pushHistory(depth) {
    history.pushState({ ...(history.state ?? {}), deskDepth: depth }, '');
}

function onPopState(e) {
    const depth = e.state?.deskDepth;

    if (typeof depth === 'number')
        dotNet?.invokeMethodAsync('OnHistoryTraversed', depth);
}
