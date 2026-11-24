// The stored theme, on <html> before the first paint.
//
// ThemeService cannot stamp the theme until the circuit opens, so an operator who chose light,
// or Nord, would see one frame of the dark default first. This classic script runs in <head>,
// before any stylesheet, and stamps the same attributes from the same storage. ThemeService owns
// the policy and repaints over this as soon as it starts; this file only mirrors it — the keys
// are theme.js's, the resolution ThemeService.Attribute's, and the tables are not here at all:
// DeskHead writes them just ahead of this script as a JSON block (DeskBoot, in C#), built from
// DeskThemeFamilies.Catalog and DeskLooks.All, so there is no copy of either to drift.
(() => {
    const root = document.documentElement;

    // The tables, or none: a host whose head predates the block, or a block that does not parse,
    // boots the plain desk — the stored mode on the default family, no accent, no look — and
    // ThemeService paints the rest when the circuit opens.
    const DEFAULT_SLUG = 'apple';
    const tables = (() => {
        try {
            const block = document.getElementById('desk-boot');
            const parsed = block ? JSON.parse(block.textContent || '') : null;
            return parsed && typeof parsed === 'object' ? parsed : {};
        } catch {
            return {};
        }
    })();
    const table = name => tables[name] && typeof tables[name] === 'object' ? tables[name] : {};
    const own = (map, key) => Object.prototype.hasOwnProperty.call(map, key);

    // A stored family is the enum member's name, read case-insensitively; anything else is no choice.
    const SLUGS = table('families');
    const STANDARD = typeof tables.standard === 'string' ? tables.standard : DEFAULT_SLUG;
    const CONTRAST = typeof tables.contrast === 'string' ? tables.contrast : STANDARD;
    // The accents with a block of their own, by family slug — every key of a family's accents but
    // the first, which is what its blocks already declare and so is never stamped.
    const ACCENTS = table('accents');
    // Each look's key, attribute, stored name to stamped value, and the machine setting that takes it off.
    const LOOKS = Array.isArray(tables.looks) ? tables.looks : [];

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
        const slug = own(SLUGS, family) ? SLUGS[family]
            : matches('(prefers-contrast: more)', false) ? CONTRAST : STANDARD;
        root.dataset.theme = slug + '-' + (isDark ? 'dark' : 'light');

        // The accent is the resolved family's own choice, under its slug; Standard's falls back to
        // the key it had while it was the only family with one, only when its own is empty —
        // ThemeService.AccentAttribute, in the same order of questions.
        const accent = stored('desk.accent.' + slug) || (slug === STANDARD ? stored('desk.accent') : '');
        const accents = own(ACCENTS, slug) && Array.isArray(ACCENTS[slug]) ? ACCENTS[slug] : [];
        if (accents.indexOf(accent) >= 0) {
            root.dataset.accent = accent;
        }

        // The looks belong to every family: each is stamped when its stored name is one with a value
        // to stamp, unless the machine setting it yields to is on — under Reduce Transparency there
        // is no glass to tint. ThemeService.AttributeOf, the same questions.
        for (const look of LOOKS) {
            if (!look || typeof look.key !== 'string' || typeof look.attribute !== 'string') {
                continue;
            }

            const values = look.values && typeof look.values === 'object' ? look.values : {};
            const value = stored(look.key);
            if (own(values, value) && !(look.yieldsTo && matches(look.yieldsTo, false))) {
                root.setAttribute(look.attribute, values[value]);
            }
        }
    } catch {
        root.dataset.theme = DEFAULT_SLUG + '-dark';
    }

    // The browser's chrome in the same ground — theme.js stampThemeColor, which repaints it on every
    // change. Read from the stamped block, never from a palette kept here, so it has to wait for the
    // stylesheets this script runs ahead of: the first try finds no token yet, and the parse's end
    // (or, failing that, the load) finds it. DeskHead declares the meta; a host without one gets one.
    const themeColor = () => {
        try {
            const ground = getComputedStyle(root).getPropertyValue('--surface-0').trim();
            if (!ground) {
                return false;
            }

            let meta = document.querySelector('meta[name="theme-color"]');
            if (!meta) {
                meta = document.createElement('meta');
                meta.setAttribute('name', 'theme-color');
                document.head.appendChild(meta);
            }

            meta.setAttribute('content', ground);
            return true;
        } catch {
            return true;
        }
    };

    if (!themeColor()) {
        document.addEventListener('DOMContentLoaded', () => {
            if (!themeColor()) {
                window.addEventListener('load', themeColor, { once: true });
            }
        }, { once: true });
    }
})();
