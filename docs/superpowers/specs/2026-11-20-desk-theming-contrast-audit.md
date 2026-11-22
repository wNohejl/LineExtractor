# The Desk's themes against the live HIG — a contrast and styling audit

**Date:** 2026-11-20
**Status:** Written as research: every number below was measured from `desk.css` and
`themes.css` as they stood on `DesignImprovements` on this date, before any of it was acted on.
Several recommendations have since landed (below), so §3 and the appendix tables are the
before-state, not the desk as it now draws.
**Builds on:** ADR 0016 (themes are token blocks), the HIG audits of 25 October and 17 November
(the `-ink` tokens, the on-accent fills), `ThemeContrastTests`, `DeskThemeTests`, and the
Tinted glass (`fa5eab7`).

## Where it stands (2026-11-21)

Read from the tree, not from memory; the recommendation numbers are §4's.

| # | Recommendation | State | Held by |
|---|---|---|---|
| 1 | Switch readable when off | Landed, as the `--knob` token in every block with a rim drawn inside the disc | `ControlContrastTests` |
| 2 | Focus stroke in `--accent-ink` | Landed; the caret and the open popup and window-tab keys' rings followed on 21 November | `ControlContrastTests`, `DeskCssConventionTests` |
| 3 | Secondary at 4.5:1 on every surface | Landed, with the alphas of T7 | `ThemeContrastTests` |
| 4 | Inks on their own washes | Landed | `ThemeContrastTests.Every_ink_passes_over_its_own_wash` |
| 5 | Concentric radii | Not landed (no `--radius-inset`) | — |
| 6 | Test the destructive Filled key | Landed, and since 21 November it also holds the hover to a visible step (≥1.1:1) that travels away from its label | `ThemeContrastTests` |
| 7 | Chart series on inks | Not landed (`DeskPalette` carries no inks) | — |
| 8 | No scroll edge effect | Nothing to change; the `.grid th { top: -14px }` slit is still unchecked in a browser | — |
| 9 | Derived Increase Contrast per family | Not landed; Standard alone has the block | — |
| 10 | Destructive fill at 4.5:1 | Landed (Standard dark #D73A31, light #DF342A, Tokyo Night light #E1275D, Nord light #B45C64, and lifted reds on the pale-label dark desks). On Standard and Solarized dark the hover deepens (#C4342C, #C52C29), the trade this item foresaw; every negative wash is its fill thinned | `ThemeContrastTests` |
| 11 | Stronger light hairlines | Not landed | — |
| 12 | Field edges | Not landed | — |
| 13 | Re-step the steep dark ramps | Landed (Catppuccin dark s3 #3B3C4F, Gruvbox dark #46403E) | `ThemeContrastTests` |
| 14 | Tinted key's hover | Landed by lifting the inks; the Tinted hover still spends `--accent-wash-strong` | `ThemeContrastTests` |
| 15–18 | Ground lift, primary at 7:1, Inter tracking, fill tiers | Not landed | — |

## 1. What was measured, and how

Eighteen token blocks: nine families (Standard, Nord, Solarized, Rosé Pine, Catppuccin,
Gruvbox, Tokyo Night, Dracula, High contrast), each dark and light, parsed straight from the
top-level `[data-theme="…"]` blocks the way `ThemeContrastTests.ThemeBlocks()` parses them.
Every ratio is WCAG 2.x; a translucent colour is composited onto its surface in gamma-encoded
sRGB first and linearised after, exactly as `ThemeContrastTests.Contrast` does, so a number here
and a number in a test agree. Tinted glass is computed as the stylesheet writes it:
`color-mix(in srgb, <material>, var(--surface-2) 50%)` with premultiplied alpha, then over
`--surface-0`. The full tables are Appendix A; the sections below quote from them.

What the tests already hold (so nothing below re-proposes it): every `-ink` at 4.5:1 over
`--surface-1` **and** `--surface-2`; `--on-accent` at 4.5:1 over `--accent`, `-hover` and
`-press` (chosen accents included); secondary at 4.5:1 and tertiary under it, **over
`--surface-1` only**; High contrast's secondary at 7:1; Standard's Increase Contrast values; the
10px type floor; the spring kept for dialogs and sheets; no text on a fill token; Reduce
Transparency covering every material fallback.

## 2. The live HIG, as read today

Every page was fetched as Markdown (`<page>.md`) on 2026-11-20.

| Page | Last change-log entry | What is specific and actionable for this desk |
|---|---|---|
| Color | 16 Dec 2025 (Liquid Glass) | Supply light, dark **and an increased-contrast variant of every custom colour**, "significantly" more differentiated. macOS defines label, secondary, tertiary and **quaternary** label, separator, grid, control, control background, selected-content and unemphasized-selection colours. On glass: colour sparingly, on the *background* of the one primary action, never on several controls. Don't reuse a semantic colour for another job (separator as text, secondary label as a fill). |
| Dark Mode | 6 Aug 2024 | **No lower than 4.5:1; for custom foreground/background colours strive for 7:1, especially in small text.** Test with Increase Contrast and Reduce Transparency, separately and together. Elevated backgrounds are brighter so foreground layers advance. |
| Materials | 9 Sep 2025 | Liquid Glass is for the controls and navigation layer only, never content; use it sparingly. Regular glass for anything with much text (popovers, sidebars, alerts); clear glass only over rich media, with a **35% dimming layer** if what is under it is bright. Use vibrant (label-tier) colours on materials; thicker materials for fine text. |
| Typography | 16 Dec 2025 (emphasized weights) | macOS styles: Large Title 26/32, Title 1 22/26, Title 2 17/22, Title 3 15/20, Headline 13/16 **Bold**, Body 13/16, Callout 12/15, Subheadline 11/14, Footnote 10/13, Caption 10/13. Emphasized: titles Bold, Title 3–Footnote Semibold. macOS tracking: 13pt −6/1000 em, 15pt −16, 17pt −26, 20pt −23, 22pt −12, 26pt +8. Default 13pt, minimum 10pt. |
| Layout | 9 Sep 2026 | Group with space, container shapes or separators. **Differentiate controls from content with glass and a scroll edge effect rather than a solid or semi-opaque fill under controls.** Nothing critical at a window's bottom edge. |
| Accessibility | 9 Jun 2025 | WCAG AA as the Accessibility Inspector's bar: up to 17pt 4.5:1; 18pt 3:1; **bold at any size 3:1**. If the default does not meet it, Increase Contrast must. macOS controls 28×28 default, 20×20 minimum; ~12pt padding around bezelled controls. Not colour alone. Let people customise chart colours. |
| Motion | 9 Sep 2025 | Purposeful, brief, cancellable; avoid motion on interactions that happen all the time; subdued for pointer input. |
| Designing for macOS | 28 Oct 2026 (touch) | Large displays: more content, fewer levels, comfortable density; let people choose colours and fonts. |
| Buttons | 16 Dec 2025 | One or two prominent buttons a view; style not size distinguishes the preferred action; destructive role uses system red; always a press state. |
| Lists and tables | 21 Jun 2023 | macOS: sortable, resizable columns; **alternating row colours are a "consider"** for wide multicolumn tables. |
| Toolbars | 16 Dec 2025 | Reduce toolbar backgrounds and tinted controls; use a scroll edge effect to separate; **custom components' corner radii concentric with the bar's**; one prominent action, trailing; at most three groups. |
| Scroll views | 8 Jun 2026 | **A scroll edge effect only where a scroll view is behind floating elements — never decorative.** Automatic style is the more opaque one for dense toolbars and **pinned table headers**. One per view; equal heights across panes. |
| Sidebars | 8 Jun 2026 | Icons take the accent unless a fixed colour carries meaning; nothing critical at the bottom. |
| Charting data | 23 Sep 2022 | Consistent styles across related charts; continuity of colour for one dataset. (Silent on palette contrast; Accessibility carries that.) |
| Branding | 9 Sep 2026 | Accent judiciously: primary actions and status, not every control; brand colour belongs in the content layer. |
| Icons | 9 Jun 2025 | Consistent size, stroke and weight; match icon weight to adjacent text; optical centring. |

## 3. Findings

### 3.1 Text tiers on the surfaces they actually sit on

The tests hold secondary text over `--surface-1`. It is also set on `--surface-0` (the gate's
track, the window strip's channel), `--surface-2` (title bars, metric tiles, the sticky table
header, hover rows, notes) and `--surface-3` (the neutral `.tag--info`, `.badge` at caption
size, MudChip's neutral capsule in `mud-bridge.css`). Over `--surface-3`, nine blocks fall under
4.5:1 (T1):

| Block | S/s3 | Note |
|---|---|---|
| catppuccin-dark | **3.75** | the steepest s2→s3 step on the desk (1.38 against Standard's 1.19) |
| gruvbox-dark | **3.88** | second steepest (1.31) |
| tokyo-night-light, gruvbox-light | **4.26** | |
| rose-pine-light | **4.29** | primary is already 100%, so the ink has no room |
| nord-dark | **4.31** | |
| dracula-dark | **4.36** | |
| catppuccin-light | **4.42** | primary already 100% |
| solarized-dark | **4.47** | |

Over `--surface-2` every block clears 4.5 (lowest rose-pine-light 4.59); over `--surface-0`
every block clears it (lowest rose-pine-light 4.85). The `.badge` case is the worst reading on
the desk: 10px semibold secondary on `--surface-3`, exactly the "small text" the HIG asks 7:1
of.

Primary against the 7:1 aim: every block clears 7:1 over the panel (rose-pine-light 7.00 and
catppuccin-light 7.06 sit on the line). Over `--surface-2` four light families drop under it
(rose-pine-light 5.68, catppuccin-light 6.04, tokyo-night-light 6.19, gruvbox-light 6.90); over
`--surface-3` six blocks do. Secondary at 7:1 is out of reach without collapsing the hierarchy:
it would need alphas of .63 (Standard dark) to 1.0 (Rosé Pine, Catppuccin light) (T7).

### 3.2 Controls that the tests never see

- **The switch's thumb disappears when off.** `.toggle__track` rests on `--surface-3` and the
  thumb is `--on-accent`. Where `--on-accent` is dark (every pale-accent dark family) the thumb is
  a dark disc on a dark track: tokyo-night-dark **1.27**, rose-pine-dark **1.40**, nord-dark
  **1.45**, gruvbox-dark **1.67**, contrast-dark **1.66** (black on #333), catppuccin-dark 1.80,
  dracula-dark 1.80. On every light desk it is a white disc on pale grey (1.38–1.88) with no
  shadow. Only Standard dark (11.70) and Solarized dark (10.98) show it. The off track against
  the panel is 1.27–1.88 everywhere (T8). Position is the only state signal a switch has, so the
  thumb is the essential graphic (WCAG 1.4.11, 3:1).
- **The destructive Filled key's label.** `--on-accent` on `--state-negative` is not tested.
  It fails 4.5:1 in eight blocks, and Standard dark's **hover measures 2.82**, under even the
  HIG's 3:1 for bold text: apple-dark 3.41/2.82, apple-light 3.55, nord-dark 3.05/3.97,
  nord-light 4.09, solarized-dark hover 3.76, rose-pine-light 4.19, gruvbox-dark 4.29,
  tokyo-night-light 3.89 (T2, T10). On dark desks the hover lightens, which is the direction that
  makes it worse.
- **Inks on their own washes.** A tag is an ink on its wash; the current window key is
  `--accent-ink` on `--accent-wash` inside the `--surface-0` channel; a Tinted key's hover is
  `--accent-ink` on `--accent-wash-strong`. Twenty-two cells fall under 4.5 (T10). Most need a
  step of .02–.04 toward the primary ink. The worst four are all a Tinted key under the pointer
  on a dark desk: dracula-dark **3.64**, nord-dark **3.68**, gruvbox-dark **3.76** and
  solarized-dark **3.96**. The first three need a step of .32–.50.
- **Field edges.** A `.field` is a `--surface-0` well with a `--separator` border on a
  `--surface-1` panel. Its edge measures 1.15–1.48 in every non-High-contrast block, 1.24 on
  Standard dark (T3); Standard under Increase Contrast reaches only 2.02. MudBlazor's outlined
  inputs are edged in opaque `--surface-3` (`mud-bridge.css`), 1.27–1.88. WCAG 1.4.11 asks 3:1
  of a boundary needed to find a control. Apple's own text fields do not reach 3:1 either, so
  this one is a judgment (§4.2).
- **The accent as a UI colour.** The focus ring's 1px stroke is `--accent`. Over the panel,
  Standard dark is 3.18 and Solarized dark 3.03 (both just over 3:1); over `--surface-2` (title
  bars, tiles, grouped forms) they are **2.83** and **2.63**. Every `-ink` already clears
  4.5:1 over both surfaces by test.

### 3.3 Hairlines and the elevation ramp

`--separator` over the panel measures 1.19 (catppuccin-light) to 1.36 (Standard dark).
Measured the same way, Apple's UIKit separator is 1.59 (dark) and 1.70 (light) (T6). The Desk's
.10 alpha is, to my knowledge, AppKit's own `separatorColor` (10% black or white), so it is
macOS-faithful but iOS-faint. It is the system's only divider ("one hairline per group"), and
in the light families it does the least work: catppuccin-light 1.19, tokyo-night-light 1.22,
rose-pine-light 1.23, gruvbox-light 1.23, nord-light 1.24, Standard light 1.25. In High
contrast it is 3.51/2.85, which is the job that family is for.

Surface steps (T3), with Standard as the reference (dark 1.09 / 1.12 / 1.19, light 1.12 / 1.26 / 1.21):

- **Too flat between ground and window** (s0↔s1): tokyo-night-dark 1.05, nord-dark 1.06,
  contrast-dark 1.06, rose-pine-dark 1.07, catppuccin-dark 1.07. A window parts from the ground
  by shadow alone there, and shadow does not read well on near-black.
- **Too flat between panel and raised** (s1↔s2): rose-pine-dark 1.09, apple-dark 1.12,
  solarized-dark 1.15. A title bar barely parts from its body.
- **Too steep at the top** (s2↔s3): catppuccin-dark 1.38, gruvbox-dark 1.31. This is what costs
  them secondary text on `--surface-3`.
- **Too flat at the top**: tokyo-night-dark 1.08, rose-pine-light 1.08. Hover is barely visible.

### 3.4 Chrome on materials, Clear and Tinted

At rest every material sits over `--surface-0`, and secondary text clears 4.5:1 on all four in
every block (lowest rose-pine-light 4.85 on thick). Tinted lowers secondary by 0.1–0.36 at rest
(it mixes toward `--surface-2`, which is lighter on a dark desk), raises the materials' alpha
from .70/.82 to about .85/.91, and leaves separators where they were (±0.01) (T5). Over the worst
thing a 30px blur could leave under a drawer or popover, a solid accent fill, secondary drops to
4.33 (nord-dark) and 4.42 (rose-pine-light) in Clear; Tinted brings those to 4.58 and 4.50
(T10). So Tinted needs no darker text. The case for it carrying the Increase Contrast hairline
is a matter of taste, not measurement.

The header is glass over a flat `--surface-0`. Nothing scrolls under it: `.shell` stacks the
header above the windows. The thin material there is safe (secondary 5.1–5.7) but has nothing
to show through.

### 3.5 Chart series

`DeskChartPalette` passes the state **fills** and the accent fill as series colours. Against the
panel (3:1 for a graphical object): on six light desks the positive or warning fill measures
2.16–2.98 (Standard light green 2.22, orange 2.20; Rosé Pine light gold 2.16; Gruvbox light
yellow 2.19). `--chart-neutral` is under 3:1 in solarized-light 2.48, catppuccin-light 2.83 and
rose-pine-light 2.87. `--chart-neutral-dim` is under 3:1 in sixteen of eighteen blocks (1.56–2.79),
which is right for a reference line but wrong as a data series. With the inks in place of the
fills, the worst series in any block is 5.32:1 (T10). `DeskPalette` carries no inks today.

### 3.6 Shape: concentric radii

The HIG's rule (Toolbars, Dec 2025) is that inner radius = outer radius − inset. Measured on the
real nested shapes:

| Container (radius, inset) | Child | Child radius | Concentric radius |
|---|---|---|---|
| `.bar__strip` (`--radius` 7, `--space-1` 4) | `.bar__key` | `--radius-sm` 5 | **3** |
| `.bar__menu-panel` (`--radius-panel` 12, 4) | `.bar__key` row | 5 | **8** |
| `.popover__panel` (12, 4) | `.menu__item` | 5 | **8** |
| `.popover__panel` (12, 4) + `.launcher` (6) | `.launcher__item` | 4 | **2** |
| `.marble-row.gate` (7, 2) | `.gate__cap` | `calc(var(--radius) - 2px)` = 5 | 5, already right |

### 3.7 Type

The ramp follows macOS sizes from Large Title to Body. The small rungs run one step large
(subheadline 12 and footnote 11, where macOS has 11 and 10), which is reasonable for Inter on
Windows. Titles are set at semibold with `letter-spacing: 0`. On a Mac, SF tracks itself. Inter
does not: at 17–22px it reads loose next to SF's −26 to −12/1000 em. The HIG's emphasized title
weight is Bold. The house rule of three weights keeps titles at semibold, and that rule should
win. `--weight-bold` is declared and spent once (`.desk-empty__mark`), so the desk actually has
four weights.

### 3.8 Outliers, family by family

- **Catppuccin dark, Gruvbox dark:** the top of the ramp is too steep, so secondary drops to
  3.75 and 3.88 on `--surface-3`, primary drops to 5.6–5.7 there, and the off switch thumb is
  invisible.
- **Rosé Pine light, Catppuccin light:** primary is at 100% and only just reaches 7:1 on the
  panel. These are the lowest-contrast light desks on every surface, and their hairlines are the
  faintest (1.19, 1.23). The palette's own ink is the ceiling.
- **Tokyo Night dark:** the flattest ground step (1.05), the flattest hover (1.08), and the
  lowest switch thumb (1.27).
- **Solarized dark, Standard dark:** the accent is a marginal UI colour (3.03 and 3.18 on the
  panel, 2.63 and 2.83 raised). Solarized dark's positive and warning tags are 4.18 and 4.27 on
  their washes.
- **Dracula dark, Nord dark, Gruvbox dark:** the Tinted key's hover is 3.64–3.76.
- **Standard (both):** the destructive Filled key is 3.41 (dark) and 3.55 (light) at rest, 2.82 on hover in dark.
- **High contrast:** it passes everything it claims to. Its light block's primary over `--surface-3` is
  11.18 and its hairlines 2.85 and 6.20. The field edge clears 3:1 only here.

## 4. Recommendations

Ranked by visible improvement per unit of risk. Each says what to change, the number behind it,
the house rule it has to respect, and the test that would hold it.

### 4.1 Safe polish

1. **Make the switch readable when off.** Today the thumb is 1.27–1.88 against its track in 16
   of 18 blocks. Give the off thumb a shadow that defines it on every ground (`--shadow-1` plus
   the hairline drawn as a shadow, not a border) and, where `--on-accent` is dark, a light thumb
   when off. The simplest token-only form is `.toggle__thumb { background: var(--text-secondary) }`
   off and `--on-accent` on, which measures ≥3.75 against the track everywhere (T1, S/s3). That
   spends a label colour as a fill, which the HIG's Color page warns against. A new
   `--knob` token in every block is the clean alternative, and the choice between them is the
   owner's. *House rules:* one
   hairline (a shadow ring, not a border); no literal colours. *Test:* in every block, the thumb
   against the off track and against the on track is ≥3:1.
2. **Draw the focus ring's stroke in `--accent-ink`.** `--focus-ring` and `--focus-ring-inset`
   become `… 0 0 0 1px var(--accent-ink)`, and `.field:focus` takes `border-color:
   var(--accent-ink)`. Before: 2.63 (Solarized dark) and 2.83 (Standard dark) over `--surface-2`.
   After: ≥4.5 over `--surface-1` and `--surface-2` in every block, which the existing ink tests
   already guarantee. This is one edit in `desk.css`'s root block, and every family re-resolves
   it. *House rules:* still one accent and one ring. *Test:* `--focus-ring` references
   `--accent-ink`, plus a contrast check of the ring stroke against s1 and s2 at 3:1.
3. **Hold secondary text to 4.5:1 on every surface it is set on, and lift the seven near-misses.**
   Extend `Every_desk_keeps_the_text_hierarchy` from `--surface-1` to `--surface-0…3`. The
   alphas that clear `--surface-3` (T7): nord-dark .66→.69, solarized-dark .62→.63,
   dracula-dark .59→.61, tokyo-night-light .80→.83, gruvbox-light .79→.82, catppuccin-light
   .92→.93, rose-pine-light .90→.93. Each moves secondary on the panel by a few tenths and keeps
   primary > secondary > tertiary. *House rule:* tokens only, per block, mirrored in `DeskPalettes`.
   Catppuccin dark and Gruvbox dark need the ramp change in §4.2, item 13.
4. **Lift the inks that fall short on their own washes.** These are the twenty-two cells in T10.
   Nineteen need a step of ≤0.24 toward the block's primary ink, for example Standard light
   `--accent-ink` #0062CC→#015FC5 and Solarized dark positive #91A318→#99A92D. *Test:* in every
   block, each state ink over its wash on s1, `--accent-ink` over `--accent-wash` on s0 and s1,
   and over `--accent-wash-strong` on s1, all ≥4.5. The three big Tinted-hover misses are §4.2,
   item 14.
5. **Make the radii concentric.** Rows inside a 4px-padded panel take
   `calc(var(--radius-panel) - var(--space-1))` (8px): `.bar__menu-panel .bar__key` and
   `.popover__panel .menu__item`. Keys in the window strip take `calc(var(--radius) -
   var(--space-1))` (3px). The launcher's rows take the panel radius less both paddings. The gate
   already does this. *House rule:* radii are tokens, so add `--radius-inset` rather than literals.
   *Test:* a `DeskCssConventionTests` case that, for each named container/child pair, reads the
   two radii and the inset and asserts child = container − inset.
6. **Test the destructive Filled key.** Add `--state-negative` and `--state-negative-hover` to
   `A_filled_key_label_passes_over_every_accent_fill`'s fills. It fails today in eight blocks,
   so the test lands with the fix in §4.2, item 10.
7. **Put the chart series on inks where the fill fails.** Add the four inks to `DeskPalette`
   (and its mirror test) and have `DeskChartPalette.For(…, palette)` use the ink for any series
   whose fill is under 3:1 over `--surface-1`. Today 14 series cells, not counting the dim
   neutral, fail across six light blocks. With inks the worst series is 5.32:1. Keep `--chart-neutral-dim` for reference lines
   only, never a data series. Slot order is unchanged, so no series swaps colour (the existing
   test still holds). *Test:* every non-dim slot ≥3:1 over the block's panel, in every block.
8. **Do not add a scroll edge effect under the header.** Nothing scrolls beneath it, and the HIG
   (Scroll views, June 2026) says the effect is never decorative. The pinned table header is
   already the opaque, hard edge the HIG's automatic style gives pinned headers. One thing to
   check in a browser, which I could not: `.grid th { top: -14px }` against `.win__body`'s 16px
   padding may leave a 2px slit where rows show above the pinned header.

### 4.2 Judgment calls for the owner

9. **Increase Contrast for every family, derived rather than declared.** The HIG's Color page
   (Dec 2025) asks for an increased-contrast variant of every custom colour. Today only Standard
   has one, and the others defer to the High contrast family, which ThemeService substitutes
   only when no family was chosen. A derived block in the Tinted glass pattern (keep each value
   under a second name on `<html>`, mix on `<body>`, name no theme) would give secondary =
   `color-mix(secondary, primary 50%)` and separator = `color-mix(separator-strong, primary 15%)`.
   That measures secondary 6.2–10.0 on the panel and hairlines 1.65–2.47 (T10). It does not
   reach 3:1 field edges.
10. **Fix the destructive Filled key's fill.** For 4.5:1 at rest, the fill steps to Standard
    dark #FF453A→#D63A31, Standard light #FF3B30→#DB3329, Tokyo Night light #F52A65→#E1275D,
    Nord light #BF616A→#B45B64 (T10). On dark desks the hover then has to deepen too, which
    breaks the "lighter on dark" rule. The alternative is a `--state-negative-fill` and
    `-fill-hover` pair in all eighteen blocks, so dots and charts keep the bright red.
11. **Strengthen the hairline in the light families** to about 1.45:1, which is still under
    UIKit's 1.70. The alphas (T7): Standard light .10→.16, Nord .12→.21, Solarized .12→.20,
    Rosé Pine .14→.25, Catppuccin .12→.25, Gruvbox .12→.22, Tokyo Night .12→.22, Dracula
    .12→.19. Dark families are at 1.25–1.36 and could stay. The case against: macOS's own
    separator is as faint as the Desk's.
12. **Field edges.** Option A (polish): rest borders on `--separator-strong`, giving 1.36–1.74
    with hover moving elsewhere. Option B (3:1): a `--field-edge` token per block. That is more
    edge than Apple draws, so it could be kept to Increase Contrast only. MudBlazor's outlined
    border should follow whichever is chosen rather than spending `--surface-3`.
13. **Re-step the two steep dark ramps.** Catppuccin dark `--surface-3` #45475A→#3B3C4F and
    Gruvbox dark #504945→#46403E, each halfway back to s2, with secondary .66/.67→.70. Measured:
    S/s3 4.57 and 4.56, S/s1 6.24 and 6.05, s2↔s3 steps 1.17 and 1.14, close to Standard's 1.19.
    The cost is that each family drifts from its published palette.
14. **The Tinted key's hover on four dark desks** (3.64–3.96). Either lift those `--accent-ink`s
    a long way (nord-dark #88C0D0→#B2D0DB, dracula-dark #BD93F9→#CBAEF2) or let the Tinted key
    keep `--accent-wash` under the pointer and signal hover another way. The second changes one
    recipe for every family, which is the cleaner option.
15. **Lift the ground under windows in the flat dark families** (s0↔s1 1.05–1.07 against
    Standard's 1.09): Tokyo Night, Nord, Rosé Pine, Catppuccin. This trades palette fidelity
    for the HIG's "elevated is brighter".
16. **Primary at 7:1 on raised surfaces.** Rosé Pine light and Catppuccin light cannot get there
    without an ink deeper than the palette's own text, because primary is already at 100%.
    Tokyo Night light and Gruvbox light can, by raising primary from .92 to about .97.
17. **Tracking for Inter at the title rungs.** Inter's dynamic metrics suggest about −0.018em at
    22px and −0.011em at 17px. A token per rung would also tighten SF on a Mac, so decide
    whether that is acceptable or limit it to the bundled face. Keep semibold rather than the
    HIG's Bold emphasis (three weights). Decide whether `--weight-bold` stays as a fourth weight.
18. **Translucent fill tiers and a quaternary label** (HIG: label → quaternary, fill → tertiary
    fill). Replacing opaque `--surface-2/3` control fills with text-RGB fills would make a
    control on a title bar differ from the bar automatically, and would fix §3.1's s3 cases at
    the root. It touches every control, and the Desk has no watermark text for a quaternary
    tier. Not recommended now; worth knowing that this is the platform's answer.

## 5. What could not be measured here

- Rendering: no browser was driven. Everything is token arithmetic. Blur, `saturate(180%)`,
  sub-pixel hairlines at `--ui-scale` other than 1, and the 2px sticky-header question need eyes.
- AppKit's exact label, separator and control alphas are not on the HIG pages. The UIKit
  calibration values in T6 are the commonly published ones, not read from Apple today.
- APCA, which the HIG names alongside WCAG, was not computed.
- Whether the bundled `InterVariable.woff2` carries the `opsz` axis, and so whether optical
  sizing already applies on Windows.
- Charts as drawn: MudChart's gridlines and legend inks were not traced.

## Appendix A — measurement tables

Computed 2026-11-20 by a PowerShell script outside the repository, from the two stylesheets
as they stood. s0–s3 = `--surface-0…3`; P/S/T = primary, secondary, tertiary text. Bold marks a
cell under its threshold: 4.5:1 for text, 3:1 for a UI graphic.

### T1. Text tiers over every surface

WCAG 2.x ratio of each text tier composited over each surface. Bold = below 4.5:1 (primary, secondary) or the value itself for tertiary (expected below).

| Block | P/s0 | P/s1 | P/s2 | P/s3 | S/s0 | S/s1 | S/s2 | S/s3 | T/s0 | T/s1 | T/s2 | T/s3 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| apple-dark | 14.51 | 13.44 | 12.03 | 10.18 | 5.97 | 5.72 | 5.34 | 4.78 | 2.71 | 2.70 | 2.63 | 2.50 |
| apple-light | 15.09 | 16.56 | 13.64 | 11.54 | 5.55 | 5.74 | 5.35 | 5.00 | 2.09 | 2.11 | 2.07 | 2.03 |
| nord-dark | 9.96 | 9.44 | 7.69 | 6.64 | 5.95 | 5.70 | 4.85 | **4.31** | 2.41 | 2.37 | 2.21 | 2.08 |
| nord-light | 8.22 | 9.10 | 7.52 | 6.65 | 5.23 | 5.61 | 4.91 | 4.50 | 1.88 | 1.92 | 1.85 | 1.80 |
| solarized-dark | 11.25 | 10.15 | 8.88 | 7.58 | 5.96 | 5.54 | 5.03 | **4.47** | 2.39 | 2.34 | 2.26 | 2.15 |
| solarized-light | 8.64 | 9.65 | 8.03 | 7.13 | 5.24 | 5.63 | 4.99 | 4.61 | 1.85 | 1.88 | 1.83 | 1.79 |
| rose-pine-dark | 11.46 | 10.76 | 9.95 | 8.37 | 5.84 | 5.62 | 5.34 | 4.73 | 2.50 | 2.49 | 2.46 | 2.34 |
| rose-pine-light | 6.07 | 7.00 | 5.68 | 5.25 | 4.85 | 5.46 | 4.59 | **4.29** | 1.83 | 1.89 | 1.80 | 1.76 |
| catppuccin-dark | 10.42 | 9.79 | 7.63 | 5.63 | 5.94 | 5.70 | 4.75 | **3.75** | 2.24 | 2.25 | 2.13 | 1.92 |
| catppuccin-light | 6.57 | 7.06 | 6.04 | 5.17 | 5.44 | 5.79 | 5.05 | **4.42** | 1.87 | 1.90 | 1.83 | 1.76 |
| gruvbox-dark | 10.30 | 9.33 | 7.43 | 5.74 | 6.10 | 5.67 | 4.77 | **3.88** | 2.30 | 2.27 | 2.13 | 1.94 |
| gruvbox-light | 7.45 | 8.13 | 6.90 | 5.68 | 5.24 | 5.60 | 4.95 | **4.26** | 1.91 | 1.95 | 1.88 | 1.79 |
| tokyo-night-dark | 9.57 | 9.14 | 7.88 | 7.30 | 5.89 | 5.71 | 5.12 | 4.83 | 2.25 | 2.26 | 2.21 | 2.17 |
| tokyo-night-light | 6.91 | 7.89 | 6.19 | 5.55 | 5.07 | 5.62 | 4.65 | **4.26** | 1.80 | 1.85 | 1.76 | 1.72 |
| dracula-dark | 12.71 | 11.53 | 9.64 | 7.97 | 6.05 | 5.68 | 5.01 | **4.36** | 2.61 | 2.56 | 2.42 | 2.26 |
| dracula-light | 11.00 | 12.62 | 9.85 | 8.56 | 5.21 | 5.59 | 4.91 | 4.54 | 1.95 | 1.99 | 1.92 | 1.87 |
| contrast-dark | 21.00 | 19.80 | 16.48 | 12.63 | 13.08 | 12.58 | 10.92 | 8.71 | 3.66 | 3.77 | 3.77 | 3.41 |
| contrast-light | 16.83 | 21.00 | 14.17 | 11.18 | 10.88 | 12.63 | 9.63 | 8.08 | 3.23 | 3.35 | 3.13 | 2.97 |

### T2. Accent: label on fill, and the fill as a non-text UI colour

| Block | on-accent/accent | /hover | /press | accent vs s0 | vs s1 | vs s2 | on-accent/neg fill | /neg hover |
|---|---|---|---|---|---|---|---|---|
| apple-dark | 4.92 | 4.65 | 5.64 | 3.46 | 3.18 | **2.83** | **3.41** | **2.82** |
| apple-light | 4.60 | 5.10 | 5.71 | 4.13 | 4.60 | 3.67 | **3.55** | 5.38 |
| nord-dark | 6.24 | 7.50 | 5.16 | 6.61 | 6.24 | 5.03 | **3.05** | **3.97** |
| nord-light | 4.62 | 5.10 | 5.74 | 3.79 | 4.27 | 3.42 | **4.09** | 5.30 |
| solarized-dark | 4.95 | 4.62 | 5.62 | 3.39 | 3.03 | **2.63** | 4.63 | **3.76** |
| solarized-light | 4.62 | 5.10 | 5.71 | 3.77 | 4.28 | 3.46 | 4.63 | 6.44 |
| rose-pine-dark | 8.43 | 10.26 | 6.83 | 8.43 | 7.88 | 7.25 | 6.07 | 7.48 |
| rose-pine-light | 4.62 | 5.12 | 5.72 | 3.85 | 4.45 | 3.61 | **4.19** | 5.58 |
| catppuccin-dark | 8.07 | 9.04 | 6.97 | 8.64 | 8.07 | 6.19 | 7.08 | 8.05 |
| catppuccin-light | 5.41 | 5.88 | 6.40 | 4.45 | 4.79 | 4.09 | 5.43 | 6.63 |
| gruvbox-dark | 5.48 | 6.59 | 4.65 | 6.09 | 5.48 | 4.31 | **4.29** | 5.15 |
| gruvbox-light | 5.82 | 6.72 | 7.75 | 5.25 | 5.82 | 4.81 | 4.82 | 6.08 |
| tokyo-night-dark | 6.79 | 8.25 | 5.66 | 7.14 | 6.79 | 5.78 | 6.46 | 7.72 |
| tokyo-night-light | 4.64 | 5.22 | 5.81 | 3.59 | 4.19 | 3.16 | **3.89** | 4.97 |
| dracula-dark | 7.19 | 8.66 | 5.89 | 6.55 | 5.90 | 4.89 | 5.52 | 6.36 |
| dracula-light | 6.24 | 7.28 | 8.24 | 5.14 | 6.02 | 4.53 | 5.03 | 6.35 |
| contrast-dark | 9.62 | 12.00 | 7.69 | 9.62 | 9.07 | 7.55 | 6.94 | 8.65 |
| contrast-light | 7.04 | 9.52 | 11.80 | 5.64 | 7.04 | 4.75 | 6.04 | 8.37 |

### T3. Hairlines, field edges and the elevation ramp

sep/s1 = --separator composited on --surface-1 against --surface-1. field edge = a field's --separator border (drawn over its --surface-0 well) against the --surface-1 panel; WCAG 1.4.11 asks 3:1 of a boundary needed to identify a control.

| Block | sep/s1 | sep-strong/s1 | sep/s2 | field edge vs s1 | field well vs s1 | s0-s1 | s1-s2 | s2-s3 | s0-s2 |
|---|---|---|---|---|---|---|---|---|---|
| apple-dark | 1.36 | 1.67 | 1.37 | **1.24** | 1.09 | 1.09 | 1.12 | 1.19 | 1.22 |
| apple-light | 1.25 | 1.53 | 1.25 | **1.40** | 1.12 | 1.12 | 1.26 | 1.21 | 1.13 |
| nord-dark | 1.33 | 1.59 | 1.31 | **1.26** | 1.06 | 1.06 | 1.24 | 1.17 | 1.31 |
| nord-light | 1.24 | 1.44 | 1.22 | **1.38** | 1.12 | 1.12 | 1.25 | 1.15 | 1.11 |
| solarized-dark | 1.30 | 1.54 | 1.30 | **1.15** | 1.12 | 1.12 | 1.15 | 1.18 | 1.29 |
| solarized-light | 1.25 | 1.46 | 1.24 | **1.41** | 1.14 | 1.14 | 1.24 | 1.14 | 1.09 |
| rose-pine-dark | 1.29 | 1.54 | 1.30 | **1.19** | 1.07 | 1.07 | 1.09 | 1.20 | 1.16 |
| rose-pine-light | 1.23 | 1.44 | 1.21 | **1.41** | 1.15 | 1.15 | 1.23 | 1.08 | 1.07 |
| catppuccin-dark | 1.27 | 1.50 | 1.28 | **1.18** | 1.07 | 1.07 | 1.30 | 1.38 | 1.40 |
| catppuccin-light | 1.19 | 1.35 | 1.18 | **1.28** | 1.08 | 1.08 | 1.17 | 1.17 | 1.09 |
| gruvbox-dark | 1.30 | 1.53 | 1.29 | **1.15** | 1.11 | 1.11 | 1.27 | 1.31 | 1.41 |
| gruvbox-light | 1.23 | 1.42 | 1.22 | **1.35** | 1.11 | 1.11 | 1.21 | 1.25 | 1.09 |
| tokyo-night-dark | 1.25 | 1.45 | 1.26 | **1.17** | 1.05 | 1.05 | 1.17 | 1.08 | 1.23 |
| tokyo-night-light | 1.22 | 1.41 | 1.21 | **1.42** | 1.17 | 1.17 | 1.32 | 1.13 | 1.13 |
| dracula-dark | 1.35 | 1.64 | 1.35 | **1.21** | 1.11 | 1.11 | 1.21 | 1.22 | 1.34 |
| dracula-light | 1.27 | 1.51 | 1.25 | **1.48** | 1.17 | 1.17 | 1.33 | 1.17 | 1.13 |
| contrast-dark | 3.51 | 7.30 | 3.53 | 3.19 | 1.06 | 1.06 | 1.20 | 1.30 | 1.27 |
| contrast-light | 2.85 | 6.20 | 2.69 | 3.45 | 1.25 | 1.25 | 1.48 | 1.27 | 1.19 |

### T4. State and accent inks on their own washes, and state fills as UI colour

ink/wash = the -ink over its own -wash composited on --surface-1 (a tag). fill/s1 = the state fill against the panel (a dot, a chart series): 3:1 for a graphical object.

| Block | pos ink/wash | warn ink/wash | neg ink/wash | accent-ink/wash | accent-ink/wash on s0 | accent-ink/wash-strong | pos fill/s1 | warn fill/s1 | neg fill/s1 | chart-neutral/s1 | chart-dim/s1 |
|---|---|---|---|---|---|---|---|---|---|---|---|
| apple-dark | 5.81 | 6.58 | 4.70 | 4.85 | 5.25 | **4.41** | 7.75 | 7.62 | 4.60 | 4.81 | 2.62 |
| apple-light | 5.13 | 5.12 | 4.73 | 4.92 | **4.45** | **4.39** | **2.22** | **2.20** | 3.55 | 3.26 | 2.21 |
| nord-dark | **4.46** | 5.48 | 4.76 | 4.52 | 4.76 | **3.68** | 6.13 | 8.00 | 3.05 | 3.50 | 1.69 |
| nord-light | 5.01 | 5.04 | 4.91 | 4.99 | **4.48** | 4.53 | **2.93** | **2.63** | 3.78 | 3.30 | 2.04 |
| solarized-dark | **4.18** | **4.27** | 4.95 | **4.42** | 4.92 | **3.96** | 4.69 | 4.68 | 3.25 | 4.75 | 2.79 |
| solarized-light | 4.93 | 4.92 | 4.96 | 4.80 | **4.27** | **4.32** | **2.97** | **2.98** | 4.29 | **2.48** | 1.62 |
| rose-pine-dark | 6.90 | 7.15 | **4.46** | 5.72 | 6.19 | 4.55 | 9.68 | 10.06 | 5.67 | 5.12 | 3.20 |
| rose-pine-light | 5.05 | 5.11 | 4.84 | 4.98 | **4.37** | 4.50 | 3.30 | **2.16** | 4.04 | **2.87** | 1.56 |
| catppuccin-dark | 7.65 | 8.60 | 5.30 | 5.82 | 6.30 | 4.61 | 11.03 | 12.91 | 7.08 | 4.44 | 2.46 |
| catppuccin-light | 4.69 | 4.81 | 4.81 | 4.78 | **4.46** | **4.25** | **2.96** | **2.31** | 4.80 | **2.83** | 1.61 |
| gruvbox-dark | 5.18 | 6.04 | 4.78 | 4.51 | 5.02 | **3.76** | 7.14 | 8.69 | 4.29 | 4.02 | 2.26 |
| gruvbox-light | 4.89 | 5.00 | 6.13 | 4.88 | **4.43** | **4.32** | **2.73** | **2.19** | 4.82 | 3.24 | 1.92 |
| tokyo-night-dark | 6.83 | 6.36 | 5.02 | 5.15 | 5.47 | **4.20** | 9.35 | 8.55 | 6.46 | 4.10 | 1.91 |
| tokyo-night-light | 5.58 | 5.10 | 5.21 | 5.26 | 4.56 | 4.75 | 4.71 | 4.30 | 3.50 | 4.33 | 1.98 |
| dracula-dark | 7.31 | 5.97 | 4.60 | **4.42** | 4.89 | **3.64** | 10.38 | 8.36 | 4.53 | 3.03 | 1.56 |
| dracula-light | 5.14 | 5.20 | 5.16 | 5.91 | 5.09 | 5.23 | 5.96 | 5.58 | 4.85 | 3.56 | 1.87 |
| contrast-dark | 7.57 | 7.53 | 5.13 | 6.24 | 6.91 | 4.63 | 10.99 | 10.98 | 6.54 | 7.57 | 3.88 |
| contrast-light | 5.57 | 5.68 | 5.47 | 5.62 | 4.57 | 4.72 | 5.52 | 5.22 | 6.04 | 5.10 | 2.38 |

### T5. Text on the materials (chrome), Clear and Tinted glass

Each material composited over --surface-0 (what the header and drawers float over at rest). Tinted = color-mix(in srgb, material, --surface-2 50%), premultiplied, then over --surface-0. Secondary text shown; primary is above 9:1 in every case (see min column).

| Block | S/ultrathin | S/thin | S/regular | S/thick | S/thin tinted | S/regular tinted | min P (all) | thin alpha clear/tinted | sep on thin clear/tinted | S on thin over primary-ink content | S on thin over accent |
|---|---|---|---|---|---|---|---|---|---|---|---|
| apple-dark | 5.65 | 5.66 | 5.74 | 5.91 | 5.50 | 5.54 | 12.60 | 0.70/0.85 | 1.36/1.37 | **3.05** | 4.57 |
| apple-light | 5.67 | 5.64 | 5.60 | 5.55 | 5.50 | 5.48 | 14.36 | 0.74/0.87 | 1.25/1.25 | 4.67 | 5.03 |
| nord-dark | 5.33 | 5.51 | 5.75 | 5.95 | 5.17 | 5.29 | 8.16 | 0.70/0.85 | 1.33/1.32 | **2.97** | **3.41** |
| nord-light | 5.47 | 5.43 | 5.38 | 5.23 | 5.17 | 5.15 | 7.87 | 0.76/0.88 | 1.23/1.23 | **4.29** | 4.62 |
| solarized-dark | 5.42 | 5.49 | 5.61 | 5.96 | 5.26 | 5.32 | 9.35 | 0.74/0.87 | 1.30/1.30 | **3.41** | **4.41** |
| solarized-light | 5.48 | 5.47 | 5.40 | 5.24 | 5.23 | 5.20 | 8.33 | 0.76/0.88 | 1.24/1.24 | **4.31** | 4.61 |
| rose-pine-dark | 5.57 | 5.63 | 5.66 | 5.84 | 5.49 | 5.51 | 10.29 | 0.72/0.86 | 1.29/1.30 | **3.36** | **3.68** |
| rose-pine-light | 5.22 | 5.14 | 5.02 | 4.85 | 4.86 | 4.80 | 5.87 | 0.76/0.88 | 1.22/1.22 | **3.87** | **4.12** |
| catppuccin-dark | 5.30 | 5.56 | 5.74 | 5.94 | 5.17 | 5.27 | 8.22 | 0.72/0.86 | 1.28/1.29 | **3.28** | **3.48** |
| catppuccin-light | 5.65 | 5.61 | 5.55 | 5.44 | 5.33 | 5.30 | 6.30 | 0.76/0.88 | 1.19/1.19 | **4.12** | **4.20** |
| gruvbox-dark | 5.39 | 5.50 | 5.75 | 6.10 | 5.13 | 5.27 | 8.06 | 0.70/0.85 | 1.30/1.30 | **3.03** | **3.58** |
| gruvbox-light | 5.47 | 5.38 | 5.24 | 4.97 | 5.17 | 5.10 | 6.92 | 0.76/0.88 | 1.22/1.22 | **4.20** | **4.29** |
| tokyo-night-dark | 5.48 | 5.58 | 5.74 | 5.89 | 5.36 | 5.45 | 8.25 | 0.72/0.86 | 1.25/1.26 | **3.39** | **3.70** |
| tokyo-night-light | 5.41 | 5.34 | 5.21 | 5.07 | 4.99 | 4.93 | 6.55 | 0.76/0.88 | 1.22/1.21 | **4.18** | **4.43** |
| dracula-dark | 5.50 | 5.58 | 5.75 | 6.05 | 5.29 | 5.38 | 10.31 | 0.70/0.85 | 1.35/1.35 | **2.98** | **3.67** |
| dracula-light | 5.45 | 5.42 | 5.33 | 5.21 | 5.17 | 5.12 | 10.41 | 0.76/0.88 | 1.27/1.26 | **4.40** | 4.64 |
| contrast-dark | 12.07 | 12.41 | 12.69 | 13.08 | 11.77 | 11.99 | 17.63 | 0.95/0.98 | 3.54/3.58 | 11.42 | 11.80 |
| contrast-light | 12.53 | 12.36 | 12.12 | 11.71 | 10.99 | 10.87 | 16.35 | 0.96/0.98 | 2.84/2.77 | 11.72 | 11.92 |

### T6. Calibration: Apple's own UIKit values, measured the same way

| Value | Ratio |
|---|---|
| iOS light secondaryLabel rgba(60,60,67,.60) on white | 3.44 |
| iOS light tertiaryLabel rgba(60,60,67,.30) on white | 1.73 |
| iOS light quaternaryLabel rgba(60,60,67,.18) on white | 1.37 |
| iOS light separator rgba(60,60,67,.29) on white | 1.70 |
| iOS light opaqueSeparator #C6C6C8 on white | 1.71 |
| iOS dark secondaryLabel rgba(235,235,245,.60) on #1C1C1E | 5.95 |
| iOS dark tertiaryLabel rgba(235,235,245,.30) on #1C1C1E | 2.48 |
| iOS dark quaternaryLabel rgba(235,235,245,.16) on #1C1C1E | 1.58 |
| iOS dark separator rgba(84,84,88,.60) on #1C1C1E | 1.59 |
| iOS dark opaqueSeparator #38383A on #1C1C1E | 1.45 |

### T7. Candidate values: the alpha each family's secondary ink needs

Lowest alpha of the block's own text RGB that clears the target over the named surface.

| Block | secondary now | 4.5 on s2 | 4.5 on s3 | 7.0 on s1 (secondary AAA) | primary now | primary for 7.0 on s3 | separator for 1.45 on s1 | separator for 1.60 on s1 | separator now |
|---|---|---|---|---|---|---|---|---|---|
| apple-dark | 0.55 | 0.49 | 0.53 | 0.63 | 0.92 | 0.73 | 0.12 | 0.15 | 0.10 |
| apple-light | 0.60 | 0.56 | 0.57 | 0.66 | 0.88 | 0.71 | 0.16 | 0.2 | 0.10 |
| nord-dark | 0.66 | 0.63 | 0.69 | 0.77 | 0.92 | 0.96 | 0.13 | 0.17 | 0.10 |
| nord-light | 0.76 | 0.73 | 0.76 | 0.84 | 0.92 | 0.95 | 0.21 | 0.26 | 0.12 |
| solarized-dark | 0.62 | 0.58 | 0.63 | 0.73 | 0.90 | 0.86 | 0.14 | 0.18 | 0.10 |
| solarized-light | 0.74 | 0.71 | 0.74 | 0.82 | 0.92 | 0.92 | 0.2 | 0.25 | 0.12 |
| rose-pine-dark | 0.62 | 0.55 | 0.6 | 0.72 | 0.92 | 0.82 | 0.15 | 0.18 | 0.10 |
| rose-pine-light | 0.90 | 0.9 | 0.93 | 1 | 1.00 | unreachable | 0.25 | 0.31 | 0.14 |
| catppuccin-dark | 0.66 | 0.64 | 0.78 | 0.76 | 0.92 | unreachable | 0.15 | 0.19 | 0.10 |
| catppuccin-light | 0.92 | 0.87 | 0.93 | 1 | 1.00 | unreachable | 0.25 | 0.31 | 0.12 |
| gruvbox-dark | 0.67 | 0.65 | 0.77 | 0.78 | 0.92 | unreachable | 0.15 | 0.18 | 0.10 |
| gruvbox-light | 0.79 | 0.76 | 0.82 | 0.87 | 0.92 | unreachable | 0.22 | 0.27 | 0.12 |
| tokyo-night-dark | 0.69 | 0.63 | 0.66 | 0.79 | 0.92 | 0.9 | 0.16 | 0.2 | 0.10 |
| tokyo-night-light | 0.80 | 0.79 | 0.83 | 0.88 | 0.92 | unreachable | 0.22 | 0.27 | 0.12 |
| dracula-dark | 0.59 | 0.55 | 0.61 | 0.68 | 0.92 | 0.85 | 0.13 | 0.16 | 0.10 |
| dracula-light | 0.68 | 0.66 | 0.68 | 0.75 | 0.92 | 0.85 | 0.19 | 0.23 | 0.12 |
| contrast-dark | 0.80 | 0.46 | 0.51 | 0.59 | 1.00 | 0.7 | 0.15 | 0.18 | 0.38 |
| contrast-light | 0.80 | 0.57 | 0.6 | 0.66 | 1.00 | 0.75 | 0.16 | 0.2 | 0.40 |

### T8. Controls drawn from the ramp

toggle off: the --surface-3 track against the --surface-1 panel, and the --on-accent thumb against that track. toggle on: the --accent track against the panel. badge: --text-secondary at caption size on --surface-3. menu desc: --text-secondary on a highlighted row (--accent-wash over --material-regular over --surface-0). current key: --accent-ink on --accent-wash in the --surface-0 channel. gate: --text-secondary on the --surface-0 track; selected label --text-primary on the --surface-3 cap. sticky header: --text-secondary on --surface-2.

| Block | off track/s1 | thumb/off track | on track/s1 | badge S/s3 | menu desc S/highlight | gate S/s0 | gate cap P/s3 | cap vs track (s3/s0) |
|---|---|---|---|---|---|---|---|---|
| apple-dark | **1.34** | 11.70 | 3.18 | 4.78 | 5.29 | 5.97 | 10.18 | 1.45 |
| apple-light | **1.52** | **1.52** | 4.60 | 5.00 | 5.33 | 5.55 | 11.54 | 1.36 |
| nord-dark | **1.45** | **1.45** | 6.24 | **4.31** | 4.50 | 5.95 | 6.64 | 1.53 |
| nord-light | **1.43** | **1.55** | 4.27 | 4.50 | 4.96 | 5.23 | 6.65 | 1.28 |
| solarized-dark | **1.37** | 10.98 | 3.03 | **4.47** | 4.93 | 5.96 | 7.58 | 1.53 |
| solarized-light | **1.41** | **1.53** | 4.28 | 4.61 | 4.88 | 5.24 | 7.13 | 1.25 |
| rose-pine-dark | **1.31** | **1.40** | 7.88 | 4.73 | 4.60 | 5.84 | 8.37 | 1.40 |
| rose-pine-light | **1.33** | **1.38** | 4.45 | **4.29** | **4.40** | 4.85 | 5.25 | 1.15 |
| catppuccin-dark | **1.80** | **1.80** | 8.07 | **3.75** | 4.58 | 5.94 | 5.63 | 1.92 |
| catppuccin-light | **1.37** | **1.54** | 4.79 | **4.42** | 4.80 | 5.44 | 5.17 | 1.27 |
| gruvbox-dark | **1.67** | **1.67** | 5.48 | **3.88** | 4.73 | 6.10 | 5.74 | 1.86 |
| gruvbox-light | **1.51** | **1.51** | 5.82 | **4.26** | 4.69 | 5.24 | 5.68 | 1.37 |
| tokyo-night-dark | **1.27** | **1.27** | 6.79 | 4.83 | 4.73 | 5.89 | 7.30 | 1.34 |
| tokyo-night-light | **1.50** | **1.66** | 4.19 | **4.26** | 4.73 | 5.07 | 5.55 | 1.29 |
| dracula-dark | **1.47** | **1.80** | 5.90 | **4.36** | 4.72 | 6.05 | 7.97 | 1.64 |
| dracula-light | **1.56** | **1.61** | 6.02 | 4.54 | 4.93 | 5.21 | 8.56 | 1.33 |
| contrast-dark | **1.57** | **1.66** | 9.07 | 8.71 | 9.46 | 13.08 | 12.63 | 1.66 |
| contrast-light | **1.88** | **1.88** | 7.04 | 8.08 | 10.40 | 10.88 | 11.18 | 1.51 |

### T9. Standard under Increase Contrast (desk.css @media prefers-contrast: more)

- apple-dark: separator 2.20:1 on s1 (from 1.36); secondary 8.35 on s1, 7.64 on s2, 6.66 on s3; field edge 2.02.
- apple-light: separator 1.78:1 on s1 (from 1.25); secondary 10.41 on s1, 9.15 on s2, 8.14 on s3; field edge 1.98.


### T10. What the failing cells would take

#### Inks on washes: the ink stepped toward the primary text colour until it clears 4.5:1

| Block | case | now | step | candidate | after |
|---|---|---|---|---|---|
| apple-dark | accent ink / wash-strong (tinted key hover) | 4.41 | 0.04 | #409CFF -> #479FFE | 4.57 |
| apple-light | accent ink / wash on s0 (current key) | 4.45 | 0.02 | #0062CC -> #0161C9 | 4.55 |
| apple-light | accent ink / wash-strong (tinted key hover) | 4.39 | 0.04 | #0062CC -> #015FC5 | 4.60 |
| nord-dark | positive ink / own wash | 4.46 | 0.02 | #A3BE8C -> #A4BF8E | 4.50 |
| nord-dark | accent ink / wash-strong (tinted key hover) | 3.68 | 0.5 | #88C0D0 -> #B2D0DB | 4.54 |
| nord-light | accent ink / wash on s0 (current key) | 4.48 | 0.02 | #45628B -> #45618A | 4.52 |
| solarized-dark | positive ink / own wash | 4.18 | 0.12 | #91A318 -> #99A92D | 4.53 |
| solarized-dark | warning ink / own wash | 4.27 | 0.08 | #BC951A -> #BE9A28 | 4.50 |
| solarized-dark | accent ink / wash on s1 | 4.42 | 0.04 | #4AA1DE -> #50A3DD | 4.53 |
| solarized-dark | accent ink / wash-strong (tinted key hover) | 3.96 | 0.22 | #4AA1DE -> #69ACD9 | 4.54 |
| solarized-light | accent ink / wash on s0 (current key) | 4.27 | 0.12 | #1A6699 -> #1A6290 | 4.54 |
| solarized-light | accent ink / wash-strong (tinted key hover) | 4.32 | 0.08 | #1A6699 -> #1A6393 | 4.50 |
| rose-pine-dark | negative ink / own wash | 4.46 | 0.02 | #EB6F92 -> #EA7194 | 4.51 |
| rose-pine-light | accent ink / wash on s0 (current key) | 4.37 | 0.18 | #6F5889 -> #6B5786 | 4.51 |
| catppuccin-light | accent ink / wash on s0 (current key) | 4.46 | 0.02 | #7A33D7 -> #7934D5 | 4.50 |
| catppuccin-light | accent ink / wash-strong (tinted key hover) | 4.25 | 0.16 | #7A33D7 -> #7337C5 | 4.52 |
| gruvbox-dark | accent ink / wash-strong (tinted key hover) | 3.76 | 0.4 | #8BAB9F -> #ABB8A2 | 4.53 |
| gruvbox-light | accent ink / wash on s0 (current key) | 4.43 | 0.04 | #076678 -> #0A6576 | 4.51 |
| gruvbox-light | accent ink / wash-strong (tinted key hover) | 4.32 | 0.1 | #076678 -> #0E6373 | 4.51 |
| tokyo-night-dark | accent ink / wash-strong (tinted key hover) | 4.20 | 0.24 | #7AA2F7 -> #88A8F3 | 4.50 |
| dracula-dark | accent ink / wash on s1 | 4.42 | 0.04 | #BD93F9 -> #BF96F8 | 4.54 |
| dracula-dark | accent ink / wash-strong (tinted key hover) | 3.64 | 0.32 | #BD93F9 -> #CBAEF2 | 4.54 |

#### Destructive filled key: --on-accent on --state-negative and --state-negative-hover

| Block | on-accent | rest | hover | fill stepped toward black (dark ink) or white for 4.5:1 at rest |
|---|---|---|---|---|
| apple-dark | #FFFFFF | 3.41 | 2.82 | #FF453A -> #D63A31 (4.65) |
| apple-light | #FFFFFF | 3.55 | 5.38 | #FF3B30 -> #DB3329 (4.64) |
| nord-dark | #2E3440 | 3.05 | 3.97 | #BF616A -> #D08A91 (4.59) |
| nord-light | #FFFFFF | 4.09 | 5.30 | #BF616A -> #B45B64 (4.55) |
| solarized-dark | #FFFFFF | 4.63 | 3.76 | #DC322F -> #DC322F (4.63) |
| rose-pine-light | #FFFFFF | 4.19 | 5.58 | #B4637A -> #A95D73 (4.66) |
| gruvbox-dark | #282828 | 4.29 | 5.15 | #FB4934 -> #FB5440 (4.53) |
| tokyo-night-light | #FFFFFF | 3.89 | 4.97 | #F52A65 -> #E1275D (4.51) |

#### Field edge with --separator-strong at rest

apple-dark 1.52; apple-light 1.70; nord-dark 1.51; nord-light 1.60; solarized-dark 1.38; solarized-light 1.64; rose-pine-dark 1.42; rose-pine-light 1.63; catppuccin-dark 1.39; catppuccin-light 1.44; gruvbox-dark 1.37; gruvbox-light 1.56; tokyo-night-dark 1.36; tokyo-night-light 1.62; dracula-dark 1.48; dracula-light 1.74; contrast-dark 6.95; contrast-light 7.18

#### Secondary on --surface-3 if surface-3 moved halfway back toward surface-2 (the two steep dark ramps)

- catppuccin-dark: --surface-3 #45475A -> #3B3C4F: secondary 4.23 (from 3.75), s2-s3 step 1.17 (from 1.38).
- gruvbox-dark: --surface-3 #504945 -> #46403E: secondary 4.31 (from 3.88), s2-s3 step 1.14 (from 1.31).

#### Derived Increase Contrast for every family (no [data-theme] rule): secondary mixed halfway to primary, separator = color-mix(separator-strong, text-primary 15%)

| Block | secondary on s1 now | derived | on s3 derived | separator on s1 now | derived | field edge derived |
|---|---|---|---|---|---|---|
| apple-dark | 5.72 | 9.06 | 7.16 | 1.36 | 2.47 | 2.28 |
| apple-light | 5.74 | 10.00 | 7.89 | 1.25 | 2.02 | 2.24 |
| nord-dark | 5.70 | 7.42 | 5.40 | 1.33 | 2.21 | 2.11 |
| nord-light | 5.61 | 7.13 | 5.47 | 1.24 | 1.79 | 1.98 |
| solarized-dark | 5.54 | 7.62 | 5.89 | 1.30 | 2.15 | 1.95 |
| solarized-light | 5.63 | 7.37 | 5.75 | 1.25 | 1.83 | 2.05 |
| rose-pine-dark | 5.62 | 7.92 | 6.38 | 1.29 | 2.17 | 2.02 |
| rose-pine-light | 5.46 | 6.18 | 4.75 | 1.23 | 1.75 | 1.96 |
| catppuccin-dark | 5.70 | 7.57 | 4.63 | 1.27 | 2.08 | 1.94 |
| catppuccin-light | 5.79 | 6.39 | 4.78 | 1.19 | 1.65 | 1.75 |
| gruvbox-dark | 5.67 | 7.35 | 4.75 | 1.30 | 2.11 | 1.92 |
| gruvbox-light | 5.60 | 6.74 | 4.92 | 1.23 | 1.75 | 1.91 |
| tokyo-night-dark | 5.71 | 7.29 | 5.98 | 1.25 | 1.99 | 1.87 |
| tokyo-night-light | 5.62 | 6.65 | 4.86 | 1.22 | 1.74 | 1.98 |
| dracula-dark | 5.68 | 8.28 | 5.99 | 1.35 | 2.36 | 2.16 |
| dracula-light | 5.59 | 8.42 | 6.28 | 1.27 | 1.93 | 2.22 |
| contrast-dark | 12.58 | 15.94 | 10.56 | 3.51 | 8.69 | 8.36 |
| contrast-light | 12.63 | 17.49 | 9.90 | 2.85 | 7.77 | 8.82 |

#### Chart series against the panel (graphical objects, 3:1): fills as DeskChartPalette passes them, and the inks instead

| Block | accent | positive | warning | negative | neutral | dim | worst with inks for states and accent |
|---|---|---|---|---|---|---|---|
| apple-dark | 3.18 | 7.75 | 7.62 | 4.60 | 4.81 | **2.62** | 5.54 |
| apple-light | 4.60 | **2.22** | **2.20** | 3.55 | 3.26 | **2.21** | 5.78 |
| nord-dark | 6.24 | 6.13 | 8.00 | 3.05 | 3.50 | **1.69** | 5.71 |
| nord-light | 4.27 | **2.93** | **2.63** | 3.78 | 3.30 | **2.04** | 5.74 |
| solarized-dark | 3.03 | 4.69 | 4.68 | 3.25 | 4.75 | **2.79** | 5.32 |
| solarized-light | 4.28 | **2.97** | **2.98** | 4.29 | **2.48** | **1.62** | 5.69 |
| rose-pine-dark | 7.88 | 9.68 | 10.06 | 5.67 | 5.12 | 3.20 | 5.67 |
| rose-pine-light | 4.45 | 3.30 | **2.16** | 4.04 | **2.87** | **1.56** | 5.70 |
| catppuccin-dark | 8.07 | 11.03 | 12.91 | 7.08 | 4.44 | **2.46** | 7.08 |
| catppuccin-light | 4.79 | **2.96** | **2.31** | 4.80 | **2.83** | **1.61** | 5.39 |
| gruvbox-dark | 5.48 | 7.14 | 8.69 | 4.29 | 4.02 | **2.26** | 5.86 |
| gruvbox-light | 5.82 | **2.73** | **2.19** | 4.82 | 3.24 | **1.92** | 5.58 |
| tokyo-night-dark | 6.79 | 9.35 | 8.55 | 6.46 | 4.10 | **1.91** | 6.46 |
| tokyo-night-light | 4.19 | 4.71 | 4.30 | 3.50 | 4.33 | **1.98** | 6.10 |
| dracula-dark | 5.90 | 10.38 | 8.36 | 4.53 | 3.03 | **1.56** | 5.60 |
| dracula-light | 6.02 | 5.96 | 5.58 | 4.85 | 3.56 | **1.87** | 6.12 |
| contrast-dark | 9.07 | 10.99 | 10.98 | 6.54 | 7.57 | 3.88 | 6.54 |
| contrast-light | 7.04 | 5.52 | 5.22 | 6.04 | 5.10 | **2.38** | 6.82 |

#### Drawers and popovers (--material-regular) over the worst content a 30px blur could leave: a solid accent fill

secondary clear/tinted: apple-dark 5.13/5.25; apple-light 5.28/5.32; nord-dark 4.33/4.58; nord-light 4.91/4.91; solarized-dark 4.95/4.99; solarized-light 4.90/4.94; rose-pine-dark 4.56/4.96; rose-pine-light 4.42/4.50; catppuccin-dark 4.54/4.65; catppuccin-light 4.70/4.87; gruvbox-dark 4.53/4.65; gruvbox-light 4.60/4.77; tokyo-night-dark 4.70/4.91; tokyo-night-light 4.68/4.66; dracula-dark 4.54/4.77; dracula-light 4.88/4.89; contrast-dark 12.43/11.79; contrast-light 11.90/10.76
