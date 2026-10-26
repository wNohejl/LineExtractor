// The panel toolbar's fold: whether its row still fits on one line.
//
// PanelToolbar used to wrap, and a narrow window stacked its pickers, field and keys on three
// or four lines. Now the row stays one line and the owner names what may leave it; this module
// measures, because .NET cannot, and tells PanelToolbar how far to fold. It calls back only
// when the fold changes, never per resize, so dragging a window edge costs the circuit nothing
// until a control actually has to move.
//
// The rule. The row is `flex-wrap: nowrap`, so content that does not fit runs past the box and
// scrollWidth exceeds clientWidth; flexible items (the search) have already shrunk to their
// floor by then, so that is the moment nothing more can give. Fold one step and remember the
// width the row needed at that moment. Unfold only once the box is that wide again plus
// HYSTERESIS: the needed width was measured, not guessed, so the controls coming back fit, and
// the margin keeps a window resized across the line from flickering between the two.
//
// Steps: 1 folds the Overflow into More; 2 then folds the Search as well. The row's own box is
// observed for the window's width, and each of its children for content that grows in place —
// a picker whose options arrive, a key whose label becomes the running job's name.
//
// Styling is controls.css under "Toolbar".

const HYSTERESIS = 24;
const FOCUSABLE = 'input:not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])';

const rows = new WeakMap();

/// Batched to one measure per burst of notifications, on a microtask rather than a frame.
/// requestAnimationFrame does not run while the page is hidden or throttled — a background tab,
/// a pane the desk was restored into before it was shown — and a measure parked on a frame
/// that never comes blocked every later one behind it: a row that was narrow when it first
/// appeared never folded until a resize. (desk-glide.js learned the same thing.) Observer
/// callbacks already run after layout, and reading scrollWidth flushes it, so there is nothing
/// to wait a frame for.
function schedule(state) {
    if (state.queued || state.waiting) return;
    state.queued = true;
    queueMicrotask(() => measure(state));
}

function measure(state) {
    state.queued = false;
    if (!rows.has(state.row)) return;

    const row = state.row;
    const width = row.clientWidth;

    // A row not yet laid out — a window restored before the desk has placed it, a minimised
    // one — has no width to decide anything from. The resize observer fires again when it gets
    // one, and that notification measures it.
    if (!row.isConnected || width === 0) return;

    // An open popover hangs off the row and can reach past its edge, which scrollWidth counts
    // as content. Decide once it has closed; nothing in the row moves while it is open.
    if (row.querySelector('.popover__panel')) {
        clearTimeout(state.retry);
        state.retry = setTimeout(() => schedule(state), 250);
        return;
    }

    // Both are rounded to whole pixels, separately, so a row that fits exactly can read one over.
    const over = row.scrollWidth > width + 1;
    let fold = state.fold;

    if (over && fold < state.steps) {
        state.need[fold] = row.scrollWidth;
        fold++;
    } else if (!over && fold > 0 && width >= state.need[fold - 1] + HYSTERESIS) {
        fold--;
    }

    if (fold !== state.fold) flip(state, fold);
}

function flip(state, fold) {
    state.fold = fold;

    if (fold > 0) state.row.setAttribute('data-narrow', 'true');
    else state.row.removeAttribute('data-narrow');

    // Until .NET has redrawn the row, the DOM still shows the old fold and measuring it would
    // fold a second step on the strength of a row that is about to shrink. The redraw changes
    // the row's children, which the mutation observer sees; the timer covers a redraw that
    // happened to change nothing.
    state.waiting = true;
    clearTimeout(state.timer);
    state.timer = setTimeout(() => settle(state), 1000);

    state.dotNet.invokeMethodAsync('Fold', fold).catch(() => settle(state));
}

function settle(state) {
    clearTimeout(state.timer);
    state.waiting = false;
    watchChildren(state);
    schedule(state);
}

function watchChildren(state) {
    state.resize.disconnect();
    state.resize.observe(state.row);
    for (const child of state.row.children) state.resize.observe(child);
}

/// Starts watching a toolbar, or tells a watched one how far it may now fold.
export function observe(row, dotNet, steps) {
    if (!row) return;

    let state = rows.get(row);

    if (steps <= 0) {
        unobserve(row);
        return;
    }

    if (!state) {
        state = { row, dotNet, steps, fold: 0, need: [], queued: false, waiting: false, timer: 0 };
        state.resize = new ResizeObserver(() => schedule(state));
        state.mutation = new MutationObserver(() => state.waiting ? settle(state) : (watchChildren(state), schedule(state)));
        state.mutation.observe(row, { childList: true });
        rows.set(row, state);
        watchChildren(state);
        // Measured now, not only when the observer first reports: a row that is narrow when it
        // appears folds before anyone sees it wrap.
        schedule(state);
        return;
    }

    state.dotNet = dotNet;
    state.steps = steps;

    if (state.fold > steps) flip(state, steps);
    else schedule(state);
}

export function unobserve(row) {
    const state = row && rows.get(row);
    if (!state) return;

    state.resize.disconnect();
    state.mutation.disconnect();
    clearTimeout(state.timer);
    clearTimeout(state.retry);
    rows.delete(row);
}

/// Focus into the More panel on open, onto its first control, as a menu takes it onto its
/// first item. Escape gives it back to More (DeskPopover and popover.js).
export function focusFirst(panel) {
    const first = panel?.querySelector(FOCUSABLE);
    first?.focus({ preventScroll: true });
}
