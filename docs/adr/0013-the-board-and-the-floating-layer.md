# ADR 0013 — The board, and the floating layer the desk reserved

**Status:** Accepted

**Builds on:** [ADR 0007](0007-window-manager-ui.md), which kept the desk a strict tiled row and
said arbitrary placement is what modals and popovers are for — reserving `--z-modal` for a
layer nothing had yet built.

## Context

Everything on the desk so far answered an operational question: is ingestion healthy, what did
this player do, did my price beat the close. None of it answered the question the product is
*for*, which an operator asks before doing anything else: **where is the best number right
now, and which book has it.**

Four books quoting the same game disagree, and the disagreement is the entire value of looking
at four books. A screen that shows four price tables makes the reader do that arithmetic; a
screen that shows one price hides the reason there are four.

## Decision

### The board shows one number per market, and how far the rest are behind

Each market cell carries the best price, the book holding it, and a **spread rail** — a
hairline with a tick per book, each placed by its implied probability between the best and
worst on offer. Books that agree collapse to one mark; a market worth shopping visibly spreads
out. The width of the group *is* the value of shopping, so a column can be scanned for where
the work repays before any number is read.

This is the one loud element on an otherwise quiet screen, and it is positional rather than
decorative — the same discipline the pulse strip earned in ADR 0007.

**Books get a monogram, not a colour.** DK, FD, B3, MG. Hue means state on this desk and
nothing else (ADR 0008), so book identity is carried typographically instead. It also stays
legible at the size a dense board needs.

### "Best" is a per-market judgement, not a maximum

On a moneyline the best price is the lowest implied probability. On a handicap the **line
outranks the price**, because half a point is usually worth more than a few cents — +1.5 at
-110 beats +1.0 at -105, and a comparator sorting on price would recommend the worse bet with
total confidence. Which line is better depends on the side: an over wants the lowest total, an
under the highest.

The label distinguishes a price gap from a **differing line**, because the second is invisible
in the first: three books quoting -110 on totals of 7, 8 and 9 have a price spread of exactly
zero and the largest real difference on the board. Reporting that as "books agree" would be
confidently wrong about the one row worth shopping hardest.

### Actions live on the row; the data they open floats

Clicking a row reveals its actions in the matchup cell — more bets, place a wager, form.
On the row, because the operator has just decided *this game* is interesting and the next move
should be one press away rather than a navigation.

Each action opens a `DeskDialog`: a draggable panel above the desk, **not a modal**. Several
stay open at once and get arranged, because the workflow being served is comparison — a scrim
would force finishing with one game before looking at the next, which is the opposite of what
a board is for. Re-opening the same view for the same game raises what is already there rather
than stacking a duplicate.

The dialog wears the window's own chrome — same title bar, same controls, same shadow — so it
reads as a member of the desk rather than a web dialog that wandered in. Drag runs entirely in
the browser and calls .NET once on pointer-up, for the reason ADR 0007 gives: a
`@onpointermove` handler over a SignalR circuit puts a network round trip inside every frame.

### Placing a wager retypes nothing

The board already knows the game, market, side, price and book. Re-entering any of it is an
opportunity to record a bet that was not made, so the form starts filled and asks only for the
stake. It defaults to the market with the widest spread between books, because that is usually
why the game was opened.

## Consequences

- The board reads the scan tier live. Nothing is precomputed, because best price is only true
  until the next scan and a cached board is one that lies quietly.
- A game nobody has priced still appears. It is on the slate and not yet shoppable, which is
  information; filtering it out would make the board disagree with the schedule.
- Each side is priced at *its own* best book, so the two halves of a handicap can show
  different numbers — Mariners +3 at one book, Astros -2 at another. That is correct for line
  shopping and reads oddly if mistaken for a single book's card.
- `DeskDialog` is not modal and has no focus trap. That is deliberate for comparison work, and
  it means the layer is unsuitable as-is for anything destructive that needs confirming.
- The demo fixture now shadows the configured book list rather than hard-coding two. A fixture
  that cannot exercise the screen it exists to demonstrate is not doing its job.

## Amendment — windows first, floating on overflow

Follow-ups now **add a desk window** while there is room, and float only once the desk is full.

A window is the better home: it tiles, holds its place in the row, and survives being scrolled
past. The reason it was not the first choice is that the desk enforces its ceiling by
*evicting* the least-recently-used window (ADR 0007) — and silently closing the board an
operator is working from, to make room for a price table opened *from that board*, is exactly
the wrong trade. So the ceiling became the switch rather than a problem to route around: while
there is room, follow-ups are windows; once there is not, the floating layer takes the
overflow and nothing gets pushed off the desk.

Each view therefore takes a **game id rather than a row**, because a window is rebuilt from its
parameters and cannot carry an object. They load their own data, which also makes them
openable from anywhere — the launcher included.

## Amendment — the game view, and following a team or player out of it

Clicking a game used to open straight onto the price-movement chart. That answers one question
— which way has this number moved — and left "who's playing, how have they been playing, what
does the market say" each a separate click away, on a different window, opened from a different
place depending which screen you started on.

`GamePanel` replaces that as the click target everywhere a game is opened from — the board, the
slate, the journal, a player's game log — since `WindowShortcuts.OpenGame` is the one seam all of
them already went through. It shows lines, team data and players in one window, ordered by what
the question is: pregame, lines come first because nothing has happened yet to weigh against
them; live, the score is the headline and "top bets" — what's still open — leads, with team and
player form read as *does the game so far match the form* rather than a preview. The movement
chart did not go away; it is a button in the header (`Manager.OpenMovement`), for when the trend
itself is what's wanted rather than the full picture.

A team name is now a destination rather than a label. `TeamPanel` — new — answers "how has this
side actually been playing" independent of any one game: a recent record with a W/L strip and
the roster's form, both read live by `MatchupCrossReference.GetTeamAsync`. A player name already
had one (`PlayersPanel`'s game log, via `Manager.OpenPlayer`); the game view now reaches it
directly instead of it living one level down from a "Players" launcher entry. Both make good on
what a flat aggregate leaves out: the board and the game view show a season-window *summary* per
stat, which answers "who's been good" but not "what actually happened game to game" — that
question now has an answer one click away, for a team as a schedule of results and for a player
as the line-by-line log it already had.

This is also the reference-id argument from the section above, applied one level further in:
following a name works because `Team.ExternalIds` and `Player.ExternalIds` — the same per-source
identifier map that let `IScheduleReader` resolve a game onto ESPN's real schedule — are what a
roster query and a recent-games query key off, rather than matching on name at the point of
display. The gap explanation, the score, and the drill-down all trace back to the same idea: an
identifier carried through the pipeline is what "click a name, get the right thing" is built on.

## What building it found

**MudDataGrid's `ChildRowContent` needs the grid's own expansion state**, which is driven by a
`HierarchyColumn` the board has no use for. Rather than add a chevron column to satisfy the
machinery, the actions moved into the matchup cell — which turned out better: no dead width on
closed rows, and only the open row changes height.

**Line formatting was wrong in three places independently.** A handicap is signed and a total
is not, but both reach the UI as the same nullable decimal, so an NBA total of 223 printed as
"+223" — a 223-point handicap. Three components had each written their own formatter. The rule
now lives in `OddsMath` with the rest of the odds vocabulary, which is where it should have
been the first time.

**The demo source was running a parallel universe.** Reported as a bug — "Yankees at Phillies
has no odds" — and true of almost every real fixture: the fixture generated its own games from
a fixed eight-team roster, so the desk held ESPN's real slate with no prices beside invented
matchups with prices, and 31 of 33 upcoming games were fabricated. A board of mostly-fake rows
looks populated, which is worse than looking empty, and the one real row read as a broken feed.

The fix is that the fixture now *prices the real schedule* through a new `IScheduleReader`.
That also made it a much better test: the games have to resolve by team name and start time,
which is exactly what a real provider's event list will have to do. Confirmed on the reported
game, which now carries both a `demo` and an `espn` external id on one row.

**A gap needs a reason.** Dashes are ambiguous between a broken feed, a fixture nobody quoted,
and a game already under way — and only one is worth acting on. Each unpriced row now states
which, and the header reports coverage as a fraction rather than leaving it to be inferred from
a screen of dashes.

**Store-on-change makes "age" the wrong word.** The header first read `oldest 1.1d`, which
implies stale data; because prices are only written when they move (ADR 0001), an old timestamp
means the line has *not moved*. It now reads `quietest line 1.1d unmoved`, which is the same
number saying the true thing.
