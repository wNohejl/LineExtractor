# ADR 0010 — Odds are scans until first pitch, then one closing line

**Status:** Accepted

**Amends:** [ADR 0001](0001-append-only-partitioned-odds.md), which kept every odds observation
for ever. The append-only, write-on-change rule still holds *within* the scan tier; what
changes is how long that tier lives.

## Context

ADR 0001 kept every price move so that closing-line value would always be answerable. It
worked, and the cost model was "storage tracks market activity rather than poll frequency",
which is true and still not the same as bounded. A monthly partition of a single day's demo
slate reached 1.8 MB from 16 games and two books; a real season across four sports, polled
through the day, grows without a ceiling and never stops.

The thing that made this worth revisiting is that almost none of it is ever read. Pre-game
movement is interesting *while the game is ahead of you* — it is the live market, and you look
at it to decide something. Once the game starts, the only question anyone asks of that history
is "what was the number at kick-off", which is one row per book, market and outcome. The
platform was paying to keep a stream in order to answer a question about its last element.

## Decision

Odds live in two tiers with different lifetimes.

**`OddsSnapshots` is a scan tier.** It holds the live market for games that have not started.
It is still append-only and still written on change only, so movement charts work exactly as
before for the games where movement is worth looking at. It is working state, and it is
deleted.

**`ClosingLines` is the permanent record.** One row per game, book, market and outcome,
promoted once the game has started: the newest scan taken *at or before* the start time. This
is the observation everything downstream actually needs.

Two passes, and the order is the safety property:

1. **Promote** every started game with no close on record.
2. **Prune** scans for games whose close is now on record, plus everything captured before
   `HistoryFloor` (2026-07-25, the date the policy took effect).

Nothing is deleted until the thing that replaces it exists. A crash between the passes leaves
scans that the next run prunes, so the sequence is safe to interrupt and safe to run at any
time. It cannot lose a close.

**Promotion waits ten minutes past the start.** A book still being scanned as the game begins
would otherwise have a line a few seconds stale promoted over the one that lands moments
later. The close is only ever read from scans at or before the start either way, so the delay
changes when the decision is made, not what counts.

**A game that has closed is no longer scanned.** `OddsIngestionService` drops payload rows for
games with a close on record. Those prices are in-play, which this product does not price
against, and writing them would mean a row, a WAL record and a delete to store nothing.

### Settlement reads the close, not the stream

`SettlementService` used to search the snapshot table for the newest row before kick-off. That
worked only while every scan was kept, and would have broken silently once pruning began — an
entry settled a week late would find nothing and lose its CLV without an error. It now reads
`ClosingLines`, chosen by the same rule and never pruned.

Because promotion and settlement are separate background passes, settlement can arrive first.
So CLV is also retried for entries that were already graded without one; the main loop only
visits *pending* entries, and without the retry a game that finished inside one retention
interval would keep a null close for ever.

`ClosingLines` is not partitioned, so unlike `OddsSnapshot` it *could* be a foreign-key target
— the constraint that forced [ADR 0002](0002-clv-reference-is-not-a-foreign-key.md) does not
apply. The reference is still left unenforced, because `JournalEntry.ClosingPrice` is what CLV
actually reads and an FK would tie a settled entry's lifetime to a table it no longer needs.

## Consequences

- **Historical odds are gone and are not coming back.** Line movement for a game that has
  already started cannot be reconstructed. This is the intended trade: the number that mattered
  was kept, the stream that produced it was not. Anything wanting pre-game movement must look
  while the game is still ahead.
- Storage per game goes from "one row per price move, for ever" to a bounded handful — for a
  15-game MLB slate at two books and three markets, on the order of 180 permanent rows a day.
- Empty past partitions are dropped rather than left behind. `DELETE` only frees pages to a
  later vacuum; a partition with nothing in it can be dropped as a metadata operation, which
  returns the space immediately.
- `PerformanceAnalytics.ComputeClv` now takes a price rather than an `OddsSnapshot`. It only
  ever read one property, and requiring a row from a table that gets deleted was backwards. A
  test was already fabricating a snapshot out of `ClosingPrice` to call it, which was the tell.
- The scan tier is still partitioned monthly even though it now spans days. That is deliberate
  overshoot: partitioning is what makes dropping cheap, and the shape costs nothing while
  giving room to lengthen retention later without a schema change.

## What running it found

The configuration binder **appends** to an array property that already has a non-empty default
rather than replacing it. `Backfill:Sources: ["espn"]` in config plus `["espn"]` as the code
default yielded `["espn", "espn"]`, which walked every day twice and reported a doubled target.
Config values are de-duplicated at the point of use; the same would happen to anyone listing a
source twice by hand.

Separately, and more embarrassingly: launching the built DLL from the repository root rather
than the project directory puts ASP.NET's content root somewhere with no `appsettings.json`, so
the app runs entirely on code defaults while looking completely healthy — the connection string
came from user-secrets, so even the database worked. Verification runs must confirm the content
root, or they are testing the defaults rather than the configuration.
