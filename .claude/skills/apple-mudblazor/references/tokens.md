# The token vocabulary

Every token names a **role**, never a colour. That is the whole reason a second
theme can be a second `:root` block instead of a component rewrite. If you find
yourself writing `--blue-500`, stop: the name has to survive being re-themed.

The values live in the first token block of `desk.css` and the theme blocks of `themes.css` — in the package folder's `staticwebassets/css/`, or `src/XLines.Desk/wwwroot/css/` in the Desk's repository (see the SKILL's "Where the system lives"). This file is the *why*.

---

## Surfaces — a true-neutral elevation ramp

```css
--surface-0: #1C1C1E;  /* the void — the ground everything sits on */
--surface-1: #232326;  /* panel and window body */
--surface-2: #2C2C2E;  /* title bars, raised rows, resting controls */
--surface-3: #38383A;  /* hover fills, pressed states */
```

**Why true-neutral.** These are Apple's own systemGray ramp read as surfaces —
systemGray6 *is* `--surface-0`, systemGray5 *is* `--surface-2`. Any warmth or blue
undertone fights the accent, which is why macOS keeps its chrome neutral. A ramp
that leans warm makes systemBlue look purple.

**Why four steps and not six.** Each step has a job you can say in one sentence.
A step you cannot name is a step somebody will use inconsistently.

Depth comes from **fill and shadow**, never from a border.

```css
--separator: rgba(255,255,255,.10);
--separator-strong: rgba(255,255,255,.16);
```

**One edge.** A group or card carries one hairline where it is defined; rows inside it are
divided by inset hairlines and padding, never framed again, and a button or field has no
outline at rest. A hairline *around* a thing inside an edged thing is the Material habit this system replaces.

---

## Text — opacity tiers, not fixed greys

```css
--text-primary:   rgba(255,255,255,.92);
--text-secondary: rgba(255,255,255,.55);
--text-tertiary:  rgba(255,255,255,.30);
```

**Why opacity rather than three hex greys.** Text sits on four surface tiers and
on four material tiers. A fixed grey tuned against `--surface-1` is wrong on a
translucent menu and wrong again on `--surface-3`. White at an opacity keeps its
*relationship* to whatever is underneath, so one token reads correctly everywhere.

**Measured contrast, against `--surface-1`:**

| Token | Ratio | Verdict |
|---|---|---|
| `--text-secondary` | **5.71:1** | passes AA |
| `--text-tertiary` | **2.70:1** | below AA — correct for its job |

`--text-tertiary` is for a hint, a placeholder, a unit nobody must read. A value
an operator has to read is not that job.

> **The rule: fix the call site, never brighten the de-emphasis token.**

When a cell an operator must read is rendering dim, move that cell to a readable
token. Raising `--text-tertiary` repairs one cell and flattens the hierarchy
everywhere else. In the migration this meant timestamps, durations, job keys and
credit counts moving off the dim class onto a readable one — a sweep of call
sites, not a one-line token change.

---

## One accent

```css
--accent: #086FD7;              /* systemBlue, the Standard dark desk's */
--accent-hover: #0973DE;
--accent-press: #0866C6;
--accent-ink: #449EFF;          /* the accent as text and as a 1px stroke */
--on-accent: #FFFFFF;
/* --accent-wash, --accent-wash-strong: derived from --accent, never declared (below) */
```

**Interactivity, focus, and selection — nothing else.** A second accent is how an
interface stops meaning anything: once two colours both mean "you can press this",
neither does.

`--on-accent` is the ink for **any saturated fill**, not only the accent one. A
destructive filled button is exactly as saturated, so it spends the same token
rather than restating white.

The `-wash` pair exists so "selected" has a fill rather than a border. Selection
is a wash; hover is a neutral surface step. See `components.md`.

**A wash is its fill thinned, so it is derived, not declared.** The Desk computes
every wash once, on `:root, [data-theme]`, with `color-mix` —
`--accent-wash: color-mix(in srgb, var(--accent) calc(var(--accent-wash-alpha) * 100%), transparent)`,
which for an opaque fill is exactly the fill at that alpha, and which browsers read well before
relative colour (`rgb(from …)`): a wash a browser cannot parse is transparent, and the focus ring
that holds it goes with it. Mixing multiplies an alpha the fill already has, so the one input
that is translucent — the sheen a dark key's finish is drawn from — keeps relative colour inside
`@supports (color: rgb(from white r g b))`, with the mix as the fainter fallback. A
block states only `--accent-wash-alpha` (or `-strong-alpha`, `--state-*-wash-alpha`)
where it departs from the appearance's default. Declared on the attribute rather
than `:root` alone, because a custom property inherits already resolved: a swatch
carrying its own `data-theme`/`data-accent` must recompute from its own fill.

---

## Semantic state — role-named, applied by judgment

```css
--state-positive: #30D158;
--state-negative: #D73A31;        /* systemRed deepened: the white label reads 4.63:1 */
--state-negative-hover: #C4342C;  /* deeper still: 1.17:1 from rest, 5.42:1 under the label */
--state-warning:  #FF9F0A;
--state-positive-wash / --state-negative-wash / --state-warning-wash
```

Apple's dark-mode system colours. Note the names: **positive/negative/warning**,
not green/red/orange. A state token is spent when a state genuinely needs a
colour, not as an always-on channel that tints every control.

`--state-negative-hover` exists so a destructive fill can answer the pointer the
way `--accent-hover` lets the accent one — a state colour, not a mix computed at
the call site. It travels away from its label, not by the ground: a red that
carries white has no room to lighten (a visible step lighter drops the label under
4.5:1), so it deepens even on a dark desk, and a red that carries dark ink lifts.
`ThemeContrastTests` holds every block to that, to 4.5:1 under the label on both
fills, and to a step of at least 1.1:1 between them, so the hover is seen.

**Hue is the second channel, never the only one.** A tag carries its word; a
metric carries its label and note; a toast carries its sentence. The state must be
legible with the colour removed, and the colour only sharpens a reading that
already works. That is both an accessibility requirement and a design one.

---

## Chart neutrals — and why they are opaque

```css
--chart-neutral: #8E8E93;      /* systemGray */
--chart-neutral-dim: #636366;  /* systemGray2 */
```

**This is the one place you must not reach for `--text-*`.**

Translucent series colours **composite against whatever is beneath them** —
gridlines, the plot background, an overlapping series. The same token then renders
as three different colours in one chart, and a legend swatch does not match the
line it labels. So chart series colours are opaque, fixed values.

The same reasoning is why series colours are handed to `MudChart` in **C#**:
MudChart writes them into SVG attributes and legend markup where a `var()` cannot
reach.

---

## Materials — four tiers, for things that float

```css
--material-ultrathin: rgba(44,44,46,.55);
--material-thin:      rgba(40,40,42,.70);
--material-regular:   rgba(36,36,38,.82);
--material-thick:     rgba(30,30,32,.92);
--material-blur: saturate(180%) blur(30px);
```

Mirrors HIG's material tiers, thinnest to thickest. The blur is an optical
constant, declared once with the derived tokens rather than in every block.

**Materials are for things that float** — menus, popovers, dialogs, sheets, fixed
chrome. **Anything holding data stays opaque**, because legibility beats depth: a
translucent panel puts whatever is behind it into the middle of a number the
operator is trying to read.

`backdrop-filter` is expensive on the GPU, which is the second reason to keep it
to the floating tier.

**Every material needs an opaque `@supports` fallback**, and that block must be
the last thing in your last stylesheet. `@supports` adds no specificity, so it
wins on source order alone. This is catalogued in `mudblazor-seam.md`; it is the
most easily-shipped bug in the system.

---

## Type

```css
--face-ui: -apple-system, BlinkMacSystemFont, 'Inter var', 'Inter', 'Segoe UI', system-ui, sans-serif;
```

**SF cannot be licensed off Apple platforms.** So: real SF via `-apple-system`
where it exists, self-hosted Inter (SIL OFL 1.1) everywhere else — the closest
legal match on metrics. Without the Inter fallback, Windows drops to Segoe UI and
the product looks like a different product on half your machines. See
the Desk's `fonts/` folder.

**One family for everything.** Apple sets numbers in SF too, so there is no second
"data" or "mono" face. A column of figures is this face plus
`font-variant-numeric: tabular-nums`.

### The ramp — HIG text styles, named for the job

```css
--text-largetitle: 26px;
--text-title1: 22px;
--text-title2: 17px;
--text-title3: 15px;
--text-headline: 13px;   /* same size as body, distinguished by WEIGHT */
--text-body: 13px;
--text-subheadline: 12px;
--text-footnote: 11px;
--text-caption: 10px;
```

A call site names the **style**, never the pixels. `--text-headline` sharing a size
with `--text-body` is the point: emphasis at body size is weight, not size, which
is how Apple does it and why the ramp does not need a size between 13 and 15.

These sizes are tuned for a dense operations console. Scale the whole ramp up for
a consumer app — but keep the style names, because the names are the interface.

```css
--leading-title: 1.25;  --leading-body: 1.45;  --leading-tight: 1.2;
--weight-regular: 400;  --weight-medium: 500;  --weight-semibold: 600;
```

Three weights. A fourth is a weight somebody will use to mean the same thing as
one of the other three.

---

## Spacing and shape

```css
--space-1: 4px … --space-16: 64px   /* one 4px ramp, steps skipped where the eye cannot tell */
--radius-sm: 5px;     /* chips, tags, inline marks */
--radius: 7px;       /* controls — buttons, fields, selects */
--radius-panel: 12px; /* windows, panels, popovers */
--radius-sheet: 14px; /* sheets and alerts */
```

**Radius grows with the surface.** Apple's radii are larger than Material's, and a
sheet at the same 7px as a button reads as a very big button. The four steps map
to four sizes of thing; if you cannot say which of the four a new element is, it
is probably one of the four already.

The spacing ramp skips steps (no 20px, no 40px) on purpose: every gap is a **named
rung**, so a layout re-tunes in one place.

```css
--radius-xs: 4px;     /* the plate inside a control: a tab's marble, a hover plate */
--control-h: 28px;    /* the toolbar control; -sm 24px (inside a grid row), -lg 34px */
--row-h: 36px;        /* a settings row's floor */
--cell-pad-y: 4px;    /* a table cell, top and bottom; --cell-inset 10px at the sides */
--face-display: var(--face-ui); /* the titles' face */
```

**The operator can rescale these** (ADR 0030): Shape (`data-shape`: sharp, round) moves the five
radii together; Density (`data-density`: compact, roomy) moves the ramp, `--titlebar-h`,
`--footer-h` and the heights above; Typeface (`data-font`: humanist, serif, mono) moves the two
faces. They live in a plain `:root` block after the theme blocks, never in one. So: **never write
28px, 24px or 34px for a control** — spend `--control-h` and its two siblings, or the control
stays put when the desk grows. Compact never shortens a target; a circle (switch, radio, dot,
swatch) is 50% or 999px and no shape touches it. Titles spend `--face-display`.

---

## Motion

```css
--dur-fast: .15s;  --dur-base: .25s;  --dur-slow: .35s;
--ease-standard: cubic-bezier(.25,.1,.25,1);
--ease-spring:   cubic-bezier(.32,1.28,.42,1);
--ease-exit:     cubic-bezier(.4,0,1,1);
--ease-enter:    cubic-bezier(0,0,.58,1);

/* the loops: ambient signals, never a response to a press */
--dur-spin: .8s;  --dur-sweep: 1.15s;  --dur-shimmer: 1.5s;  --dur-breathe: 1.9s;
```

**Motion says where a thing came from.** A sheet rises from below and settles; a
popover scales out of its anchor; a control settles under a press. Nothing moves
for decoration.

- `--ease-standard` — the default for a state change.
- `--ease-spring` **overshoots a hair and settles**, approximating the platform
  spring. Use it for anything arriving or travelling to a position (a sheet, a
  segmented control's cap).
- `--ease-enter` decelerates without overshoot: a small arrival in place, such as an
  answer appearing under the row that asked for it.
- `--ease-exit` does **not** overshoot, because things leaving should not draw a
  second look.

> **These are interaction motion tokens. They do not apply to continuous ambient
> signals.**

A busy sheen, a live pulse, a ticker — those run on the loop tokens (`--dur-spin`,
`--dur-sweep`, `--dur-shimmer`, `--dur-breathe`), never on a literal. Sizing an
always-on animation from an interaction token makes it read as a control
responding to something, which is exactly the wrong message.

Everything stops under `prefers-reduced-motion`. That block is the **one
legitimate `!important`** in the system: a preference a component can out-specify
is not a preference.

---

## Depth and focus

```css
--shadow-1 … --shadow-3, --shadow-sheet
--focus-ring: 0 0 0 3px var(--accent-wash-strong), 0 0 0 1px var(--accent-ink);
```

The 1px stroke is the accent's ink, not its fill: a fill as a UI colour can fall under 3:1 on a
raised surface, and every ink is held to 4.5:1 there.

Three shadow tiers plus a sheet tier, because a surface is either resting,
floating, or modal. (MudBlazor's 24 elevation steps are collapsed onto these four
in the bridge.)

**One focus ring, everywhere, accent-coloured** — Apple's own convention. It sits
**outside** the control on an offset, which is what lets an accent ring survive on
an accent-filled button. A ring drawn inside an accent surface vanishes.

Never paint a ring on something that does not have focus. See `modality.md`.

---

## The second theme

A light mode is one more block. The Desk's `themes.css` ships it, keyed on
`[data-theme="apple-light"]` beside `[data-theme="apple-dark"]` — and the same pair again for
each other family (ayu, catppuccin, contrast, dracula, everforest, gruvbox, kanagawa, midnight,
monokai, night-owl, nord, obsidian, one, palenight, paper, primer, rose-pine, solarized, synthwave,
terra, tokyo-night, zenburn: twenty-three families, filed under Built in, Character, Soft and Vivid) — and
no component, selector or C# call
site knows how many there are. If adding one requires touching anything below the token blocks,
the token that made it necessary was named for a colour.

**Redeclare every token whose value is a colour, and nothing else.** The type ramp,
spacing, radii, motion curves and z-layers are not theme-dependent; they stay in
`:root` and fall through. `--focus-ring` falls through too, and that one is the
proof the vocabulary is right: it is written entirely in `var()`s, so it
re-resolves to the light accent without being mentioned twice — on `<html>`, where the block is
stamped. It is declared on `:root`, so a swatch carrying its own `data-theme` inherits `<html>`'s
ring already resolved.

**The surface ramp re-derives; it does not invert.** A light app cannot have a
ground lighter than white, so it uses the arrangement macOS and iOS actually use —
grey ground, white content on top, interactive tiers darkening *away* from the
panel. `--surface-0` ends up lighter than `--surface-2`, which reads out of order
until you remember these are roles rather than a lightness scale.

| | dark | light |
|---|---|---|
| `--surface-0` the ground | `#1C1C1E` | `#F2F2F7` |
| `--surface-1` panel body | `#232326` | `#FFFFFF` |
| `--surface-2` resting control | `#2C2C2E` | `#E5E5EA` |
| `--surface-3` hover, pressed | `#38383A` | `#D1D1D6` |

**Hover and press travel the other way.** In dark mode prominence is lightness, so
`--accent-hover` is lighter than `--accent`. Against a white page a blue that
lightens under the pointer reads as *fading*, so in light mode both states deepen.
The names still mean "more prominent" — the direction that delivers it is a
property of the ground, not of the token. `--state-negative-hover` is the one
exception: it follows its label rather than its ground (above).

**Washes come down, shadows come down further.** A tint over white composites to a
far more saturated result than the same tint over near-black, so `--accent-wash`
drops from `.15` to `.12`. Shadows keep their geometry and thin their ink to
roughly a quarter: dark mode's alphas exist to be seen against near-black, and
carried onto white they stop reading as depth and start reading as dirt.

**Three values are genuinely shared, and each has a reason.** `--on-accent` is the
ink for any saturated fill, and a filled systemBlue button is exactly as saturated
either way. `--chart-neutral` is systemGray, the one step of Apple's ramp that
reads on both grounds — which is why Apple gives it no light/dark variant.
`--knob` is a switch's thumb, white in either appearance. (`--material-blur`, an
optical constant, is not in either block: it is derived once.) Anything else that
survived the mirror unchanged is a copy-paste.

**A scrim is the one thing that does not mirror.** The dark scrim is `--surface-0`
at ~60%, because dimming a near-black app means laying more near-black over it.
The analogous light value would *brighten* the app behind a sheet. A scrim
subtracts attention, and on any ground that means going darker: use neutral black,
at a lower alpha, because black over white bites far harder.

### Contrast, both ways

Redo the arithmetic; do not assume the light tier inherits the dark one's pass.
Composite the translucent ink onto the surface in gamma-encoded sRGB *first*, then
linearise — doing it the other way round is a plausible-looking mistake that shifts
every ratio you measure.

| Token over `--surface-1` | dark | light | verdict |
|---|---|---|---|
| `--text-secondary` | 5.71:1 | **5.74:1** | passes AA, both |
| `--text-tertiary` | 2.70:1 | 2.11:1 | below AA — correct for its job |

Aim for the two themes landing *close*, not merely both passing. Two themes that
de-emphasise by different amounts make the same cell read as two different grades
of important depending on the hour.

### Test the mirror mechanically

A token the light block forgets does not fail anywhere. It silently keeps its dark
value, so one near-black surface renders into a light app and nothing says so. This
is the same class of defect as a rule losing the cascade, and grep does not find it
either — a maintained list of tokens to check goes stale at the twenty-ninth one.

Parse both blocks and assert three things:

1. every colour-valued name in the dark block appears in the light one;
2. no name appears in the light block that the dark one lacks (the same defect
   pointed the other way, and the direction a one-way check misses);
3. every mirrored value actually **changed**, with a short allow-list for the
   three that are deliberately shared.

Then compute the contrast ratios in the test rather than tabulating them, so a
token nudged by two hundredths of an alpha fails instead of being re-measured by
hand and forgotten.

### Wiring it up — as the Desk does it

The stylesheet is the whole mechanism: `data-theme` on `<html>` re-themes the app. Around it:

- **`ThemeService`** (scoped, in the Desk's `Theming` namespace) holds the choices — `Mode`
  (System / Dark / Light), `Family`, an `Accent` per family, `Glass` (Clear / Tinted) and the four
  makes `Shape`, `Density`, `Backdrop`, `Typeface` (ADR 0030) — and derives `Attribute`
  (`DeskThemeFamilies.Attribute(ShowingFamily, IsDark)`) and `MudTheme`
  (`DeskTheme.For(ShowingFamily, AccentAttribute)`). `MainLayout` hands both to `MudThemeProvider`,
  so MudBlazor and the stylesheet always flip from the one boolean.
- **`DeskThemeFamilies.Catalog`** is the one list of families: member, name, slug, picker heading,
  palettes, accents (its own first). The picker, `DeskTheme` and every per-family test read it.
- **`theme.js`** reads and writes the choices in `localStorage` (`desk.theme`,
  `desk.theme.family`, `desk.accent.<slug>` — Standard falling back to the old `desk.accent` —
  `desk.glass`, `desk.shape`, `desk.density`, `desk.backdrop`, `desk.font`), keeps a `matchMedia`
  listener so System stays live, and sets `data-reduced-transparency` from the OS setting,
  `data-glass="tinted"` from the glass (never under Reduce Transparency, which wins) and
  `data-shape`/`-density`/`-backdrop`/`-font` from the makes. **`theme-boot.js`** runs before first
  paint and stamps the stored attributes, so the one frame of the wrong theme is never seen.
- **The mirror is tested.** `DeskThemeTests` parses every family's two blocks and holds them to
  the Standard dark block's colour-token list, its C# palette and its text contrast, every accent
  block to its four tokens and its catalogue row, the derived tokens to being declared once and
  resolving to their fill at their alpha, and every make's blocks to its enum;
  `ThemeContrastTests` holds the inks to 4.5:1 in every family and accent; `ThemeBootTests` holds
  the boot script's tables to the catalogue and the enums. Add a token to one block and the test
  names the blocks that lack it.

`<meta name="color-scheme" content="dark light">` and a `color-scheme` declaration in each
block keep the browser's own scrollbars and canvas in the right theme.

Three more things follow the theme, and none adds a token:

- **`theme-color`** — `DeskHead` declares the meta empty; the boot script and every `theme.js`
  stamp fill it from the computed `--surface-0` of the showing block. Read back, never tabled in
  JS, so a new family needs nothing here.
- **`::selection`** is `--accent-wash-strong` behind `--text-primary` (selection is a wash, as
  everywhere), held to 4.5:1 on every desk and accent by `DeskThemeTests`; the caret is
  `--accent-ink`, because a one-pixel stroke is read as text is — as the focus ring's stroke and an
  open key's ring are. `--accent` is a fill, never a line.
- **The switch cross-fades.** An operator's change is wrapped in a view transition on
  `--dur-base`/`--ease-standard`; the first paint and the machine's own changes are instant, a
  choice that moves no attribute on `<html>` opens none, the hold lets go shortly after the settle
  even on a desk that never goes still, and the reduced-motion block stills `::view-transition-*`
  as it stills everything else.

### Forced colours

A Windows contrast theme is not a fourth family: the browser replaces every colour with the
operator's system palette and drops shadows and gradients — which is where the desk keeps its
focus ring (`--focus-ring` is a box-shadow) and its selection (a wash). One
`@media (forced-colors: active)` block at the end of `desk.css` restores the *distinctions*, not
the looks: the ring as a 2px `Highlight` outline, "on" as `Highlight` with `HighlightText`
(system colours always in their pairs), a 1px system edge on controls and cards — the one
sanctioned outline at rest, because a control with neither fill nor edge on `Canvas` is not a
control — `GrayText` for disabled, `LinkText` for links, opaque materials. It wins by weight
(`:root`, and an id's weight on the focus rule), never `!important`, and names no theme. Only
the theme and accent swatches opt out (`forced-color-adjust: none`), because there the colour is
the information.

---

## Layers

```css
--z-chrome: 8000;  --z-popover: 9000;  --z-modal: 9500;  --z-toast: 9800;
```

Deliberately low at the base level so anything floating has room. MudBlazor's own
z-indexes sit in the 1000s and are re-pointed at these four in the bridge, so keep
the four names whatever else you change.
