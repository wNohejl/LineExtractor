# Row-level drilldown — two-stage disclosure across the desk

**Date:** 2026-08-01
**Status:** Approved for planning

## Problem

The desk holds game-grain data and only ever shows window-grain summaries.

`PlayerGameStats` stores one stat line per player per game. `ClosingLines` stores what the
market said at first pitch. Neither is ever displayed per game. `TeamPanel` computes a
`12-8 over 20 games` aggregate from rows the operator cannot open, and `GamePanel` shows
per-player totals over a window without the appearances behind them.

Following a name also dead-ends. `game → team` works; `team → player` does not — a player
click opens the *Players list* window filtered to that player
(`WindowShortcuts.OpenPlayer`), which is a roster view, not a player view. There is no game
log anywhere in the product.

Separately, the Board has a good row-action pattern — click a row, buttons appear on it — and
it exists on exactly one panel. The Slate, which is the day-scanning surface, has none of it.

## Principle: two-stage disclosure

Every row on every surface behaves identically:

1. **Click a row** → it expands, revealing action buttons.
2. **Click an action** → a *snippet* opens inline beneath the buttons: enough to answer the
   question without spending a window. Each snippet carries one "Open window" affordance.
3. **Click that** → the full window opens.

The common case — glance at head-to-head, check a player's last five — costs no window slot,
so the desk stays in whatever shape the operator arranged. Windows are spent deliberately
rather than as a side effect of curiosity. ADR 0007's eviction ceiling stops being something
browsing can trip.

The pattern is used at every level, including inside the windows it opens. A game row in the
Team window expands the same way a game row on the Slate does. There is one interaction to
learn and it holds all the way down.

**One snippet per row at a time.** Selecting a second action swaps the first out rather than
stacking. This extends the existing "one open row at a time" rule in `BoardPanel.Toggle` for
the same reason: rows that grow without bound push the rest of the slate off screen, and the
snippet stops being obviously attached to its row.

## Components

### `RowAction` and `RowActions` — `src/LineOps.Web/Components/Desk/`

`RowActions` is the Board's inline action strip, extracted so Board, Slate, Team window and
Game window all use one control.

It carries the non-trivial behaviour that currently lives in `BoardPanel.OpenAsync`, because
that logic is what makes the pattern work rather than incidental to it:

- A window while the desk has room; a floating dialog once it is at capacity — so a follow-up
  never evicts the board being worked from.
- Re-opening the same view for the same subject raises what is already there instead of
  stacking a duplicate.

```csharp
public sealed record RowAction
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public DeskTone Tone { get; init; } = DeskTone.Neutral;

    /// <summary>The inline snippet. Null for an action that only opens a window.</summary>
    public RenderFragment? Snippet { get; init; }

    /// <summary>The escape hatch, rendered by SnippetShell. Null for snippet-only actions.</summary>
    public Func<Task>? OpenWindow { get; init; }
}
```

Parameters: `Actions`, `OpenKey` (the selected action, or null), `OnSelected`.

The host panel builds the action list. That keeps `RowActions` ignorant of markets, games and
players — it renders buttons and one snippet, and can be understood without reading any panel.

### `SnippetShell` — `src/LineOps.Web/Components/Desk/`

Wraps snippet content with a consistent frame: a title, an optional coverage note, and the
"Open window" button when `OpenWindow` is set. Snippets therefore contain only their own
content and never re-implement the escape hatch.

### Snippets — `src/LineOps.Web/Components/Snippets/`

Each is small, single-purpose, and takes ids rather than objects so it can be hosted anywhere:

| Component | Parameter | Shows | Opens |
|---|---|---|---|
| `OddsSnippet` | `GameId` | Best number per market and the book holding it; time since last move | Line movement (`odds`) |
| `HeadToHeadSnippet` | `GameId` | Last 5 meetings — date, score, closing line beside each | Head-to-head (`h2h`) |
| `FormSnippet` | `GameId` | Both sides' last 5 as W/L tags | Game (`game`) |
| `PlayerRecentSnippet` | `PlayerId` | Last 5 stat lines, one row each | Player (`player`) |
| `TeamStatsSnippet` | `GameId`, `TeamId` | That game's team stat line | Game (`game`) |

### `PlayerPanel` — new `player` window

The missing bottom of the drilldown chain. Full game log: one row per appearance with the
stat line, plus the same splits strip the Team window uses. `WindowShortcuts.OpenPlayer` is
repointed from the Players list to this. `Singleton = false` — comparing two players is
normal.

### `HeadToHeadPanel` — new `h2h` window

Full meeting history between two teams rather than the snippet's five, with the closing line
and result per meeting. Takes `GameId` and derives both teams from it, so it is reachable from
any game row without the caller resolving team ids.

## Panels changed

**`BoardPanel`** — its inline action block is replaced by `RowActions`. Actions gain `Odds`
and `H2H` alongside the existing More bets / Place wager / Form. `OpenAsync` moves into
`RowActions`; the panel keeps only its action list.

**`DashboardPanel` (Slate)** — gains `RowActions` with a scan-oriented set: `Odds`, `H2H`,
`Form`, and `Teams`. Deliberately not the Board's set — the Slate is for reading the day, not
shopping a price, so `Place wager` stays on the Board where a best number is on screen to
wager against.

`Teams` is the one window-only action: it has no snippet and opens both teams' windows
directly, because team data is a place to work rather than something to glance at. This is
the `Snippet = null` case `RowAction` allows for.

**`TeamPanel`** — rebuilt. The two static tables become two stacks of expandable rows:

- *Game rows* — one per game in the window, expanding to that game's team stat line and the
  betting result. Above them, a splits strip: All / Home / Away / Last 5 / Last 10 / vs
  opponent. Splits filter rows already in memory, so toggling is instant and costs no query.
- *Player rows* — each expanding to `PlayerRecentSnippet`, with the escape to the player
  window.

**`GamePanel`** — the roster tables become expandable rows carrying `PlayerRecentSnippet`.
The team-data block gains an `H2H` action.

## Data layer

### `GameLogService` — `src/LineOps.Data/CrossReference/GameLogService.cs`

Game-grain reads, kept separate from `MatchupCrossReference`. That class is 360 lines and
exists to aggregate windows into form; reading individual games is the opposite job, and
merging them would produce a file where neither purpose is legible.

```csharp
Task<IReadOnlyList<TeamGameLogRow>> TeamGameLogAsync(int teamId, TimeSpan window, CancellationToken ct);
Task<IReadOnlyList<PlayerGameRow>>  PlayerGameLogAsync(int playerId, int take, CancellationToken ct);
Task<IReadOnlyList<HeadToHeadRow>>  HeadToHeadAsync(int gameId, int take, CancellationToken ct);
```

`TeamGameLogRow` carries `GameId, StartsAt, Opponent, Home, ScoreFor, ScoreAgainst, Status,
Won, ClosingSpread, AtsResult, ClosingTotal, TotalResult`. The two result fields are
`EntryResult?` — null meaning *not gradable*, which is distinct from a push.

Like `MatchupCrossReference`, this is a lookup and not a copy: nothing is materialised into a
summary table, for the same reasons ADR 0003 and that class's own doc comment give — a stored
aggregate is a second version of the truth that goes stale silently.

### `GameLogSplits` — pure

Static filters over a loaded `IReadOnlyList<TeamGameLogRow>`: home, away, last N, versus one
opponent. No database, so the split rules are unit-testable directly and the UI can toggle
without a round trip.

### `Grading` kernel extraction — `src/LineOps.Core/Analytics/Grading.cs`

`Grade` currently takes a `JournalEntry`, so the ATS/total rules cannot be applied to a
`ClosingLine`. Extract the kernel:

```csharp
public static EntryResult? GradeOutcome(
    string market, string outcome, decimal? line,
    string homeTeamName, string awayTeamName, int homeScore, int awayScore);
```

`Grade(JournalEntry, …)` keeps the free-text guard and becomes a thin adapter over it. The
team game log grades a closing line through the same kernel. One rule set, two callers, no
second copy to drift.

The existing `GradingTests` must pass **unchanged** — that is what proves the extraction is
behaviour-preserving before anything new depends on it.

## Missing data

Closing lines exist only for games this system was running for. `OddsSnapshots` are deleted at
first pitch by design (ADR 0010) and only `ClosingLines` survive, so a head-to-head list
reaching back five meetings will mostly predate any stored line.

**The odds column ships now and renders `—` where no line was captured.** Rows are never
hidden for missing odds — that would silently understate a team's record, which is exactly the
failure ADR 0003 was written about. A blank and a zero mean different things and only one of
them is honest here.

Coverage is stated rather than inferred, mirroring the Board's existing
`@_priced of @_rows.Count priced` strip: the team log and head-to-head views carry a note
reading e.g. *12 of 30 games have a closing line*.

A later ESPN historical-odds backfill fills this column with no UI change — the column is
already there and already labelled.

## Error and empty states

Consistent with the existing panels:

- A team or player with no games in the window: `<p class="empty">` stating the window, not a
  zero row.
- A game with no closing line: `—` in the odds and result cells, row retained.
- Two teams that have never met: the head-to-head snippet says so rather than rendering an
  empty table.
- A snippet whose load throws: the snippet body shows the failure and the row stays open, so
  one bad lookup does not close the row or take down the panel.

## Testing

| Scope | Test | Kind |
|---|---|---|
| Grading extraction | Existing `GradingTests`, unchanged | Unit |
| ATS / total from closing lines | `GameLogServiceTests` — cover, push, no-line-blank | Integration (`PostgresFixture`) |
| Head-to-head | Ordering, `take` limit, teams that never met | Integration |
| Player game log | Ordering, stat-line parsing, cap | Integration |
| Splits | `GameLogSplitsTests` — home/away/last-N/vs-opponent | Unit, pure |

No component tests: the project has no Blazor test harness today and this spec does not
introduce one. UI is verified by running the app.

## Out of scope

- **ESPN odds ingestion.** Agreed separately: ESPN serves as a *fallback* line only when no
  real book has priced a fixture, never competing for best price and never feeding
  closing-line value. That reconciles the free-reference goal with ADR 0011's reasoning, and
  it is its own spec.
- Keyboard navigation of rows.
- Hover-revealed actions — click-to-expand only.
- Any change to ingestion, retention or the odds tier.
