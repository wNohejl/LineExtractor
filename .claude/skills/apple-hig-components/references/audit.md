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
- [ ] Groups are fills on the body, **no borders**; no box inside a box.
- [ ] One inset hairline between rows; no vertical rules; no cell grid.
- [ ] Labels lead in one aligned column; controls trail; footers explain under the group.
- [ ] Groups are separated by space, not lines.
- [ ] Nothing wider than a reading width; nothing pinned to the far edge away from what it
      belongs to.

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
