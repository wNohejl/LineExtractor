// Two gestures on the desk, deliberately on two different hit targets so neither is ever
// triggered by accident:
//
//   - drag a TAB (a window's title bar)  -> reorders the row, or, let go over the middle of
//     another window, joins that window's column as a tab
//   - drag a ROW of a tab group's menu onto another window -> joins that window's column
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
    document.removeEventListener('keydown', holdTabStripKeys);
    document.addEventListener('keydown', holdTabStripKeys);
    document.removeEventListener('pointerdown', beginMenuTabDrag, true);
    document.addEventListener('pointerdown', beginMenuTabDrag, true);
    reportViewport();
}

// A tab group's key opens its menu with Down or Up (WindowTabs.razor); the same keys scroll the
// desk under it when the row overflows. Prevented here, as the divider's are, because Blazor's
// preventDefault cannot tell these keys from Tab.
const STRIP_KEYS = new Set(['ArrowDown', 'ArrowUp']);

function holdTabStripKeys(e) {
    if (!STRIP_KEYS.has(e.key) || e.ctrlKey || e.metaKey || e.altKey) return;
    if (e.target instanceof Element && e.target.closest('.wtabs__key')) e.preventDefault();
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
    handle.addEventListener('keydown', holdSplitKeys);
    handle.__deskSplit = onDown;
}

export function detachDivider(handle) {
    if (!handle || !handle.__deskSplit) return;

    handle.removeEventListener('pointerdown', handle.__deskSplit);
    handle.removeEventListener('keydown', holdSplitKeys);
    handle.__deskSplit = null;
}

// The divider's own keys (Desk.razor, SplitByKey) are also the desk's scroll keys: the desk
// scrolls sideways when the row overflows, and Home or an arrow pressed on a divider moved the
// columns and scrolled the desk under them in the same keystroke. Prevented here rather than
// with Blazor's preventDefault, which cannot tell these keys from Tab.
const SPLIT_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'Home', 'End']);

function holdSplitKeys(e) {
    if (!SPLIT_KEYS.has(e.key) || e.ctrlKey || e.metaKey || e.altKey) return;
    e.preventDefault();
    showReadoutBriefly(e.currentTarget);
}

// The readout floats over both columns' edges, so it is shown while the divider is being moved —
// for as long as a drag holds it, and for a beat after each key — not for as long as focus rests
// there: a focused divider kept the pills over the neighbouring window's text indefinitely (HIG
// audit, 17 November). The lit seam says "focused"; the divider's value says the widths aloud.
const READOUT_LINGER_MS = 1200;

function showReadoutBriefly(handle) {
    handle.classList.add('divider--keyed');
    clearTimeout(handle.__deskReadoutTimer);
    handle.__deskReadoutTimer = setTimeout(() => handle.classList.remove('divider--keyed'), READOUT_LINGER_MS);
    handle.addEventListener('blur', hideReadout, { once: true });
}

function hideReadout(e) {
    clearTimeout(e.currentTarget.__deskReadoutTimer);
    e.currentTarget.classList.remove('divider--keyed');
}

// The readout's two figures, written as the drag goes. The server draws them too, on every
// render; these only keep them current between the pointer moving and the server hearing of it,
// and are set as attributes (the stylesheet draws them) so the next render simply overwrites them.
function showWidths(handle, left, right) {
    handle.querySelector('[data-split-left]')?.setAttribute('data-width', String(Math.round(left)));
    handle.querySelector('[data-split-right]')?.setAttribute('data-width', String(Math.round(right)));
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
        showWidths(handle, leftStart + delta, rightStart - delta);
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

        target = markTarget(target, tabTargetAt(desk, e.clientX, e.clientY, win, MERGE_ZONE));
    };

    let target = null;

    const onUp = (e) => {
        handle.removeEventListener('pointermove', onMove);
        handle.removeEventListener('pointerup', onUp);
        handle.removeEventListener('pointercancel', onUp);

        if (!dragging)
            return;

        win.classList.remove('win--dragging-tab');
        document.body.classList.remove('tab-dragging');
        markTarget(target, null);

        if (e.type === 'pointercancel') {
            win.style.left = `${originLeft}px`;
            return;
        }

        // Over the middle of another window: a tab in its column. The window's own box is put
        // back first; the manager's redraw places it, and the row does not flash it mid-air.
        const into = tabTargetAt(desk, e.clientX, e.clientY, win, MERGE_ZONE);

        if (into) {
            win.style.left = `${originLeft}px`;
            dotNet?.invokeMethodAsync('OnTabDropped', managerId(win.id), managerId(into.id));
            return;
        }

        const orderedIds = computeDropOrder(desk, win.id, e.clientX);

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
    // One element per column: a tab behind another shares its column's box and is not a slot of
    // its own. The page lists windows in the order they were opened, not the row's (Desk.razor
    // keeps them still so none loses its scroll), so the slots are read left to right by place.
    const siblings = [...desk.querySelectorAll('.win:not(.win--behind)')]
        .filter((el) => el.id !== draggedElementId)
        .sort((a, b) => a.getBoundingClientRect().left - b.getBoundingClientRect().left);

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

    return orderedElementIds.map(managerId);
}

// Strip the "win-" prefix the DOM id carries back down to the manager's own id.
function managerId(elementId) {
    return elementId.replace(/^win-/, '');
}

// ------------------------------------------------------------- tabs (into a window) ----

// How much of a window's width, centred, takes a dragged title bar as a tab. The outer quarters
// on either side still mean "beside it", so reordering the row keeps working as it always has.
const MERGE_ZONE = 0.5;

/**
 * The window a tab let go at (x, y) would join: one drawn in the row (not behind another, not
 * the one being dragged, not the column the tab already belongs to), under the pointer, within
 * the centred <zone> of its width. Read from boxes rather than elementFromPoint, because the
 * dragged window floats over whatever it is above.
 */
function tabTargetAt(desk, x, y, exclude, zone) {
    for (const el of desk.querySelectorAll('.win:not(.win--behind)')) {
        if (el === exclude) continue;

        const rect = el.getBoundingClientRect();
        if (x < rect.left || x > rect.right || y < rect.top || y > rect.bottom) continue;

        const margin = rect.width * (1 - zone) / 2;
        return x >= rect.left + margin && x <= rect.right - margin ? el : null;
    }

    return null;
}

function markTarget(previous, next) {
    if (previous === next) return next;
    previous?.classList.remove('win--tab-target');
    next?.classList.add('win--tab-target');
    return next;
}

/**
 * A row of a tab group's menu (WindowTabs.razor marks each with data-tab-drag-id) dragged onto
 * another window. The window it names is behind in its group and has no box of its own, so a
 * label lifted off the menu follows the pointer instead, and anywhere over another window takes
 * it. Captured at the document, once, so rows drawn later need no wiring.
 */
function beginMenuTabDrag(event) {
    if (event.button !== 0 || !(event.target instanceof Element)) return;

    const row = event.target.closest('[data-tab-drag-id]');
    // The row's close glyph is a click, never a drag.
    if (!row || event.target.closest('.menu__remove')) return;

    const desk = document.querySelector('[data-desk]');
    if (!desk) return;

    const id = row.getAttribute('data-tab-drag-id');
    const home = row.closest('.win');
    const startX = event.clientX;
    const startY = event.clientY;

    let ghost = null;
    let target = null;

    const onMove = (e) => {
        if (!ghost) {
            if (Math.hypot(e.clientX - startX, e.clientY - startY) < DRAG_THRESHOLD) return;

            ghost = document.createElement('div');
            ghost.className = 'tab-ghost';
            ghost.textContent = row.querySelector('.menu__label, .menu-item__label')?.textContent?.trim()
                || row.getAttribute('aria-label') || 'Tab';
            document.body.appendChild(ghost);
            document.body.classList.add('tab-dragging');
        }

        ghost.style.left = `${e.clientX}px`;
        ghost.style.top = `${e.clientY}px`;
        target = markTarget(target, tabTargetAt(desk, e.clientX, e.clientY, home, 1));
    };

    const onUp = (e) => {
        document.removeEventListener('pointermove', onMove, true);
        document.removeEventListener('pointerup', onUp, true);
        document.removeEventListener('pointercancel', onUp, true);

        if (!ghost) return;

        ghost.remove();
        document.body.classList.remove('tab-dragging');
        markTarget(target, null);

        // The press ended on a row as a drag: the click that follows must not also choose it.
        const swallow = (c) => { c.stopPropagation(); c.preventDefault(); };
        window.addEventListener('click', swallow, { capture: true, once: true });
        setTimeout(() => window.removeEventListener('click', swallow, { capture: true }), 0);

        if (e.type === 'pointercancel') return;

        const into = tabTargetAt(desk, e.clientX, e.clientY, home, 1);
        if (into && id) dotNet?.invokeMethodAsync('OnTabDropped', id, managerId(into.id));
    };

    document.addEventListener('pointermove', onMove, true);
    document.addEventListener('pointerup', onUp, true);
    document.addEventListener('pointercancel', onUp, true);
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
