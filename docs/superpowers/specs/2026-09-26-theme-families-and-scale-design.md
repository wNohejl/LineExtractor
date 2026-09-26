# Theme families, UI scale and text size

2026-09-26 · branch `LineX_Development`

## What was asked

More themes than the default dark and light, then UI and text scaling in the Window manager.

## Themes: a family and an appearance, not a longer list

The desk already has three appearance positions — Dark, Light, System — and System is the one
worth keeping honest: it keeps following the machine. Adding "Nord" or "Solarized" as a fourth and
fifth position on that same control would make each of them a fixed desk and break System for
anyone who picks one.

So a theme is two choices:

- **Family** — Standard (the Apple desk as it was), Nord, Solarized, Rosé Pine, High contrast.
- **Appearance** — Dark, Light, System, unchanged.

Every family ships a dark and a light variant, so every family works under System. The attribute on
`<html>` becomes `<family>-<dark|light>`: `apple-dark`, `apple-light`, `nord-dark`, … The Standard
light block was `[data-theme="light"]` and is renamed to `apple-light`, so all ten desks share one
naming rule.

### Where the values live

- `lineops.css` keeps the Standard pair (`:root, [data-theme="apple-dark"]` and `apple-light`).
- `themes.css`, loaded after `lineops.css` and before `mud-bridge.css`, holds the eight new blocks.
  Each block redeclares every colour token and nothing else, exactly as the light block does
  (ADR 0016). No component rule changes: nothing below the token blocks knows which theme is
  showing, and a grep of the codebase confirms no rule keys off `data-theme`.
- C# mirrors each block as a `DeskPalette` (the colours MudBlazor derives from). The `MudTheme`
  becomes per family: `DeskTheme.For(family)` carries that family's dark and light palettes, and
  `MudThemeProvider` takes it from `ThemeService` along with `IsDarkMode`.
- Charts take the resolved `DeskPalette` rather than an `isDark` flag, so a Nord chart is drawn in
  Nord colours. The old overloads stay and keep meaning Standard.

### Persistence

`ThemeService` gains `Family`, stored in `localStorage` under `lineops.theme.family` beside the
existing appearance key. An unreadable stored family is not a choice and falls back to Standard, the
same rule the appearance key follows.

### The picker

The Appearance section of the Window manager keeps its Dark/Light/System gate and gains a row of
swatches, one per family. Each swatch carries its own `data-theme` attribute, so it is painted by
that family's real token block in the appearance currently showing. A swatch is a preview because
it is the theme, not a picture of it.

### Guard rails (tests)

The existing gates generalise from one pair to every block:

1. every block redeclares every colour token the Standard dark block declares, and invents none;
2. every block's `color-scheme` matches its suffix;
3. every C# `DeskPalette` matches its block, token for token;
4. `--text-secondary` clears AA over `--surface-1` on every block, and `--text-tertiary` stays below
   it (ADR 0016's rule — fix the call site, never brighten the dim token); the High contrast pair
   clears AAA for secondary;
5. no new family is a copy of Standard (surfaces and accent must differ from the Standard block of
   the same appearance).

## Scale: two settings in the Window manager

Both live on `DeskSettings`, are saved with the desk layout, and restore with it. A layout saved
before this change reads as 100% on both.

- **UI scale** — 80, 90, 100, 110, 125, 150%. Everything grows: type, spacing, controls, chrome.
- **Text size** — 90, 100, 110, 125, 140%. Only type grows; the layout keeps its geometry.

### UI scale is CSS `zoom` on `<html>`

The alternative, multiplying every length in the stylesheet by a variable, means touching several
hundred pixel values plus the inline styles in the panels, and missing one leaves a control
unscaled. `zoom` scales everything, and it has been standard in every engine since 2024.

It has one consequence, measured in the browser pane (Chrome 152) before this design was written.
`getBoundingClientRect()` and pointer `clientX` report visual pixels, but `offsetWidth` and an
element's own `style.left` are in layout pixels. Code that reads a rectangle and writes a position
is off by the zoom factor unless it converts:

- **The desk's own scripts** — windowing.js (viewport report, divider drag, tab drag), dialogs.js
  (floating dialogs) and desk-glide.js (the gate's plate) — divide by `currentCSSZoom`.
- **MudBlazor's popovers** position themselves from the anchor's rectangle, and the misplacement was
  measured: with 125% on `<html>` a menu anchored at x=215 rendered at x=268. The fix is CSS only.
  `.mud-popover-provider` is zoomed back to 1, so the popover's `left`/`top` land in visual pixels,
  and each popover's content is zoomed forward again so it still renders at the desk's scale.
  Measured after the fix, the menu sat exactly on its anchor at 125%.

The window manager measures the desk in layout pixels, so at 125% fewer columns fit, and
"Fits readably" shows it.

### Text size is a multiplier on the type ramp

`--text-scale` (default 1) multiplies every type token (`--text-body: calc(13px * var(--text-scale))`)
and every literal `font-size` in the stylesheets and the panels' inline styles. That conversion is
mechanical: one pattern, no judgement calls. The two scale variables are declared in a plain `:root`
rule outside the theme blocks, so a swatch carrying its own `data-theme` inherits the operator's
scale rather than resetting it.

### Applying it

`Desk.razor` already owns the windowing module and the layout. After each render it compares the
settings with what it last applied and, when either scale moved, calls `applyScale(ui, text)`.
That sets the two variables on `<html>` and re-reports the viewport, because the desk's layout width
has changed.

## Out of scope

- An accent picker separate from the family: one accent per desk is the rule, and a family is where
  the accent comes from.
- Keyboard shortcuts for scale: Ctrl +/− already belong to the browser's own zoom, which still works
  on top of this.
