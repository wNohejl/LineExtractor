# Forms, wizards and grouped controls

The shape a screen takes when it asks the person for something: the macOS grouped form
and the iOS inset grouped list, which are the same idea at two densities.

## The grouped form

```
Title of the thing                                   Tag
One quiet detail line

Section name                       ← optional, footnote, secondary, sentence case
┌──────────────────────────────────────────────────┐  fill: one level above the body
│ Label          [ control ──────────────────── ]  │  row 28–36pt (macOS) / 44pt (iOS)
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ │  separator inset to the label's edge
│ Label          ( Seg | Seg | Seg )               │
│ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ ─ │
│ Label          [ field  ]  ◦ ◦                   │
└──────────────────────────────────────────────────┘  radius ~10–12pt, NO border
Footer: one or two sentences of explanation, footnote, secondary.

                      ← space, not a line, between groups
┌──────────────────────────────────────────────────┐
│ …                                                │
└──────────────────────────────────────────────────┘

[ Primary ]  Plain  Plain                            ← one prominent key, leading-aligned
```

Rules that make it read as a form and not a stack of boxes:

1. **The group is a fill with one hairline edge.** Secondary grouped background on the
   primary one, and a 1px stroke at the strong separator alpha (macOS System Settings draws
   its grouped forms this way; the house rule, decided October 2026 after a borderless pass
   was rejected, is that every group and card is bordered). What the HIG's Boxes page
   forbids is the box *inside* a box: a card may hold groups, a group holds rows, and rows
   are divided by hairlines and padding, never framed again. Depth inside a group is one
   more fill level (the well of a field), not a nested border.
2. **One hairline between rows, inset from the label's leading edge to the trailing edge.**
   Never a vertical rule; never a rule around the group; never a grid of cells.
3. **Labels lead, controls trail, in one aligned column.** The label column has one width
   per group so the controls line up down the page ("align elements to make them easier to
   scan"). Labels are body text in the primary label colour; a control that needs no label
   (a segmented control with labelled segments) has none.
4. **Fields share widths inside a group and match the expected input.** A stake field is
   short; a note field runs to the trailing edge.
5. **Footers explain, headers name.** The sentence that explains what a group will do
   ("It returns $210 if it wins.") is the group's footer, footnote size, secondary colour —
   not a box of its own.
6. **The one number the form is about is a value row, not a hero.** In a form, a price is
   title 2 or title 3 in the primary colour, tabular figures, beside its label — not a
   large-title figure in the accent. The accent is for what you can press.
7. **The primary action is one filled button at the end, leading-aligned with the groups**
   (macOS: the default button is trailing in a dialog; in a window body or sheet with
   content above it, align with the content). Plain buttons beside it. Nothing pinned to a
   far edge the eye has to travel to.
8. **Everything caps at a reading width** (around 560–760px). A form that stretches across
   a 1900px window puts its controls out of each other's reach.

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
