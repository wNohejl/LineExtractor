// The desk's theme, on the browser side.
//
// Three jobs, and only three: read and write the operator's choice in localStorage,
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

export function prefersDark() {
    return window.matchMedia
        ? window.matchMedia('(prefers-color-scheme: dark)').matches
        : true;
}

// The whole switch, in one attribute. Both token blocks weigh (0,1,0) and the light
// one is written second, so stamping "light" here is enough to hand the entire desk
// over; "apple-dark" is set explicitly rather than by removing the attribute so that
// what is showing is always readable in the DOM inspector.
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
