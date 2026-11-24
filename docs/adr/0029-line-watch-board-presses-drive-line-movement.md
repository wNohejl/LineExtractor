# ADR 0029 — In Line watch, a Board press drives Line movement

**Status:** Accepted.

## Context

Line watch was a workspace of two windows, the Board beside Line movement, and nothing joined
them. Line movement opened on whichever game it picked first, usually one with no moves on
record, and said "Every book is holding its opening number" over a table of "unmoved" and "seen
once". Pressing a game on the Board opened that game's row; reaching its movement meant the row's
Odds glance, then Movement, which opened a *second* Line movement window in another column. The
owner asked for the workspace to lead the desk, to carry the Journal, and for "board clicks
[to] be specific to line movement displays".

## Decision

**While Line watch is the workspace on the desk, a press on the Board shows that game in the Line
movement window already open.**

- *When it holds.* Line watch is recognised by its tag, `line-watch` (`Workspace.Tag`, which the
  Desk carries and never interprets), through `WindowManager.ActiveWorkspace` — **and** only while a
  Line movement window is on the desk. The Desk keeps a workspace as the active one while its
  windows are closed and others opened, and restores it with a saved layout, so the tag alone would
  go on taking the Board's presses long after the desk stopped being a line watch. Close the last
  Line movement window and the Board is the plain Board again, its note gone. Renaming the
  workspace changes nothing.
- *A row press* opens the row, as it always has, and shows the game in Line movement on the
  market and side the window is already on. The row stays open so its strip — glances, More bets,
  Place wager — is one press away.
- *A price press* shows that game, **that market and that side** in Line movement, and opens the
  row on it **without** opening the take. The price is being watched, not taken: the open row's
  Place wager takes **the price last pressed on it** (its market, side and book), falling back to
  the row's most value only when none was pressed. Closing the row, or opening another, forgets it.
  Pressing the row again closes it, as before.
- *One window, never a new one.* The Line movement window on the desk is re-aimed in place
  (`WindowManager.Aim` on its parameters), never a second one and never a new one: the one showing
  in its column if any is, else the one last in front. Every press carries a count (`Press`), so
  pressing the same price again after the operator has looked elsewhere in Line movement turns it
  back; a redraw that hands the window the same aim leaves the operator's choice alone. In a column
  of its own, a tab behind another — the Journal is one — comes forward. In the Board's own column
  (a column limit of one, or dragged there) it is aimed but left behind the Board, so a press never
  hides the slate it was made on. The Board keeps the desk's focus.
- *Said on the Board.* While the rule holds, the Board carries a note under its toolbar saying
  what a press does. It is text on the page, not a hover title.
- *Outside Line watch* the Board behaves exactly as before: a row press opens the row, a price
  press opens the take, Movement on the Odds glance opens a window of its own. Every book takes a
  price under its market in the same take, and no longer opens the Place wager window beside itself.

The workspace itself is the Board, Line movement and the Journal, with the Journal a tab behind
Line movement (`Columns: [0, 1, 1]`): at the default column limit a third column squeezed the
Board below the width its six market cells need, and the Journal is consulted rather than
watched. It opens on its pending entries while Line watch is active (a default, not a lock).
Line watch is first in the catalogue, so it is the workspace the empty desk features.

Line movement reads what is stored and nothing else: for a side nobody has moved it leads with
the fair price, each side's best, each book's distance from fair and how long it has held its
number, and says what could move them next — with line polling on Manual, only a pull — offering
the league's Pull lines through its own job menu, with its cost and the menu's confirm. No new
call is made to any provider.

## Consequences

- A row press in Line watch both opens the row and re-aims Line movement; the row's own take is
  one further press (Place wager). An operator who wants the take from a price cell in Line watch
  presses Place wager on the open row instead.
- Line movement re-reads when its parameters change (`OddsPanel.OnParametersSetAsync`), not only
  on first load. Re-aiming does not add a page to the window's Back history.
- The rule lives in the application (`LineWatch`), not the Desk: the Desk only carries the tag
  and the active workspace.

## Amendment, 2026-11-21 — nothing is chosen until something is pressed

Applying Line watch opened Line movement on a game the operator never pressed — the window's
standalone default, the next game to start ("Nov 21, Navajo Stirling at Jiří Procházka, no moves
yet") — and it read as the choice. In Line watch the window now chooses nothing:

- *Before a press* it shows the slate's overview: a one-line prompt ("Press a game or a price on
  the Board to follow it here"), then **Biggest moves today** — every book's row on every side of
  the games from the start of today to two days out, kept where the book's number now differs
  from where it opened and the change landed today, a line move ranked over any price move — each
  row a button that follows that game, market and side here. With nothing moved it says so and
  lists the next games by start time, each pressable too. Read from stored prices only
  (`ILineMovementData.SlateAsync`, `SlateMoves`), to the reader's tier.
- *After a press* (on the Board, in the overview, or the window's own game select) it follows
  that selection and says what it follows — "Following Bills at Chiefs · Spread · Bills" — with
  an **Overview** key back.
- *A restored selection is not a choice.* Every Board press carries a token this server issued
  (`LineWatch.IsCurrent`). A desk saved in an earlier run comes back with its old token, which
  this server never issued, so the window opens on the overview rather than on the old game. A
  reload against the same running server keeps the selection, which was made in that run.
- *Outside Line watch* the standalone Line movement window keeps its deliberate default: the next
  game to start, or the one that started last when none is ahead (`OddsPanel.NextUp`).

## Amendment, 2026-11-21 (review) — when the watch ends, and what it remembers

- *The watch drives its own window and ends with it.* The Line movement window the workspace
  opened is the watch's (`LineWatch.Drives`). Closing it ends the watch, even though the Desk keeps
  Line watch as the active workspace: the Board is the plain Board again, and a Line movement
  window opened afterwards from the strip — or a second one opened beside the watch's — is a
  window of its own, opening on the next game and never driven by the Board or shown the
  overview. Applying Line watch again (which replaces every window) starts a new watch. All of
  this is the application's (`LineWatch`), worked out from the desk as it stands; the Desk needed
  no change.
- *A press is today's.* A press token is kept with the league day it was issued on; a token is
  current only on that day, older ones are let go of as new ones are issued, and at most 4,096 are
  kept. A desk saved on an earlier day — or restored after a restart — opens on the overview.
- *The window says and saves what it follows.* Following a game from the overview writes it into
  the window's parameters and title, as a Board press does, so the title bar names it and a desk
  saved then comes back on it. The Overview key, and an old aim found at start-up, clear both: the
  title no longer names a game over the overview.
- *A move is said once.* One book moving a spread or a total moves both of its sides; the
  overview lists it once, by the larger side — the away side or the over where they are equal.
  The overview reads each book's first and last capture per side from the database, for sides
  whose last capture landed today, rather than every scan of two days of games.
