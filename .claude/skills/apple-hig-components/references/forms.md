# Forms, wizards and grouped controls

The shape a screen takes when it asks the person for something: the macOS grouped form
and the iOS inset grouped list, which are the same idea at two densities.

## The grouped form

```
Title of the thing                                   Tag
One quiet detail line

┌──────────────────────────────────────────────────┐  the card: fill one level above the body,
│ Section name                     ( All | Few )   │  one 1px hairline edge, radius ~10–12pt
│──────────────────────────────────────────────────│  header bar: headline title, one trailing
│ Label          [ control ──────────────────── ]  │  thing; ONE full-width hairline under it
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ │  row 28–36pt; separator inset to the label
│ Label     ( Seg | Seg )  ┆  Label   ○ ○ ○ ○       │  split row: two compact controls, each labelled
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ │
│ Label          [ field  ]  ◦ ◦                   │
└──────────────────────────────────────────────────┘
Footer: one or two sentences of explanation, under the card, secondary.

                      ← space, not a line, between cards
┌──────────────────────────────────────────────────┐
│ …                                                │
└──────────────────────────────────────────────────┘

[ Primary ]  Plain  Plain                            ← one prominent key, leading-aligned
```

Rules that make it read as a form and not a stack of boxes:

1. **The group is a fill with one hairline edge — a section card.** Secondary grouped
   background on the primary one, and a 1px stroke at the strong separator alpha (macOS System
   Settings draws its grouped forms this way; the house rule, decided October 2026 after a
   borderless pass was rejected, is that every group and card is bordered). What the HIG's
   Boxes page forbids is the box *inside* a box: a card holds rows, and rows are divided by
   hairlines and padding, never framed again. Depth inside a group is one more fill level (the
   well of a field), not a nested border.
2. **One hairline between rows, inset from the label's leading edge to the trailing edge.**
   Never a rule around a row; never a grid of cells. The two other lines inside a card are
   dividers, not edges: the bar's full-width hairline (rule 5) and a split row's short vertical
   one (rule 10).
3. **Labels lead, controls trail, in one aligned column.** The label column has one width
   per group so the controls line up down the page ("align elements to make them easier to
   scan"). Labels are body text in the primary label colour; a control that needs no label
   (a segmented control with labelled segments) has none. A label leads only where it
   genuinely leads a control; it is never a heading over loose controls.
4. **Fields share widths inside a group and match the expected input.** A stake field is
   short; a note field runs to the trailing edge.
5. **Headers name, inside the card; footers explain, under it.** The section's name is in a
   **header bar at the top of the card** — headline style (semibold, primary ink), sentence
   case — divided from the rows by one **full-width** hairline: the bar is the card's
   threshold, not a row, so its divider is not inset. Its fill sits half a step between the
   card's and the body's, so it reads as the card's head and not its first row. The bar trails
   **at most one** quiet thing: a small Plain or Tinted key, a count, or a 2–5 segment filter over
   the card's rows — never the window's prominent key. The sentence that explains what a group
   will do ("It returns $210 if it wins.") is the group's footer, under the card, secondary
   colour — not a box of its own, and never inside the card. A warning about the group's state
   is a note between the card and its footer, never inside the card. (Until November 2026 the
   name was a quiet grey line *above* the group, the iOS grouped-list header; on a window of
   several sections each name sat nearer the box before it than the one it named, and the
   sections ran together.)
6. **The one number the form is about is a value row, not a hero.** In a form, a price is
   title 2 or title 3 in the primary colour, tabular figures, beside its label — not a
   large-title figure in the accent. The accent is for what you can press.
7. **The primary action is one filled button at the end, leading-aligned with the groups**
   (macOS: the default button is trailing in a dialog; in a window body or sheet with
   content above it, align with the content). Plain buttons beside it. Nothing pinned to a
   far edge the eye has to travel to.
8. **Each group caps at a reading width; a window of several groups does not.** A group whose
   controls are a screen apart puts them out of each other's reach, so a card of form rows stays
   around 420–760px. But a window of several sections is not one form: capping *it* (one 680px
   column) leaves the right half of a wide window empty and the sections stacked a long way
   down. See "Sections in a window" below. (A single form — a wizard's one column — still caps
   at its reading width; `wizards.md`.)
9. **Narrow is the card's width, not the window's.** Labels stack above their controls when the
   card is narrow (around 400px), split cells stack when it cannot hold both (around 720px), and
   the insets tighten below about 480px — whatever the window is doing.
10. **A split row for two compact, related controls.** Two cells in one row, each with its own
    label leading its control, divided by a short vertical hairline inset top and bottom: a
    2–3 segment gate beside a row of swatches, a short field beside a toggle. The first cell's
    label takes the group's label column, so its control lines up with the rows beneath. It is
    not for two full form rows (those are two rows), and it stacks into two ordinary rows when the
    card is too narrow for both.

## Sections in a window

A window that holds several sections — a settings screen above all — lays them out as a
**flow of cards**, not a column:

- **More width is more columns, never wider cards.** Each card takes a track of at least ~420px
  (enough that its form rows keep their labels beside their controls, which stack under ~400px
  of card) and the tracks share what is left, so a 1600px
  window shows three cards across and a ~680px one stacks them. Do not cap the window with a
  max-width column; the track does the capping.
- **Cards keep their own height.** Align cards to the start of their row; a short card beside a
  tall one stays short rather than stretching into an empty box ("do not stretch the boxes").
- **Space between cards, not lines.** The card's edge and its bar are what separate one section
  from the next.
- **A card leaves its track only when its body is a grid of its own** — a library of tiles, a
  chart, a wide table — and then takes the whole row, or the rest of the first row beside the
  card that leads the window. A card of form rows always takes one track.
- **A long collection is filtered, not stacked under sub-headings.** Twenty tiles under four
  floating headings make ragged shelves and a card five shelves tall. Draw one grid and put the
  choice of heading as a small segmented filter (All, then each heading) in the card's bar,
  opening on All — one heading's few tiles in a wide card leave a large empty box; name the
  current choice and its heading in the footer, since a narrower filter can hide it.
- **Put together what refines one thing; give what wants width its own card.** Controls that
  refine one thing (a mode and an accent) share a card, side by side in a split row when both
  are compact; a collection that wants width (the tiles) is a card of its own.

## Choosing the control in a row

| The row asks for | Draw |
|---|---|
| one of 2–5 options, all worth seeing | segmented control; no selection shows no cap |
| one of many | pop-up button showing the current choice |
| on / off | checkbox (macOS form) · switch (iOS list row) |
| a small text | text field, placeholder + label |
| an amount with common values | text field + momentary segmented control of the common values, or a stepper |
| a date | textual date picker (macOS) · compact date picker (iOS) |
| a book, a team, a source from a known set | pop-up button; a combo box if typing is also allowed |

## Wizards and steppers (the multi-step kind)

- A numbered rail is right **only when the content is a sequence**; a form with three
  independent groups is not a wizard.
- Open one step; a done step folds to its title and its choice on one line, and that line
  is the link that reopens it; a waiting step says in one line what it waits for.
- Each step's guidance is one or two sentences in the secondary colour under the title,
  written for someone who has never done it.
- The review step is a grouped list of facts (a mark, a sentence, a remedy key where there is
  one) with the summary sentence as its footer, and the primary key beneath.

## Tabs over a form

A tab view when two closely related panes share the area (the form and the slip it feeds).
Tabbed control at the top, labels nouns in title-style ("Wager", "Slip · 2"), inset from
the window body, never more than six, and each pane's controls affect only that pane.
