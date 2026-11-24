# ADR 0032 — A window's sections are cards in a flow

**Status:** Accepted, 2026-11-22. Built in the Desk (`FormGroup`, `DeskCardSpan`, `.wflow`,
`.wgroup__split`, `DeskThemePicker`'s `Group`) and the Window manager; the hand-spelled sections
in `XLines.Web` are still to move to it.

**Amends:** [ADR 0016](0016-apple-feel-design-system.md) and the October 2026 house rule it grew
(a group is a fill with one hairline edge), which now says where a group's name goes;
[ADR 0020](0020-one-anatomy-for-every-window.md), whose grouped-list sections (`.wsection`) keep
their spacing and take a header bar; [ADR 0030](0030-the-desk-s-make-and-an-accent-for-every-family.md),
whose theme picker drew its families under plain headings; and the Window manager of
[ADR 0007](0007-window-manager-ui.md), which was one column at a form's width.

## Context

The owner's mock-up of the Window manager (`docs/evidence/styling_improvements_2.pdf`, pages 1 to 3, with
a note on pages 4 and 5 not to stretch the boxes) named three faults, and each was a rule of the
system working as written:

1. **The sections ran together.** A group's name was a quiet grey sentence-case line standing
   *above* the group (`.wgroup__head`), the HIG's grouped-list header. On a window of half a
   dozen sections that read as header, gap, box, header, gap, box: each name was nearer the box before
   it than the one it named, and nothing said where one section ended.
2. **The width was wasted.** The manager stopped at 680px, a form's reading width
   (`apple-hig-components`, `forms.md` rule 8), so at 1600px the right half of the window stood
   empty and the sections stacked a long way down. The rule was right about a line of controls and
   wrong about the window: it capped the window when it should have capped each group.
3. **Controls floated ungrouped.** Mode, accent and twenty-three theme tiles sat in one long
   Appearance group, the tiles in four shelves under floating headings — two, five, nine and seven
   tiles, ragged at every width — and the short gates took a full row each.

## Decision

**A section is one card, and its name is inside it.** `FormGroup` draws a `section.wsection`
holding the existing `.wgroup` (the `--surface-2` fill, one 1px `--separator-strong` edge,
`--radius-panel`) whose first child is a **header bar**, `.wgroup__bar`: the title (`h3.wgroup__title`,
headline style — `--text-headline`, semibold, the primary ink — sentence case, and the section's
accessible name) and, trailing, at most one quiet thing (`Keys`: a small Plain or Tinted key, a
count, or a two-to-five segment filter over the card's rows; never the window's Filled key). One
full-width hairline divides the bar from the rows. The bar's fill is
`color-mix(in srgb, var(--surface-2) 50%, var(--surface-1))`, half a step back down the ramp from
the two tokens it sits between, so it is right in every family and both appearances without a
token of its own. Every `FormGroup` with a `Title` draws it in the bar; with neither `Title` nor
`Keys` there is no bar.

**This is still one hairline per group.** The card's edge is the one edge; the bar's hairline is a
divider *inside* it, the full-width separator the HIG draws under a grouped list's own header row,
exactly as the rows are divided by inset hairlines. Nothing inside the card draws a box — no
border or shadow on the bar, a cell, a key or a row — so the non-negotiable "never a box inside a
box" stands. The full width is deliberate: the bar is the card's threshold, not one of its rows,
so its divider is not inset.

**Footers stay under the card,** always, as the HIG's grouped-list footer; a warning `Note` about
the card's state goes in `Notice`, between the card and its footer, never inside the card (a Note
is a box of its own).

**More width is more columns, never wider cards.** A window with two or more sections puts them in
`.wflow`: `repeat(auto-fill, minmax(min(100%, 420px), 1fr))`, `--space-5` between rows and
`--space-4` between columns, `align-items: start`. Why 420px: a card's form rows stack their
labels above their controls under 400px of card, so the first cut, a 360px track (a label column
and a four-segment gate, no more), stacked every row of a settings card and stood it twice as tall.
At 420px the labels stay beside their controls: a 1600px window is three tracks of about 520px, and
a window of about 680px is one column. Each card caps itself, so a wide window shows three cards
across rather than one box the width of the desk, and a narrow one stacks them; under 420px the
track is the window. `auto-fill` keeps empty tracks, so two cards in a wide window stay two
card-widths. `align-items: start` is the "do not stretch the boxes" note: a short card keeps its
own height beside a tall one rather than growing an empty bottom. The window is never capped with
a max-width column; the 420px floor and `1fr` do it. (`forms.md` rule 8, the reading width, is now a rule about a group, not a window.)

**Span only a card whose body is a grid of its own.** `FormGroup.Span` is a `DeskCardSpan`:
`Track` (the default, every card of form rows), `Row` (the whole row: a library of tiles, a chart,
a wide table), or `Rest` (the rest of the first row once two tracks fit, from 856px of flow, two tracks and the gap; the
whole row below that). `Rest` is for the one large card beside the card that leads the window, and
must be the flow's second card.

**A split row puts two compact, related controls side by side.** `.wgroup__row.wgroup__split`
holds two `.wgroup__cell`s, each its own `span.wgroup__label` leading its control, divided by a
vertical hairline inset top and bottom, as row hairlines are inset at the leading edge. The first
cell's label takes the group's `--form-label` column so its control lines up with the rows below.
Under 720px of card — two cells that each hold a label column and a three-segment gate — the cells
stack and the divider becomes the ordinary inset row hairline. A cell never wraps: its label stays
beside its control, side by side or stacked, so the two cells always lay out alike, and only under
400px do both put their labels above their controls at once. It is
for a two-or-three-segment gate, a row of swatches, a short field — never for two full form rows,
which are two `FormRow`s.

**A long collection is filtered, not shelved.** `DeskThemePicker` draws one grid; its `Group`
narrows it to one of the catalogue's headings (`""` or null for all), and the caller draws the
choice — on the Window manager a small `DeskSwitch` of `DeskThemePicker.ThemeFilters` (All, then
each heading) in the Themes card's bar. It opens on All: opened on the showing family's heading, a
card spanning the rest of a wide row held two tiles and was a large empty box. The footer names the
family showing and its heading (`DeskThemePicker.GroupOf`), since a narrower filter can hide the
tick. Every row is full but the last, and there are no shelves. The picker stays one radiogroup with one Tab stop; when the filter hides the chosen tile,
the first tile showing holds the stop.

**What a card holds that is not a row is the card's to style.** Seven windows had each written the
same rules in their own scoped sheets; they are one block in `desk.css` now. A `DeskGrid` straight
in a card treats the card as its frame: it insets so its text starts where the bar's title does,
and its last row's hairline stands down, because the card's edge is under it and two lines a pixel
apart read as a box inside the box. A lone `EmptyState` sentence and an `InlineConfirm` row take the
rows' inset. All three tighten under 480px of card, as the rows do. A window puts them in the card
and writes none of it again.

**Narrow widths are the card's.** The group is a container: form rows stack their labels under
400px of card, split cells under 720px, and the bar, rows and cells give back their inset under
480px — whatever the window's width.

The Window manager is the worked example: a `.wflow` of Appearance (Mode beside Accent in a split
row, then Glass and the makes as form rows), Themes (`Rest`, the filter in its bar), Scale,
Windows, Tab groups, Primary window, Resolution and Workspaces.

## Consequences

- `.wgroup__head`, the old header above the group, stays in `desk.css` for the sections
  `XLines.Web` still spells by hand; it is the old spelling and nothing new uses it. The hand
  spelling of the new card, where `FormGroup` cannot be used, is `section.wsection >
  div.wgroup > div.wgroup__bar > h3.wgroup__title (+ div.wgroup__keys)`, then the rows, then
  `p.wgroup__foot` after the `.wgroup`.
- The flow is `.wflow`, not `.wcards`: the application owns `.wcard`, and `DeskConventionTests`
  matches class names by substring.
- A popover that frames its own `FormGroup` (no fill, no edge of its own) now gets a bar too, and
  re-targets its padding at `.wgroup__bar`.
- Tests that read `.wgroup + .wgroup__foot` still hold: the footer is the card's next sibling
  unless a `Notice` is given.
- The design rules live in the skills: what to draw in `apple-hig-components` (`forms.md`, "The
  grouped form" and "Sections in a window"; `type-color-layout.md`, "Layout"; the audit), how the
  Desk draws it in `apple-mudblazor` (`references/components.md`, "Section cards and the flow"),
  and the parameters in the Desk's README.
