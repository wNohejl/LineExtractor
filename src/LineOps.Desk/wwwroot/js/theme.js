// The desk's theme, on the browser side.
//
// Three jobs, and only three: read and write the operator's choices in localStorage,
// stamp the resolved theme onto <html> so the token block switches, and report what
// prefers-color-scheme currently says. Everything about *which* theme should be
// showing is decided in ThemeService; this file holds no policy, because a policy
// living in two languages is a policy that disagrees with itself.

const KEY = 'lineops.theme';

// Every localStorage touch is guarded. It throws outright — not returns null — in a
// Safari private window and under a "block all cookies" setting, and a theme
// preference is not worth taking the circuit down for. The desk simply forgets.
export function read() {
    try {
        return localStorage.getItem(KEY);
    } catch {
        return null;
    }
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
const FAMILY_KEY = 'lineops.theme.family';

export function readFamily() {
    try {
        return localStorage.getItem(FAMILY_KEY);
    } catch {
        return null;
    }
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

// The whole switch, in one attribute: "<family>-<dark|light>", which names exactly one
// token block in lineops.css or themes.css. Every block weighs (0,1,0) and each is written
// after :root, so stamping any of them hands the entire desk over; "apple-dark" is set
// explicitly rather than by removing the attribute so that what is showing is always
// readable in the DOM inspector.
export function apply(theme) {
    document.documentElement.setAttribute('data-theme', theme);
}

// Watch the machine's own preference so System mode keeps meaning "system" rather
// than "whatever system said when the circuit opened". The listener is kept on the
// module so a reconnect can replace it instead of stacking a second one.
let query = null;
let listener = null;

export function watch(dotNetRef) {
    unwatch();

    if (!window.matchMedia) {
        return;
    }

    query = window.matchMedia('(prefers-color-scheme: dark)');
    listener = e => dotNetRef.invokeMethodAsync('OnSystemPreferenceChanged', e.matches);

    // addEventListener over the deprecated addListener, with a fallback because
    // older WebKit only has the latter and would otherwise silently never fire.
    if (query.addEventListener) {
        query.addEventListener('change', listener);
    } else {
        query.addListener(listener);
    }
}

export function unwatch() {
    if (!query || !listener) {
        return;
    }

    if (query.removeEventListener) {
        query.removeEventListener('change', listener);
    } else {
        query.removeListener(listener);
    }

    query = null;
    listener = null;
}
