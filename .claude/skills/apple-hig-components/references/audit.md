# Audit: run before a screen is called done

Answer each with a yes, or fix it. Then take a screenshot at a narrow and a wide width.

## Components
- [ ] Every "pick one of a few" is a segmented control or radio buttons, never a grid of
      buttons or tiles.
- [ ] Every "pick one of many" is a pop-up button that shows the current choice.
- [ ] Every text field has a label as well as a placeholder, a width that fits its input,
      and the same width as its siblings in the group.
- [ ] Every amount that nudges has a stepper or quick values **beside** its field.
- [ ] Every on/off is a checkbox (form) or a switch (list row), never a button pair.

## Grouping
- [ ] Groups and cards are fills with **one 1px hairline edge** (house rule); rows inside are
      divided by hairlines and padding, never framed again; no box inside a box inside a box.
- [ ] One inset hairline between rows; no cell grid. The only other lines inside a card are
      the bar's one full-width hairline and a split row's short vertical divider.
- [ ] Every section is **one distinct card** with its name **in a header bar inside it**
      (headline style, sentence case) — never a grey line floating above the box, attached to
      nothing. Can you tell, at a glance, where each section starts and ends?
- [ ] The bar holds at most one trailing thing (a small key, a count, a 2–5 segment filter),
      never the window's Filled key; nothing in the bar, a cell or a row draws a border or a
      shadow.
- [ ] A table in a card draws no second frame and no doubled bottom edge: the card is its
      frame, its text starts where the bar's title does, and its last row has no hairline over
      the card's edge.
- [ ] Labels lead in one aligned column, and only where a label genuinely leads a control;
      controls trail; no control floats in a group without a label or a row of its own.
- [ ] Footers explain **under** the card, never inside it; a warning note sits between the card
      and its footer.
- [ ] Two compact, related controls (a short gate, a row of swatches) share a split row, each
      with its own label; two full form rows are not squeezed into one.
- [ ] Groups are separated by space, not lines.
- [ ] Each card of form rows stays within a reading width; nothing pinned to the far edge away
      from what it belongs to.

## Sections in a window
- [ ] A window of two or more sections lays them out as a flow of cards: at its widest,
      the extra width became **more columns**, not wider cards, and no half of the window
      stands empty beside a single capped column.
- [ ] Cards align to the start of their row: a short card beside a tall one keeps its own
      height, with no stretched empty box beneath its rows.
- [ ] Only a card whose body is a grid of its own (tiles, a chart, a wide table) spans the
      row or the rest of it; every card of form rows takes one track.
- [ ] A long collection (a dozen tiles or more under headings) is one grid with a filter in
      its card's bar (opening on All, not on a heading that leaves a wide card mostly empty),
      and the footer names the current choice —
      not shelves under sub-headings.

## Actions
- [ ] One filled (prominent) button per view, non-destructive, verb-first label.
- [ ] Destructive is red and never primary; cancel/escape is plain.
- [ ] Hit regions ≥ 44pt where fingers may land; a space between controls.
- [ ] A disabled key says what it waits for (tooltip or a line beside it).

## Type and colour
- [ ] Hierarchy by text style and the four label levels; no all-caps eyebrows, no new sizes.
- [ ] Numbers that change or align use tabular figures.
- [ ] The accent appears on interactive things and the one primary action only.
- [ ] State is never colour alone; red/orange/green sit on values, tags and marks.
- [ ] 4.5:1 or better on all text in both appearances.

## Width
- [ ] Screenshot at the window's widest and at ~300px (a window beside other windows).
- [ ] Narrow: each label stacks above its control (HIG: adjacent views stack as space
      shrinks). Drive it from the group's own width (a container query), not the viewport.
- [ ] Narrow: a split row's cells stack, the vertical divider turning into the ordinary inset
      row hairline; the bar's filter drops beneath its title rather than squeezing it; the flow
      is one card wide and nothing scrolls sideways.
- [ ] Narrow: every segment of a control breaks in the same place; nothing clips to two
      letters and nothing runs into its neighbour.
- [ ] An override that must win (a narrow-width rule) is placed after the rule it overrides
      or is more specific: check the computed value in the browser, not the source.

## Disclosure and sequence
- [ ] A numbered rail only where the content is a sequence.
- [ ] One step open; done steps fold to one line that reopens them; waiting steps say why.
- [ ] Nothing on screen that cannot be used yet.

## Worked example: the wager window (October 2026)

Before: a boxed header with an accent rule; a bet group outlined, holding a 3×2 grid of
outlined cells with a large accent price in a second outlined section; an outlined stake
box; an outlined review box; all-caps tab labels; keys spread along the row.

After: the title and its detail line on the body; three steps on a rail; step 1 a filled
group of three rows — Moneyline / Spread / Total, each a two-segment control — an inset
hairline, then a Price row (title 2 figure, pencil, book pop-up) with the value and capture
age as the group's footer; step 2 a group of Stake (field + quick values) and Note rows
with returns as its footer; step 3 the checks as rows with the sentence as footer and the
one filled key leading. Same information, no outlines.

## Worked example: the Window manager (November 2026)

Before: one 680px column of seven groups, each under a quiet grey name standing above it, so a
name sat nearer the box before it than its own and the sections ran together; at 1600px the
right half of the window was empty. Appearance was one long group: Mode, Accent, then
twenty-three theme tiles in four ragged shelves under floating headings, then the looks.

After: a flow of cards, three across at 1600px and one at ~680px, each card's name in its header
bar. Appearance leads with Mode beside Accent in one split row, then Glass and the makes as form
rows; Themes is its own card taking the rest of the first row, one grid of tiles with a
segmented filter (All, Built in, Character, Soft, Vivid) in its bar opening on All, and a
footer naming the theme showing and its heading. Scale, Windows, Primary window,
Resolution and Workspaces each take one track and keep their own height.
