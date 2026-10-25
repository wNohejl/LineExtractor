# Wizards: bordered groups, width, the card look, the bet-and-price group

A design brief for multi-step windows (the Place wager window is the case in hand). It
amends `forms.md` in one place: **a group may carry a 1px stroke** — the HIG's Boxes page
says a box "uses a visible border or background color", so the stroke is allowed; what it
forbids is nesting them. Everything else in `forms.md` stands. Values are CSS px on the
desk's dark tokens (body `#232326`, surfaces `#1C1C1E/#232326/#2C2C2E/#38383A`,
separators `rgba(255,255,255,.10)` / `.16`, accent `#0A84FF`, radii 7/12, body 13px).

## Bordered groups (values)

How macOS draws it (from memory: System Settings in macOS 13–15 and 26, SwiftUI
`Form { }.formStyle(.grouped)`): every group is a rounded rectangle with **both** a fill a
little lighter than the window and a hairline stroke a little lighter again. Rows inside
are divided by inset hairlines; there are no strokes on rows, no vertical rules, no cells.
Labels lead in the primary label colour; the control trails, right-aligned to the group's
trailing padding. The older Preferences / Xcode-options form (right-aligned labels with
colons, controls in a column to their right) is the *label-column* variant below.

Rules:

1. **The stroke belongs to the group, never to a row.** One element per group gets it.
   - fill: `#2C2C2E` on a `#232326` body (or `rgba(255,255,255,.045)` if the body varies)
   - stroke: `1px solid rgba(255,255,255,.10)`; draw it as `box-shadow: inset 0 0 0 1px`
     or `border` with `box-sizing: border-box` so it does not change the layout width
   - radius: `10px` (macOS 13–15 Settings ≈ 10pt; macOS 26 rounds a touch more — `12px`,
     the desk's large radius, is acceptable; pick one and use it for every group)
   - no shadow. Depth in dark mode is fill and stroke, not shadow.
2. **Row separators**: `1px rgba(255,255,255,.08)` (quieter than the group stroke), inset
   `12px` from the leading edge **and** `12px` from the trailing edge, as Settings does;
   never on the first row, never touching the stroke.
3. **Rows**: min-height `36px` for a row with a pop-up, field or segmented control,
   `32px` for a label/value row, `44px` for a row with a 2-line label (title + footnote);
   padding `6px 12px`; label/control gap at least `12px`.
4. **Labels**: 13px regular, primary label colour, sentence case, no colons in the
   leading-label form. In the label-column form (rule 7 below) labels are right-aligned
   in a column, `text-align: end`, colon optional; column `112–140px`, one width per group.
5. **Controls inside a group** use the third surface: field well `#1C1C1E` (inset) or a
   pop-up/segmented track `rgba(255,255,255,.08)`, radius `6–7px`, height `22–24px`
   (macOS regular control size). A control never gets its own 1px outline at rest.
6. **Focus**: the focused control (not the row, not the group) gets the accent ring:
   `outline: 3px solid rgba(10,132,255,.55); outline-offset: 0;` following the control's
   radius. Invalid input: the same ring in system red at `.6`, plus a footer sentence.
7. **Headers and footers sit outside the stroke**: header 11px semibold secondary
   (`rgba(235,235,245,.60)`), `6px` above the group, indented `12px`; footer 11–12px
   secondary, `6px` below, indented `12px`. Space between groups `20px`; no line.
8. **NSBox primary style** (memory): a light fill (`quaternarySystemFill`-like, ≈ `.05`
   white in dark) with a hairline stroke (≈ `.10` white) and a ~5–6pt radius, title above
   it in 11pt. It is the right look for a single box in a pane, and the one this desk
   reproduces at 10px radius. NSBox's *secondary* style is a separator line only.
9. **"Bordered" table style** (HIG Lists and tables, fetched): macOS's bordered style "uses
   alternating row backgrounds" for large tables. It is a data-table style, not a form
   style — do not use zebra rows in a wizard.
10. **Never** a stroked group inside a stroked group or card. If a container is stroked,
    everything inside it is divided by hairlines, padding and fill only.

## Wizard layout and width (breakpoints)

What Apple's assistants do (memory unless noted):
- **macOS Installer**: fixed window (~620×440pt). Leading sidebar (~170pt) lists every
  step with a small dot (grey = waiting, blue = current, hollow/grey = done) — the step
  list *is* the progress indicator. Content pane to its right is one bordered box with the
  step title above. Go Back / Continue at the bottom trailing edge; Continue is default.
- **Setup Assistant / Migration Assistant** (Ventura+): no step list. A centred column
  (~560–640pt) holding symbol, large title, one guidance paragraph, then the content
  (often an inset grouped list), Back and Continue at the bottom (Back leading of
  Continue, both trailing in Migration Assistant; split edges in Setup Assistant).
- **Xcode new-project assistant**: sheet ~740pt wide; step 2 is a label-column form
  (right-aligned labels ~140pt, controls in a column); Cancel bottom leading,
  Previous / Next bottom trailing.
- **Apple Account / Family Sharing sheets** (System Settings): centred icon, title,
  2-line guidance, one grouped list, buttons trailing at the foot.
- HIG Sheets (fetched): the Back button "lets people navigate to a previous step in a
  multi-step flow"; avoid showing Cancel, Done and Back together; for long flows prefer a
  window to a sheet. HIG Layout (fetched): adapt to window size, stack side-by-side views
  when space runs out, use more space to show more (e.g. a sidebar), and keep content
  "horizontally centered at very large sizes".

So: a title + one guidance sentence on top, one content column with a reading cap, and a
progress indicator that is either a step list (Installer) or folded step lines (this
desk's rail). Continue is the one filled key.

Rules for a resizable desk window. **Measure the window body, not the viewport**: use a
CSS container (`container-type: inline-size` on the window body) and `@container` queries.

| Body width | Layout | Columns |
|---|---|---|
| `< 480px` | **(a) stacked** | one column, `16px` gutters; labels above controls (11px secondary, `4px` gap); segmented controls full width; price row wraps; summary folds into the Review step; buttons full width, Continue above Back |
| `480–879px` | **(b) label-column form** | one column capped at `600px`, left-aligned to a `24px` gutter (centred once the body exceeds `648px`); label column `120px` (`104px` under 560) |
| `880–1279px` | **(c) form + summary** | form column `minmax(440px, 640px)`, gap `24px`, summary `300px`, sticky to the top of the scroll area; the pair is centred |
| `≥ 1280px` | **(c) widened** | form `680px`, summary `340px`, gap `32px`; total capped at `1052px` + gutters and centred; the rest of the window is plain body, never a stretched form |

- Never stretch a form past `680px`; empty margin at 1920px is correct when the content
  is centred and the summary fills the second column. A sea of space comes from a
  560px form pinned to the left, not from centring.
- The summary column (c) is a bordered card titled "Your wager": rows for Bet, Price
  (with book), Stake, To win, and the Review checks (mark + sentence each), then the
  **Log** key (filled) at its foot. In (c) the Log key lives here only; in (a)/(b) it is
  the filled key at the foot of the Review step. Never two Log keys on screen.
- Do not add an Installer-style step sidebar as a third column: the folded step lines
  already are the step list. If one is wanted, it replaces the folded lines at `≥ 1280px`
  (sidebar `200px`) and the summary stays — never both a sidebar and a rail.
- Back / Continue: bottom of the current step, trailing, Continue filled and rightmost,
  Back plain to its left; Cancel is the window's close control, not a third button.
- Change layout only at these breakpoints; between them only the column widths flex.
  Same functions at every width (HIG Layout: don't change functionality with size).

## Reconciling the card look

The look the user liked: a header card (title, subtitle, chips), one main card with only
the current step expanded, other steps folded to one line with 16px ring markers on a
2px rail, and a status note. It is HIG-compatible when the cards are the **only stroked
level** and siblings, not nested.

1. **Two sibling cards, one stroke level.** Header card and steps card share the group
   values above (fill `#2C2C2E`, stroke `.10`, radius `12px`), gap `12px`. Nothing inside
   either card is stroked. (Boxes, fetched: "Consider using padding and alignment to
   communicate additional grouping within a box".)
2. **Header card**: padding `16px`; title 17px semibold (title 3); subtitle 12px
   secondary on the next line; chips trailing on the title line or wrapping beneath it —
   chips are 20px tall, radius 10px, 11px medium text, fill `rgba(255,255,255,.08)`,
   no stroke, no accent unless the chip is pressable. At most four chips.
3. **Steps card**: padding `4px 0`. Each step is a section of the card divided from the
   next by a full-width hairline `rgba(255,255,255,.08)` — hairlines, not gaps of body.
4. **Rail and markers**: a marker column `40px` wide; ring centre `20px` from the card's
   leading edge. Ring `16px` diameter. Waiting: `1.5px` ring `rgba(255,255,255,.28)`,
   hollow. Current: `2px` accent ring with a `6px` accent dot. Done: filled accent disc
   with a 9px white check. Rail `2px` `rgba(255,255,255,.12)` from ring to ring, accent
   (`#0A84FF` at `.7`) on segments between done steps. The rail runs behind the folded
   lines and the expanded step's left edge — it is the progress indicator; no separate
   "Step 2 of 3" bar.
5. **Folded step line**: `40px` tall; title 13px semibold, then its choice in secondary
   ("Yankees −1.5 · +120 · DraftKings"); a plain "Edit" text button trailing. The whole
   line reopens the step. A waiting step says what it waits for in tertiary.
6. **Expanded step**: title 13px semibold on the marker line, guidance 12px secondary
   beneath, then its rows **inside the card**, indented to the title's leading edge
   (`40px`), right padding `16px`. Rows here are hairline-divided (rule 2 values) with no
   group fill or stroke — the card is the group. If a step needs two groups, separate them
   with a header (rule 7) and `16px`, not a second box. Back / Continue at the foot of the
   step, trailing, `12px` below the last row.
7. **Status note**: under the steps card, outside it; a 13px symbol + 12px secondary
   sentence, `8px` below the card, aligned to the card's content edge (`40px`). A warning
   uses the orange symbol and primary text; never a tinted box unless it blocks logging.
8. **Choose one look per window.** Either the card look (strokes on the two cards, rows
   bare inside) or the grouped-form look (strokes on each group, no outer card). Mixing
   the two is the nested-box failure.

## The bet-and-price group recipe

One object: "what you are betting and what it pays". In the card look it is the
expanded step's content; in the grouped-form look it is one stroked group.

```
Your bet                                          ← header, outside the group
┌─────────────────────────────────────────────────────┐
│ Moneyline   ( NYY −135   |   BOS +115 )             │ 36px rows, label col 120px
│   ─────────────────────────────────────────         │ inset hairline .08
│ Spread      ( NYY −1.5 +120 | BOS +1.5 −140 )       │ ← chosen segment raised
│   ─────────────────────────────────────────         │
│ Total       ( Over 8.5 −110 | Under 8.5 −110 )      │
│━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━│ full-width hairline .16
│ Yankees −1.5            +120   [ DraftKings  ⌃⌄ ]   │ price row, 44px, fill .03
│ Spread · at Red Sox                                 │
└─────────────────────────────────────────────────────┘
Best price across 6 books. Prices refresh every minute.   ← footer
```

1. **One choice across three controls.** Only one segment in the whole group is selected;
   choosing a side in one market clears the others. Unchosen rows show no selection.
2. **Segmented controls**: track `rgba(255,255,255,.08)`, radius `7px`, height `24px`,
   equal-width segments (HIG: "keep segment size consistent"), 12px text, side name in
   primary and price in secondary tabular figures. Selected segment: raised thumb
   `#636366`, radius `6px`, text white — the macOS look; the **accent is not** the
   thumb colour, so the one accent left in the group is the focus ring and Continue.
   All three controls share one width (the control column), so the segments line up
   down the group.
3. **The price row closes the group and echoes the choice.** Separated by a
   **full-width** hairline `rgba(255,255,255,.16)` (the stronger separator, not inset), a
   fill `rgba(255,255,255,.03)` on the row only, height `44px`. Leading: the chosen side
   as a 13px semibold line ("Yankees −1.5") over a 11px secondary line ("Spread · at Red
   Sox"). Trailing: the price at 17px semibold tabular, primary colour (forms.md rule 6),
   then the book pop-up. With nothing chosen, the row reads "Choose a side above" in
   tertiary and the pop-up is disabled — not hidden.
4. **The tie between segment and price row** is the echo text and the shared group, plus
   one optional accent touch: a `2px` accent hairline along the *top* of the price row,
   full width, appearing when a side is chosen (`rgba(10,132,255,.6)`). Use it or the
   `.16` separator, not both. A 150ms cross-fade on the echo text when the choice changes.
5. **Book pop-up = macOS pop-up button**: height `22px`, radius `6px`, fill
   `rgba(255,255,255,.10)`, no stroke, 12px text, padding `0 6px 0 9px`, an up/down
   chevron glyph (`chevron.up.chevron.down`, 9px, secondary) at the trailing end, the
   current book's name as its label. Its menu lists each book with its price, best first,
   with a check on the current one. It sits in the price row, never on a row of its own.
6. **Narrow (< 480px)**: market label above each control (11px secondary); controls full
   width; segment text truncates the team name first, never the price; the price row
   wraps to echo on line 1, price + pop-up trailing on line 2.

Avoid: the price in the accent colour or as a hero figure; the price row as its own card
or box; a coloured thick leading bar; a vertical rule between label and control; zebra
rows; the same price twice in one row (the segment may show the market price and the
price row the chosen book's, nothing more); all-caps market labels; three separate
groups for three markets; a "Price" label on the price row (the echo is the label); and a
stroked group inside the steps card.

## Sources

Fetched 2026-10-25 from `https://developer.apple.com/tutorials/data/design/human-interface-guidelines/<page>.json`:

- `boxes.json` — a box uses "a visible border or background color"; keep it small relative
  to its view; use padding and alignment for grouping inside a box; nested boxes make an
  interface "feel busy and constrained"; iOS boxes use secondary/tertiary backgrounds;
  macOS puts the title above.
- `lists-and-tables.json` — iOS grouped style uses headers, footers and space; macOS
  "bordered" style uses alternating row backgrounds for large tables; alternating colours
  for wide multicolumn tables.
- `layout.json` — group with negative space, container shapes or separators; align to aid
  scanning; adapt to window size; stack horizontally adjacent views when text grows; use
  more space to show more (e.g. a sidebar) without changing functionality; keep content
  horizontally centred at very large sizes; readable-width layout guides.
- `sheets.json` — Back is for previous steps in a multi-step flow, not dismissal; avoid
  Cancel + Done + Back together; button placement may vary per step; for long flows on
  macOS consider a window; default size that keeps context.
- `windows.json` — macOS windows are freely resizable; set minimum and maximum sizes so
  layouts don't overlap when small or become unwieldy when large.
- `split-views.json` — use multiple panes only in regular widths; consider narrow and
  intermediate widths; prefer the 1pt thin divider; let people hide a pane.
- `sidebars.json` — consider hiding a sidebar automatically when the window narrows; no
  critical actions at a sidebar's bottom.
- `toolbars.json` — concise window titles (under ~15 characters); standard Back/Close.
- `settings.json` — task-specific options belong in the task, not in Settings.
- `onboarding.json` — keep flows brief; postpone nonessential setup steps.
- `segmented-controls.json` — equal segment widths; about five segments or fewer; nouns
  for labels; text or images, not both; labelled segments need no introductory text.
- `pop-up-buttons.json` — flat list of mutually exclusive options; the button shows the
  current choice; a useful default; an introductory label to predict the options.
- `progress-indicators.json` — determinate where possible; consistent location (about
  operations, not step flows; no step-indicator guidance on that page).

From memory (not on the fetched pages; treat values as approximations to verify against a
screenshot): System Settings grouped-form fill/stroke alphas, the 10pt radius, 12px
separator insets and row heights; NSBox primary appearance; the macOS Installer window
size, step sidebar and dots; Setup Assistant and Migration Assistant column width and
button placement; Xcode new-project sheet width and label column; Apple Account / Family
Sharing sheet structure; the macOS selected-segment thumb colour; the pop-up button's
22px height and chevron. The breakpoints and column widths are this brief's own
recommendation, derived from those layouts and the 13px desk density.
