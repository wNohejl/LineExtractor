# The token vocabulary

Every token names a **role**, never a colour. That is the whole reason a second
theme can be a second `:root` block instead of a component rewrite. If you find
yourself writing `--blue-500`, stop: the name has to survive being re-themed.

The values live in `templates/apple-tokens.css`. This file is the *why*.

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

**Hairlines separate; they never outline.** A border around a panel or a button
is the Material habit this system replaces. A hairline is legitimate *between*
two things and illegitimate *around* one thing.

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
--accent: #0A84FF;              /* systemBlue, dark */
--accent-hover: #3D9BFF;
--accent-press: #0871DB;
--accent-wash: rgba(10,132,255,.15);
--accent-wash-strong: rgba(10,132,255,.24);
--on-accent: #FFFFFF;
```

**Interactivity, focus, and selection — nothing else.** A second accent is how an
interface stops meaning anything: once two colours both mean "you can press this",
neither does.

`--on-accent` is the ink for **any saturated fill**, not only the accent one. A
destructive filled button is exactly as saturated, so it spends the same token
rather than restating white.

The `-wash` pair exists so "selected" has a fill rather than a border. Selection
is a wash; hover is a neutral surface step. See `components.md`.

---

## Semantic state — role-named, applied by judgment

```css
--state-positive: #30D158;
--state-negative: #FF453A;
--state-negative-hover: #FF6961;
--state-warning:  #FF9F0A;
--state-positive-wash / --state-negative-wash / --state-warning-wash
```

Apple's dark-mode system colours. Note the names: **positive/negative/warning**,
not green/red/orange. A state token is spent when a state genuinely needs a
colour, not as an always-on channel that tints every control.

`--state-negative-hover` exists so a destructive fill can lighten on hover the way
`--accent-hover` lets the accent one — a lit state colour, not a mix computed at
the call site.

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

Mirrors HIG's material tiers, thinnest to thickest.

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
`templates/fonts/README.md`.

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
--radius-sm: 5px;    /* chips, tags, inline marks */
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

---

## Motion

```css
--dur-fast: .15s;  --dur-base: .25s;  --dur-slow: .35s;
--ease-standard: cubic-bezier(.25,.1,.25,1);
--ease-spring:   cubic-bezier(.32,1.28,.42,1);
--ease-exit:     cubic-bezier(.4,0,1,1);
```

**Motion says where a thing came from.** A sheet rises from below and settles; a
popover scales out of its anchor; a control settles under a press. Nothing moves
for decoration.

- `--ease-standard` — the default for a state change.
- `--ease-spring` **overshoots a hair and settles**, approximating the platform
  spring. Use it for anything arriving or travelling to a position (a sheet, a
  segmented control's cap).
- `--ease-exit` does **not** overshoot, because things leaving should not draw a
  second look.

> **These are interaction motion tokens. They do not apply to continuous ambient
> signals.**

A busy sheen, a live pulse, a ticker — those run on their own duration. Sizing an
always-on animation from an interaction token makes it read as a control
responding to something, which is exactly the wrong message.

Everything stops under `prefers-reduced-motion`. That block is the **one
legitimate `!important`** in the system: a preference a component can out-specify
is not a preference.

---

## Depth and focus

```css
--shadow-1 … --shadow-3, --shadow-sheet
--focus-ring: 0 0 0 3px var(--accent-wash-strong), 0 0 0 1px var(--accent);
```

Three shadow tiers plus a sheet tier, because a surface is either resting,
floating, or modal. (MudBlazor's 24 elevation steps are collapsed onto these four
in the bridge.)

**One focus ring, everywhere, accent-coloured** — Apple's own convention. It sits
**outside** the control on an offset, which is what lets an accent ring survive on
an accent-filled button. A ring drawn inside an accent surface vanishes.

Never paint a ring on something that does not have focus. See `modality.md`.

---

## Layers

```css
--z-chrome: 8000;  --z-popover: 9000;  --z-modal: 9500;  --z-toast: 9800;
```

Deliberately low at the base level so anything floating has room. MudBlazor's own
z-indexes sit in the 1000s and are re-pointed at these four in the bridge, so keep
the four names whatever else you change.
