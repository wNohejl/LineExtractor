// Focus moved to a control the server has just drawn (DeskFocus.WhenDrawnAsync).
//
// A press that swaps one control for another — the Board take's Custom price… turning its price
// line into the typed fields, and "Use the fetched price" turning them back — removes the control
// that had focus, and the browser drops focus to the page. The server says where it should land
// instead, after the render that drew it; a frame later, so a popover closing on the same press
// has finished returning focus to its opener (popover.js) before this takes it on.

export function focusWhenDrawn(selector) {
    requestAnimationFrame(() => {
        const el = document.querySelector(selector);
        if (!el) {
            return;
        }

        el.focus();

        // A number to type over: selected, so the first keystroke replaces it.
        if (el instanceof HTMLInputElement) {
            try {
                el.select();
            } catch {
                // Not every input type can select its text; focus alone is enough.
            }
        }
    });
}
