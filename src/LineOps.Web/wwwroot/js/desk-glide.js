// The glide: one highlight plate per cluster of flat controls, slid between them
// instead of each control flashing its own background. A container opts in with
// data-glide; nothing else is wired. Delegation on the document means clusters can
// appear, re-render, or vanish (Blazor owns the DOM) without any bookkeeping here —
// a plate that got diffed away is simply rebuilt on the next hover.
//
// Two flavours, same engine:
//
//   data-glide           a grey plate that appears under the pointer and leaves
//                        with it. For clusters where nothing is "current".
//   data-glide="marble"  the marble, in its iris outline. Follows the pointer the
//                        same way, but instead of vanishing it settles back onto
//                        whichever child carries data-glide-rest — the toolbar's
//                        selected window. A cluster with no resting child behaves
//                        like the plate and fades out.
//
// Measuring the target rather than stepping by a fixed column is what lets the
// toolbar hold tabs of different widths: a window called "Ops" has no business
// being as wide as one called "Boston Red Sox at Miami Marlins". The CSS-variable
// marble in lineops.css still drives the gate and the section tabs, where the
// positions genuinely are equal and no measuring is needed.
//
// Styling lives in lineops.css under "The glide".

(function () {
    'use strict';

    const ACTIONABLE = 'button, [role="menuitem"], a[href]';

    function isMarble(container) {
        return container.getAttribute('data-glide') === 'marble';
    }

    function plateFor(container) {
        let plate = container.querySelector(':scope > .glide-plate');
        if (!plate) {
            plate = document.createElement('span');
            plate.className = 'glide-plate';
            plate.setAttribute('aria-hidden', 'true');
            container.prepend(plate);
        }

        // Blazor re-renders can replace the resting child, so the flavour is
        // re-asserted every time rather than only at creation.
        plate.classList.toggle('glide-plate--marble', isMarble(container));
        return plate;
    }

    /// The child the marble sits on when the pointer is elsewhere.
    function restingChild(container) {
        return container.querySelector(':scope [data-glide-rest]');
    }

    function move(container, target) {
        if (target.disabled || target.getAttribute('aria-disabled') === 'true') return;

        const plate = plateFor(container);
        const fresh = !container.hasAttribute('data-glide-live');

        const c = container.getBoundingClientRect();
        const t = target.getBoundingClientRect();

        // A fresh plate materialises under the control rather than sliding in
        // from wherever it last was.
        if (fresh) plate.classList.add('glide-plate--still');

        plate.style.width = t.width + 'px';
        plate.style.height = t.height + 'px';
        plate.style.transform =
            'translate(' + (t.left - c.left + container.scrollLeft) + 'px,'
                         + (t.top - c.top + container.scrollTop) + 'px)';

        const tone = target.getAttribute('data-glide-tone');
        if (tone) plate.setAttribute('data-tone', tone);
        else plate.removeAttribute('data-tone');

        container.setAttribute('data-glide-live', '');

        if (fresh) {
            // Same reason as scheduleReseat: a hidden document never reaches a frame,
            // and the plate would stay pinned to its no-transition state for good.
            queueMicrotask(function () {
                plate.classList.remove('glide-plate--still');
            });
        }
    }

    // Leaving the cluster: the plate goes out, the marble goes home.
    function settle(container, leavingFor) {
        if (leavingFor && container.contains(leavingFor))
            return;

        const rest = isMarble(container) ? restingChild(container) : null;

        if (rest) move(container, rest);
        else container.removeAttribute('data-glide-live');
    }

    /// Put the marble back on its resting child without animating in from nowhere.
    /// Called when Blazor changes which child rests — a new focused window — while
    /// the pointer is somewhere else entirely.
    function reseat(container) {
        if (container.matches(':hover')) return;

        const rest = restingChild(container);

        if (rest) move(container, rest);
        else container.removeAttribute('data-glide-live');
    }

    /// Blazor moves the resting marker as focus moves between windows, and rebuilds
    /// the tabs outright when one opens or closes. Watching for both is cheaper and
    /// more reliable than having the component call out to JS on every render.
    ///
    /// childList matters as much as the attribute: a re-rendered tab arrives as a new
    /// node that already carries data-glide-rest, which is not an attribute change on
    /// anything the observer was watching. Without it the marble keeps the size and
    /// position of a tab that no longer exists.
    /// Batched to once per mutation burst, on a microtask rather than a frame:
    /// requestAnimationFrame does not run while the document is hidden, which would
    /// leave a background tab holding a marble sized to a window that has since been
    /// closed. The observer's callback already runs after the DOM is updated, and
    /// getBoundingClientRect flushes layout itself, so there is nothing to wait for.
    let pending = null;

    function scheduleReseat(host) {
        if (pending !== null) {
            pending.add(host);
            return;
        }

        pending = new Set([host]);

        queueMicrotask(function () {
            const hosts = pending;
            pending = null;
            if (hosts) hosts.forEach(reseat);
        });
    }

    function watch() {
        const observer = new MutationObserver(function (records) {
            for (const record of records) {
                const node = record.target instanceof Element ? record.target : null;
                const host = node ? node.closest('[data-glide="marble"]') : null;

                if (host) scheduleReseat(host);

                // A whole group can appear at once — the window strip does not exist
                // until the first window opens. Its mutation is reported against the
                // parent, and closest() only looks upward, so the group would never
                // be seated. Look into what was added as well.
                for (const added of record.addedNodes) {
                    if (!(added instanceof Element)) continue;

                    if (added.matches('[data-glide="marble"]')) scheduleReseat(added);

                    added.querySelectorAll('[data-glide="marble"]').forEach(scheduleReseat);
                }
            }
        });

        observer.observe(document.body, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: ['data-glide-rest']
        });
    }

    /// What the plate should measure itself against.
    ///
    /// Normally that is the control under the pointer. A composite item — a window
    /// tab, which is one thing to the eye but a focus button plus a close button to
    /// the DOM — marks its wrapper with data-glide-item and is measured whole, so
    /// the marble covers the tab instead of shrinking onto the little × the moment
    /// the pointer crosses it.
    function measured(node) {
        if (!(node instanceof Element)) return null;
        return node.closest('[data-glide-item]') || node.closest(ACTIONABLE);
    }

    function enter(e) {
        const target = measured(e.target);
        if (!target) return;

        const container = target.closest('[data-glide]');
        if (container) move(container, target);
    }

    function leave(e) {
        const container = e.target instanceof Element ? e.target.closest('[data-glide]') : null;
        if (container) settle(container, e.relatedTarget);
    }

    // Pointer and keyboard read as the same gesture: focus moving through a
    // cluster slides the same plate hovering does.
    document.addEventListener('pointerover', enter);
    document.addEventListener('pointerout', leave);
    document.addEventListener('focusin', enter);
    document.addEventListener('focusout', leave);

    // Marbles need seating on first paint, before anything is hovered.
    function seatAll() {
        document.querySelectorAll('[data-glide="marble"]').forEach(reseat);
    }

    watch();

    if (document.readyState === 'loading')
        document.addEventListener('DOMContentLoaded', seatAll);
    else
        seatAll();

    // Blazor renders the toolbar after the script runs, and a resting child that
    // appears later still has to be found.
    document.addEventListener('pointermove', function once() {
        document.removeEventListener('pointermove', once);
        seatAll();
    });
})();
