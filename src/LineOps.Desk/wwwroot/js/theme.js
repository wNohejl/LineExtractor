// The desk's theme, on the browser side.
//
// Three jobs, and only three: read and write the operator's choices in localStorage,
// stamp the resolved theme (and Reduce Transparency and the looks) onto <html> so the stylesheet switches —
// the browser's theme-color with it, and inside a cross-fade when the operator made the change —
// and report what the machine's appearance, contrast and transparency settings currently say.
// Beside them, one courtesy to the pickers that make those choices: their arrow keys do not
// scroll the page (holdChoiceKeys, below). Every key and attribute a look uses is handed in by
// ThemeService; the only keys named here are the mode's, the family's and the accents'.
// Everything about *which* theme should be showing is decided in ThemeService; this file
// holds no policy, because a policy living in two languages is a policy that disagrees with
// itself.

const KEY = 'desk.theme';

// The key the choice lived under before the Desk carried its own name. Read when the new one is
// empty and copied onto it, so a browser that chose its theme before the rename comes up in it
// once and is on the new key from then on; never written to, and left where it was.
const OLD_KEY = 'lineops.theme';

// Every localStorage touch is guarded. It throws outright — not returns null — in a
// Safari private window and under a "block all cookies" setting, and a theme
// preference is not worth taking the circuit down for. The desk simply forgets.
//
// A value of nothing but whitespace is no value: theme-boot.js trims what it reads and falls back
// to the old key when the new one is blank, so this does the same, or the two would stamp
// different desks from one storage.
function readKey(key, old) {
    try {
        const value = localStorage.getItem(key);
        if (!blank(value)) {
            return value;
        }

        const before = localStorage.getItem(old);
        if (blank(before)) {
            return null;
        }

        localStorage.setItem(key, before);
        return before;
    } catch {
        return null;
    }
}

function blank(value) {
    return value === null || value.trim() === '';
}

export function read() {
    return readKey(KEY, OLD_KEY);
}

export function store(mode) {
    try {
        localStorage.setItem(KEY, mode);
    } catch {
        /* forgotten, not fatal */
    }
}

// The family — which palette — kept under its own key rather than folded into the mode's.
// The two are independent choices, and a browser that stored "Light" before families existed
// must go on reading it as a mode without having to be migrated.
const FAMILY_KEY = 'desk.theme.family';
const OLD_FAMILY_KEY = 'lineops.theme.family';

export function readFamily() {
    return readKey(FAMILY_KEY, OLD_FAMILY_KEY);
}

export function storeFamily(family) {
    try {
        localStorage.setItem(FAMILY_KEY, family);
    } catch {
        /* forgotten, not fatal */
    }
}

// The accent — each family's chosen hue — under a key per family, the family's slug appended:
// desk.accent.apple, desk.accent.nord. Per family because each family's list is its own, and an
// operator who gave Nord one and Standard another expects both back.
const FAMILY_ACCENT_KEY = 'desk.accent.';

// Where the accent lived while it was the Standard desk's alone. Only ever read — ThemeService
// asks for it when Standard's own key is empty — and removed only by a reset.
const ACCENT_KEY = 'desk.accent';

export function readAccents(slugs) {
    return (slugs || []).map(slug => {
        try {
            return localStorage.getItem(FAMILY_ACCENT_KEY + slug);
        } catch {
            return null;
        }
    });
}

export function readAccent() {
    try {
        return localStorage.getItem(ACCENT_KEY);
    } catch {
        return null;
    }
}

export function storeAccent(slug, accent) {
    try {
        localStorage.setItem(FAMILY_ACCENT_KEY + slug, accent);
    } catch {
        /* forgotten, not fatal */
    }
}

// The looks — the glass and the four makes, and any setting added beside them — each under its own
// key, with no older key: they arrived after the Desk carried its own name. Which keys they are is
// ThemeService's to say (DeskLooks), so this file reads and writes whatever key it is handed, in
// one read for all of them, as the accents are read.
export function readValues(keys) {
    return (keys || []).map(key => {
        try {
            return localStorage.getItem(key);
        } catch {
            return null;
        }
    });
}

export function storeValue(key, value) {
    try {
        localStorage.setItem(key, value);
    } catch {
        /* forgotten, not fatal */
    }
}

// "Reset appearance": every key this file writes, every family's accent and every look among them,
// and the keys they replaced — left behind, an old key would be copied forward again on the next load.
export function forgetAppearance(slugs, looks) {
    const keys = [KEY, OLD_KEY, FAMILY_KEY, OLD_FAMILY_KEY, ACCENT_KEY]
        .concat((slugs || []).map(slug => FAMILY_ACCENT_KEY + slug))
        .concat(looks || []);

    for (const key of keys) {
        try {
            localStorage.removeItem(key);
        } catch {
            /* nothing stored, or nowhere to store it */
        }
    }
}

export function prefersDark() {
    return window.matchMedia
        ? window.matchMedia('(prefers-color-scheme: dark)').matches
        : true;
}

// Increase Contrast and Reduce Transparency, read the same way. What either one *means* for the
// desk — the contrast family, opaque materials — is ThemeService's to decide, not this file's.
export function prefersMoreContrast() {
    return window.matchMedia
        ? window.matchMedia('(prefers-contrast: more)').matches
        : false;
}

export function prefersReducedTransparency() {
    return window.matchMedia
        ? window.matchMedia('(prefers-reduced-transparency: reduce)').matches
        : false;
}

// The whole switch, in one attribute: "<family>-<dark|light>", which names exactly one
// token block in desk.css or themes.css. Every block weighs (0,1,0) and each is written
// after :root, so stamping any of them hands the entire desk over; "apple-dark" is set
// explicitly rather than by removing the attribute so that what is showing is always
// readable in the DOM inspector.
//
// The accent rides in the same call. Its blocks are compound — [data-theme][data-accent],
// (0,2,0) — so they win over the family block beneath them without touching anything else.
// ThemeService sends null for the showing family's own accent, and null removes the attribute:
// the inspector shows an accent only when one was chosen and something answers it.
export function apply(theme, accent) {
    const root = document.documentElement;

    root.setAttribute('data-theme', theme);

    if (accent) {
        root.setAttribute('data-accent', accent);
    } else {
        root.removeAttribute('data-accent');
    }

    stampThemeColor();
}

// The browser's own chrome — the tab strip, the title bar, the address bar where a browser tints
// them — in the ground of the desk that is showing. Read back from the block just stamped rather
// than kept as a table here: the palette lives in the stylesheet, and a second copy in this file
// would be a second copy to drift. DeskHead declares the meta; one is made only for a host whose
// head lacks it. theme-boot.js does the same before the circuit opens.
function stampThemeColor() {
    const ground = getComputedStyle(document.documentElement).getPropertyValue('--surface-0').trim();
    if (!ground) {
        return;
    }

    let meta = document.querySelector('meta[name="theme-color"]');
    if (!meta) {
        meta = document.createElement('meta');
        meta.setAttribute('name', 'theme-color');
        document.head.appendChild(meta);
    }

    meta.setAttribute('content', ground);
}

// ---- The switch, cross-faded --------------------------------------------------
//
// An operator's change of any appearance setting fades from the old desk to the new one
// rather than cutting: the browser photographs the page, the attribute changes under the
// photograph, and the two cross-fade on the desk's tempo (desk.css, "The theme switch"). Only a
// change the operator made: ThemeService calls this from its setters and from nowhere else, so
// the first paint and the machine's own change at sunset stay instant — nobody asked for either,
// and a fade nobody asked for is decoration.
//
// The order is the point. ThemeService paints through two hops — the attribute here, then the
// Changed event that re-renders MudThemeProvider and whichever panel held the picker — and a
// photograph taken between them would fade into a half-changed desk. So the transition is opened
// first and held: beginTransition resolves once the old desk has been captured and the update
// callback is running, ThemeService stamps and announces inside it, then settleTransition says
// the announcement has gone out. Blazor's renders land just after (the panel's after the handler
// returns), so the hold lets go once the document has been still for QUIET milliseconds, and the
// new desk is taken whole. A live desk is never still — a clock ticks, a feed streams rows — so
// stillness alone would run every switch to the limit with rendering paused: SETTLE_LIMIT
// milliseconds after the settle the hold lets go whatever is still moving, by which time the
// switch's own renders have long landed. HOLD_LIMIT bounds every wait, the settle that never
// comes included: a lost circuit never freezes the page.
//
// 0 means "no transition": no API (the switch is the instant one, as before), or the machine asks
// for reduced motion — checked here as well as in the stylesheet, so there is no photograph at all.
const HOLD_LIMIT = 1000;
const QUIET = 50;
const SETTLE_LIMIT = 180;

let serial = 0;
let held = null;

function reducesMotion() {
    return window.matchMedia
        ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
        : false;
}

// A transition another change skipped rejects its promises with AbortError (and a hidden page
// with InvalidStateError). Neither is a failure of the switch, which has already happened.
function quiet(promise) {
    if (promise && typeof promise.catch === 'function') {
        promise.catch(() => { /* skipped, not failed */ });
    }
}

function hold(id, done) {
    let quietTimer = 0;
    let settleTimer = 0;
    let observer = null;
    const limit = setTimeout(end, HOLD_LIMIT);

    function end() {
        clearTimeout(limit);
        clearTimeout(settleTimer);
        clearTimeout(quietTimer);
        if (observer) {
            observer.disconnect();
        }
        if (held && held.id === id) {
            held = null;
        }
        done();
    }

    function settle() {
        // The first settle starts the short clock; a second one does not restart it.
        if (!settleTimer) {
            settleTimer = setTimeout(end, SETTLE_LIMIT);
        }
        if (!observer && typeof MutationObserver === 'function') {
            observer = new MutationObserver(() => {
                clearTimeout(quietTimer);
                quietTimer = setTimeout(end, QUIET);
            });
            observer.observe(document.documentElement, { attributes: true, childList: true, characterData: true, subtree: true });
        }
        clearTimeout(quietTimer);
        quietTimer = setTimeout(end, QUIET);
    }

    return { id, end, settle };
}

export function beginTransition() {
    if (typeof document.startViewTransition !== 'function' || reducesMotion()) {
        return Promise.resolve(0);
    }

    const id = ++serial;

    return new Promise(entered => {
        // If the callback never runs, the switch goes ahead uncovered rather than waiting on it.
        let abandoned = false;
        const giveUp = setTimeout(() => { abandoned = true; entered(0); }, HOLD_LIMIT);

        let transition;
        try {
            transition = document.startViewTransition(() => {
                clearTimeout(giveUp);
                if (abandoned) {
                    return undefined;
                }

                return new Promise(done => {
                    // A second change made while the first was held: the browser has skipped the
                    // first transition for this one, so its hold ends here rather than at its limit.
                    if (held) {
                        held.end();
                    }
                    held = hold(id, done);
                    entered(id);
                });
            });
        } catch {
            clearTimeout(giveUp);
            entered(0);
            return;
        }

        quiet(transition.ready);
        quiet(transition.updateCallbackDone);
        quiet(transition.finished);
    });
}

// The change has been stamped and announced; the new desk is taken once the renders it set off
// have landed. A stale id — a transition a later change already ended — is no-one's to settle.
export function settleTransition(id) {
    if (held && held.id === id) {
        held.settle();
    }
}

// Reduce Transparency, as an attribute the stylesheet answers. Removed rather than set to
// "false" when it is off, so a selector on the attribute alone is enough.
export function applyTransparency(reduced) {
    if (reduced) {
        document.documentElement.setAttribute('data-reduced-transparency', 'true');
    } else {
        document.documentElement.removeAttribute('data-reduced-transparency');
    }
}

// Every look, in one call, the same way: a map of attribute to value or null — data-glass to
// tinted, data-shape to null, and so on — and null removes the attribute, so the inspector shows a look
// only when one was chosen and something answers it. Which attributes there are, and which value
// means "nothing to stamp" (a default, or the glass under Reduce Transparency), is ThemeService's
// to say, not this file's.
export function applyLooks(looks) {
    const root = document.documentElement;

    for (const [name, value] of Object.entries(looks || {})) {
        if (value) {
            root.setAttribute(name, value);
        } else {
            root.removeAttribute(name);
        }
    }
}

// Watch the machine's own preferences so System mode keeps meaning "system" rather
// than "whatever system said when the circuit opened". Three queries, one report: whichever
// moved, .NET hears all three as they stand now, so it never has to remember which half of a
// pair it was last told. The listener is kept on the module so a reconnect can replace it
// instead of stacking a second one.
const QUERIES = [
    '(prefers-color-scheme: dark)',
    '(prefers-contrast: more)',
    '(prefers-reduced-transparency: reduce)'
];

let queries = [];
let listener = null;

// A roving radiogroup moves its choice with the arrows, Home and End (Roving, in C#: the theme and
// accent pickers, DeskRadioGroup); in the Window manager the same keys scroll the window's body
// under them, so the choice moved and the page jumped. Held here, as windowing.js holds the tab
// strip's, because Blazor's preventDefault is one flag for every key and would swallow Tab with
// them. Exactly the keys Roving answers, and only on a radio of a group marked data-roving — a
// group opts in by carrying the marker, and this file names none of them.
const CHOICE_KEYS = new Set(['ArrowRight', 'ArrowDown', 'ArrowLeft', 'ArrowUp', 'Home', 'End']);
const CHOICE_RADIOS = '[role="radiogroup"][data-roving] [role="radio"]';

function holdChoiceKeys(e) {
    if (!CHOICE_KEYS.has(e.key) || e.ctrlKey || e.metaKey || e.altKey) {
        return;
    }
    if (e.target instanceof Element && e.target.closest(CHOICE_RADIOS)) {
        e.preventDefault();
    }
}

export function watch(dotNetRef) {
    unwatch();

    document.addEventListener('keydown', holdChoiceKeys);

    if (!window.matchMedia) {
        return;
    }

    queries = QUERIES.map(q => window.matchMedia(q));
    listener = () => dotNetRef.invokeMethodAsync(
        'OnSystemPreferenceChanged',
        queries[0].matches,
        queries[1].matches,
        queries[2].matches);

    // addEventListener over the deprecated addListener, with a fallback because
    // older WebKit only has the latter and would otherwise silently never fire.
    for (const query of queries) {
        if (query.addEventListener) {
            query.addEventListener('change', listener);
        } else {
            query.addListener(listener);
        }
    }
}

export function unwatch() {
    document.removeEventListener('keydown', holdChoiceKeys);

    if (!listener) {
        return;
    }

    for (const query of queries) {
        if (query.removeEventListener) {
            query.removeEventListener('change', listener);
        } else {
            query.removeListener(listener);
        }
    }

    queries = [];
    listener = null;
}
