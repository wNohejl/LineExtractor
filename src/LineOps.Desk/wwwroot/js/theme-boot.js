// The stored theme, on <html> before the first paint.
//
// ThemeService cannot stamp the theme until the circuit opens, so an operator who chose light,
// or Nord, would see one frame of the dark default first. This classic script runs in <head>,
// before any stylesheet, and stamps the same attribute from the same storage. ThemeService owns
// the policy and repaints over this as soon as it starts; this file only mirrors it — the keys
// are theme.js's, the slugs DeskThemeFamilies.Slug's, the resolution ThemeService.Attribute's.
(() => {
    // A stored family is the enum member's name, read case-insensitively; anything else is no choice.
    const SLUGS = { standard: 'apple', nord: 'nord', solarized: 'solarized', rosepine: 'rose-pine', highcontrast: 'contrast' };
    const root = document.documentElement;

    const stored = key => {
        try {
            return (localStorage.getItem(key) || '').trim().toLowerCase();
        } catch {
            return '';
        }
    };

    const matches = (query, otherwise) => window.matchMedia ? window.matchMedia(query).matches : otherwise;

    try {
        // The Desk's own keys first, then the ones they replaced — theme.js copies an old choice
        // forward when the circuit opens, but this runs before it ever has on the first load.
        const mode = stored('desk.theme') || stored('lineops.theme');
        const family = stored('desk.theme.family') || stored('lineops.theme.family');
        const isDark = mode === 'dark' || (mode !== 'light' && matches('(prefers-color-scheme: dark)', true));
        const slug = Object.prototype.hasOwnProperty.call(SLUGS, family) ? SLUGS[family]
            : matches('(prefers-contrast: more)', false) ? 'contrast' : 'apple';
        root.dataset.theme = slug + '-' + (isDark ? 'dark' : 'light');
    } catch {
        root.dataset.theme = 'apple-dark';
    }
})();
