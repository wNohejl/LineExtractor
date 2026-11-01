# Game window: movement and head to head in place

**Status:** Built · 2026-11-01
**Touches:** `GamePanel.razor`, new `MarketMovement` and `GameHeadToHead` snippets, `HeadToHeadSnippet.razor` (takes rows it is handed), `lineops.css`. No schema change, no new service.
`lineops.css` (the `gamelines` block). No schema change, no new service.

## The problem

The Game window answers "what is the number" and then sends every follow-up question to
another window. Movement opens Line movement beside it; Head to head opens a third. On a
1440-wide desk the screenshot of 1 November shows the cost: the Game window gave up half the
screen to a Movement window that said only *"No stored prices for this game and market"*,
followed by a 30-day player table that repeats the Game window's own Players tab with every
name cut to three letters.

Three things are wrong, in order of how much they cost:

1. **A follow-up opens a window.** Movement and H2H are questions *about this game*, asked
   while looking at its lines. Opening a window moves the answer away from the question and
   halves the width of both.
2. **Movement is empty exactly when the game is on.** Scans are pruned at first pitch
   (ADR 0010); a live or final game has one close per book and no path. The window's empty
   state is correct and useless. Meanwhile the thing that *is* held, every book's close, is
   hidden in a hover tooltip ("Hover a price for every book's").
3. **The header carries controls for panes it does not hold.** The season switch only changes
   Team data and Players, but it sits on the title row beside Movement, Head to head and Place
   wager, so the row is four controls wide on every tab.

## Research: what the HIG says the controls are

| Job | HIG answer | Source |
|---|---|---|
| Show a market's detail under the market | **Disclosure triangle** on the row's leading edge: points at the label when closed, down when open; the row expands in place. Same idiom as Finder's list view. | `disclosure-controls.md` |
| Show H2H under the header | **Disclosure button**, near what it reveals, *no more than one per view*. Points down when hidden, up when shown. | `disclosure-controls.md` |
| Show movement compactly | A **small chart for glanceable information about one item**; keep it simple, let people choose detail; a table where nobody is analysing. | `charting-data.md` |
| Season for team and player data | Controls in a pane affect only that pane; a segmented control heads the view it switches. | apple-hig-components, *Segmented controls* |
| Open the whole record | A button that opens another window ends in "…". | apple-hig-components, *Buttons* |

A popover was considered for H2H and rejected: a popover closes on the next click outside it,
and the point of H2H is to read it *against* the lines below. A sheet blocks the window. The
disclosure keeps both on screen and costs height only while it is open.

## The design

```
┌ Kansas City Chiefs  7   ● Live                                         ┐
│ Denver Broncos      3                                                  │
│ Nov 1 16:25 ET · 2026 season                                           │
│                                    [▾ Head to head · KC 4–1]  [⚡ Place wager] │
│ ─ (open) ───────────────────────────────────────────────────────────── │
│   Oct 6 2025  Broncos at Chiefs   17-24   KC −3.5  Covered  O 44.5     │
│   … 4 more                         All 9 meetings…                     │
├────────────────────────────────────────────────────────────────────────┤
│ Your bets  2 pending · $235 at risk                          Journal   │
├ Top bets │ Team data │ Players ────────────────────────────────────────┤
│ ▸ Moneyline   Chiefs −120          Broncos +105                        │
│               BetMGM · closing     DraftKings · closing                │
│ ▾ Spread      Chiefs −1.5 −108     Broncos +1.5 −110                   │
│   ┊ Book        Chiefs        Broncos                                  │
│   ┊ BetMGM      −1.5 −108 ●   +1.5 −112                                │
│   ┊ DraftKings  −1.5 −110     +1.5 −110 ●                              │
│   ┊ FanDuel     −2.5 +105     +2.5 −125                                │
│ ▸ Total       Over 41.5 −105       Under 41.5 −110                     │
│ Closing prices: the last number each book showed before the start.     │
└────────────────────────────────────────────────────────────────────────┘
```

### 1. Movement lives in the market row (Top bets / Lines tab)

- Each market row gains a **disclosure triangle** on the market label. Pressing the label (or
  the triangle) opens the row; the side cells keep their own press, which is still the wager
  door. One row open at a time, as every other row on the desk.
- **What opens depends on what is held**, and the disclosure label says which:
  - **Before the start: "Moves".** A compact per-book table for the side in focus: Book, the
    sparkline on the shared time axis, Open, Now, Move. A two-segment switch (Chiefs / Broncos,
    Over / Under) heads the table, so it reads one side at a time exactly as Line movement
    does. A one-line headline above it ("Chiefs shortened at 3 of 4 books since Tuesday")
    carries the finding. Rows are `OddsPanel.MovementRows` and `Headline`, already static and
    tested; nothing new is computed.
  - **After the start: "Every book".** The scans are gone, so the row shows what is held:
    every book's close on both sides, in two columns, the best on each side marked with a dot
    *and* bold weight (never colour alone). This is the tooltip's content, made readable and
    reachable by keyboard. It needs no query: it is `BestOffer.Rungs` on the row already loaded.
- The table is capped at six books with "All 9 books" as a plain row beneath when there are
  more. The Line movement window stays in the catalogue and is reached from that row's
  "Open in Line movement…" key only; the header button goes.
- The footer drops "Hover a price for every book's", because every book is now one press away.

### 2. Head to head is a disclosure button in the header

- Replaces the window-opening button. **Collapsed, its label is the answer:** "Head to head ·
  KC 4–1" (straight-up record over the meetings shown, leader named first; "Head to head · no
  meetings" when there are none, disabled-looking but still pressable to say why).
- **Expanded,** it shows `HeadToHeadSnippet` (last five) directly under the title block, above
  Your bets and the tabs, so it stays visible whichever tab is selected: it is about the
  matchup, not a section of it. Below the rows, the snippet's coverage line and a plain
  "All 9 meetings…" key that opens the Head to head window for the sortable record.
- The open state is remembered per window (`WindowState`), so a refresh on the odds signal
  does not fold it.
- It is the view's only disclosure *button*; the market rows use triangles. That keeps to the
  HIG's one-per-view rule.

### 3. The header holds only what applies to every tab

- **Season switch moves into Team data and Players**, heading those panes (it already hides
  for a fight). Top bets never read it.
- **Movement button is removed** (it is the market rows now).
- The keys row is H2H and Place wager: two controls, Place wager the one Tinted action.

### 4. Your bets folds to a line at three or more

Two bets show as rows, as today. Three or more fold behind a disclosure triangle whose label
is the summary: "Your bets · 3 pending · $335 at risk". Settled bets lead with their result.
At two rows a fold saves nothing and hides the price taken.

### 5. Switching tabs loses nothing

- The open market row, the side in focus and the H2H state survive a tab switch and a refresh;
  they are window state, not component locals that a re-render resets.
- The Players tab keeps its name column at a minimum width (the one-line name) and lets the
  stat columns scroll; "Alo…" is not a name. This applies to the Line movement window's
  *Recent form* too, which duplicates Game › Players and is a candidate to drop once Movement
  lives here (not part of this change).

## Space budget, against the screenshot

| | Today | Proposed |
|---|---|---|
| Windows to answer "how did the spread move, and how have they gone against each other" | 3 | 1 |
| Header controls | Season, Movement, H2H, Place wager | H2H, Place wager |
| Height of a closed market row | 36px | 36px (unchanged) |
| Height of an open market row | n/a | ~36 + 28 × books (≤ 6) |
| H2H when closed | a button | the same button, carrying the record |

Nothing closed grows. Every new pixel of height is one the operator asked for by opening it.

## Behaviour details

- **Keyboard.** The market label is a `<button aria-expanded>` with the triangle inside it;
  Enter and Space toggle; the side cells remain separate tab stops. The H2H button is
  `aria-expanded` with `aria-controls` on the region.
- **Motion.** Rows expand with the desk's 200ms ease-out height transition; reduced motion
  swaps it for a fade.
- **Loading.** An opened pregame row shows `DeskSkeleton Lines="3"` in the row's shape while
  the scans load; the H2H region shows the snippet's own skeleton. Neither blocks the window.
- **Fights (ADR 0021).** Moneyline and Total rounds get the same disclosure; there is no
  Spread row. H2H for a bout reads "Previous fights", and is usually empty.
- **Visitor desk (ADR 0018).** Unchanged: side cells are readings, not doors; disclosures work.

## Out of scope

- In-play odds. Live prices are closes until a live market feed exists; the design names them
  "Every book" so it never implies a live path it does not have.
- Retiring the Line movement and Head to head windows. Both stay for the full, sortable views.

## Build order

1. Extract the market row into a `GameMarketRow` component with the disclosure and the two
   branches (Moves / Every book); reuse `OddsPanel.MovementRows` and `Headline` by moving them
   to `LineOps.Web.Services.MarketMovement` so the panel and the snippet share one source.
2. H2H disclosure button, summary from the five rows `HeadToHeadSnippet` already reads
   (lift the read so the label and the snippet share one query).
3. Move the season switch into the two panes; drop the Movement button.
4. Your bets fold; Players name column minimum.
5. bUnit tests: a started game's open row shows every book's close and no "Moves"; a pregame
   row shows the side switch; H2H label carries the record; open state survives a refresh.

## As built

- The market's disclosure is its name, a `<button aria-expanded aria-controls>` with the desk's
  disclosure triangle; the detail is `MarketMovement`, which reuses `OddsPanel`'s static readers
  (`MovementRows`, `Headline`, `SpanText`, `Quote`, `MoveText`) where they are, under
  `OddsMovementTests`, rather than moving them to a service.
- Head to head is `GameHeadToHead`, a row of its own directly under the title rather than a key
  in the title's row: the region it opens has to sit under the title, and a button in the keys
  row would be a screen away from it. It reads the meetings once for its label and hands them to
  `HeadToHeadSnippet`. Its open state, the open market and the side in focus are component state,
  which a refresh re-renders without resetting.
- A market with no scans before the start (nothing moved yet) opens onto every book's current
  number, the same table a close shows.
- Tests: `GameWindowInPlaceTests`.
