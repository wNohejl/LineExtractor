# Type, colour and layout: the numbers

## macOS text styles (the desk's density)

| Style | Weight | Size | Line height | Emphasized |
|---|---|---|---|---|
| Large Title | Regular | 26 | 32 | Bold |
| Title 1 | Regular | 22 | 26 | Bold |
| Title 2 | Regular | 17 | 22 | Bold |
| Title 3 | Regular | 15 | 20 | Semibold |
| Headline | Bold | 13 | 16 | Heavy |
| Body | Regular | 13 | 16 | Semibold |
| Callout | Regular | 12 | 15 | Semibold |
| Subheadline | Regular | 11 | 14 | Semibold |
| Footnote | Regular | 10 | 13 | Semibold |
| Caption 1 | Regular | 10 | 13 | Medium |
| Caption 2 | Medium | 10 | 13 | Semibold |

Default body 13pt, minimum 10pt on macOS (17 / 11 on iOS). **Avoid light weights**; prefer
Regular, Medium, Semibold, Bold. Headline is the same size as Body and differs by weight.
Minimise typefaces; hierarchy comes from the styles, not from new sizes. Tabular
(monospaced) digits wherever numbers change or align.

iOS default (Large): Large Title 34, Title 1 28, Title 2 22, Title 3 20, Headline 17
semibold, Body 17, Callout 16, Subhead 15, Footnote 13, Caption 1 12, Caption 2 11.

## Label and background levels

Four label colours: **label** (primary content), **secondary** (subheading, supplemental),
**tertiary** (unavailable), **quaternary** (watermark). Placeholder text has its own
colour. Separator is translucent; opaque separator blocks what is beneath.

Two background sets on iOS, each with primary / secondary / tertiary:
- **grouped** (`systemGroupedBackground` …) for grouped tables and forms: primary is the
  view, secondary the groups on it, tertiary a grouping inside a group;
- **system** for everything else.
macOS: window background, control background (tables, browsers), text background,
under-page background, alternating content backgrounds, grid, separator, and the
selected/unemphasized selection pairs. Don't redefine a semantic colour's meaning
(separator is never a text colour).

Dark Mode: base vs elevated backgrounds (elevated = foreground sheets, popovers,
multitasking); dim backgrounds, bright foregrounds; **4.5:1 minimum, aim for 7:1** on small
text; soften white image backgrounds. macOS desktop tinting: transparency only in neutral
states of custom components.

## Colour

- Use colours consistently: the same colour never means two things, and the interactive
  colour is never used on non-interactive text.
- Every colour works in light, dark and increased contrast; prefer system colours.
- **Never colour alone**: pair with a label or a glyph shape.
- Accent: on the **background** of the one primary action, sparingly elsewhere; don't colour
  the backgrounds of several controls; monochrome toolbars and tab bars by default.
- State colours (red, orange, green) on values, tags and status, not on controls.
  Red = destructive.

## Layout

- **Order by importance**: top and leading first.
- **Align, and indent to show subordination.** People assume aligned items are related.
- **Group with negative space, container shapes or separator lines** — in that order of
  strength.
- **Progressive disclosure**: disclosure triangles, menus, nested views, folded steps.
- **Differentiate controls from content**: controls on their own layer (materials / scroll
  edge effect), never a solid tint under a control row.
- **Hit regions 44×44pt** (60 on visionOS); buttons at least a space apart.
- macOS: **avoid controls or critical information at the bottom of a window** (people drag
  windows off the bottom); keep the camera housing clear.
- Adapt to size classes, not device type; text can grow, so rows can grow and side-by-side
  can stack.
- **A section's name belongs to its container.** Proximity is grouping: a header floating in
  the gap between two boxes is as near the box above as the one below, so on this Desk the name
  sits in a header bar inside the card (headline style, one full-width hairline under it), and
  the card's edge is what ends the section (`forms.md`, rule 5).
- **Width buys columns, not line length.** A window of several sections flows them as cards on
  tracks of at least ~420px that share what is left: wider is more cards across, narrower is a
  stack. Cap each card at a reading width, never the window; leave a short card its own height
  beside a tall one (`forms.md`, "Sections in a window").
- **Compact related controls sit side by side.** Two short controls that refine one thing share
  a split row, each labelled; they stack when the card cannot hold both.

## Tells that a screen skipped the guidelines

Nested boxes · a grid of outlined cells for a choice · a border on anything inside a bordered
group · vertical rules (a split row's one short divider excepted) · all-caps tracked labels on
every heading · a section name floating above its box · one capped column with half a wide
window empty beside it · cards stretched to their tallest neighbour · ragged shelves of tiles
under sub-headings · the accent on a value · two filled buttons · a hero number inside a form ·
a control pinned to the far edge away from its label · colour as the only state · body text
under 10pt · a placeholder with no label.
