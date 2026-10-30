// The desk's theme, on the browser side.
//
// Three jobs, and only three: read and write the operator's choices in localStorage,
// stamp the resolved theme (and Reduce Transparency) onto <html> so the stylesheet switches,
// and report what the machine's appearance, contrast and transparency settings currently say.
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
function readKey(key, old) {
    try {
        const value = localStorage.getItem(key);
        if (value !== null) {
            return value;
        }

        const before = localStorage.getItem(old);
        if (before !== null) {
            localStorage.setItem(key, before);
        }
        return before;
    } catch {
        return null;
    }
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
// token block in lineops.css or themes.css. Every block weighs (0,1,0) and each is written
// after :root, so stamping any of them hands the entire desk over; "apple-dark" is set
// explicitly rather than by removing the attribute so that what is showing is always
// readable in the DOM inspector.
export function apply(theme) {
    document.documentElement.setAttribute('data-theme', theme);
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

export function watch(dotNetRef) {
    unwatch();

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
