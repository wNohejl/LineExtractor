# MLB and NFL seasons — research

Date: 2026-09-04 · Branch: `data/mlb-nfl-seasons` · Status: research, nothing implemented

## The ask, and how it is read here

> Only MLB and NFL. All 2026 MLB games and stats accounted for in the DB. Pull last year's NFL
> games and stats so we have a season-filtering infrastructure. ESPN should store closing odds
> for its NFL games, carrying over if possible. Research how we should share data for users.

Four concrete goals and one open phrase. "Share data for users" is read as *the data the desk
serves, scoped by sport and season* — there is no user, tenant or auth concept anywhere in
the platform today, so a second reading (multi-user access) is treated in §7 as a question
back rather than a design. Everything below is measured against the live database and the
code on this branch, and every claim about ESPN was checked against ESPN itself today.

## 1. What the platform can already do

- **ESPN is the stats port for every mapped league** (`EspnLeagues`: nine leagues including
  `football/nfl`). Scoreboard per date, box scores from a finished game's summary, parsed by
  a generic label/athlete walker. Verified today that an NFL summary yields twenty athlete
  groups with QB, rushing and receiving labels, so NFL needs no new parser.
- **ESPN closing lines already exist.** `EspnStatsAdapter.ParseClosingLines` reads the
  explicit `close` blocks (moneyline, spread, total, per side) from the summary's `pickcenter`,
  and `StatsIngestionService.PersistClosingLinesAsync` writes them to `ClosingLines`
  (source `espn`, book `DraftKings`). 2,040 MLB games already carry them, and
  `EspnClosingLineTests` pins the parsing.
- **Carry-over is already the rule.** `GameLogService.ClosingLinesForAsync` prefers a book
  market *per game* and falls back to the ESPN reference only for games no market covered;
  `SettlementService` falls back to any book; the board reads the scan tier, then
  `ClosingLines`. Nothing new is needed for ESPN closes to carry over: they fill exactly the
  games a market never saw.
- **Backfill walks days, checkpointed, newest-first, unmetered sources only** (ADR 0009). A
  backfilled day calls the box-score overload (`stats.IngestAsync(..., "stats:backfill",
  date)`), so stats *and* ESPN closes ride along with history.
- **Sports are gated by config alone**: `Ingestion:Sports` (currently `["mlb"]`) and
  `Backfill:Sports`. The `Sports.Enabled` column is honoured by nothing.

## 2. What the database holds today (measured 2026-09-04)

| Sport | Games | Stats coverage | ESPN closes | Notes |
|---|---|---|---|---|
| MLB 2026 | 2,117 (2,042 final, 3 "live", 27 postponed, 45 scheduled), 25 Mar → 6 Sep | 2,055 games, 66,727 lines, through 30 Aug; 1 final without stats | 2,040 games, through 29 Aug; 2 finals without a close | 152 checkpointed days 25 Mar → 23 Aug, 0 errors |
| NFL 2026 | 272 scheduled, 10 Sep → 10 Jan 2027 | none; **0 players** | none | the-odds-api scans running since 24 Aug (1,918 rows) |
| NFL 2025 | **nothing** | — | — | — |
| NBA 2026 | 163 final (playoffs, Apr–Jun) | 163 games | none | out of scope; residue |
| NHL 2026 | 173 final (playoffs, Apr–Jun) | 173 games | none | out of scope; residue |

Sources: `espn` (unmetered), `the-odds-api` (500 credits/month), `odds-api-io` (100/h), and
`balldontlie` (300/h) — which has a `Sources` row, an options block and a key in `.env`, and
**no adapter**: dead configuration. Database size: 44 MB.

## 3. Findings

**F1 — MLB 2026 is complete except for a hole the host's downtime punched.** No ingestion
runs exist for 31 Aug → 3 Sep. The consequences are layered: games for those dates are absent
from `Games` entirely (the slate pass never ran), 30 August has closes for 17 games but box
scores for 1, and three games have sat at `Live` since 30 Aug (ids 3013–3015).

**F2 — The results pass cannot heal an outage longer than three days.**
`DatesAwaitingResultsAsync` only considers games with `StartsAt >= cutoff - 3 days`. The host
came back on 4 Sep, by which time 30 Aug had aged out of the window, so those games are never
owed again. The self-healing that `RunResultsAsync`'s own comment promises stops one day short
of what happened. Backfill is unaffected (those days were never checkpointed), which is why it
is the remediation — but the window is a bug.

**F3 — NFL is a schedule with nothing behind it.** The 2026 slate and 32 teams are loaded, but
players enter only through box scores (ADR 0011) and none has been ingested. Last season does
not exist in the database at all.

**F4 — ESPN's historical NFL closing lines are partial, and the boundary is ESPN's.** Sampled
with the adapter's own identity (`.NET/10.0`; a browser identity is refused with 403):

| 2025 season date | Season type | `pickcenter` | Close blocks |
|---|---|---|---|
| 4 Sep (kickoff), 7 Sep, 5 Oct, 16 Nov | regular | `[]` — empty | 0 |
| 21 Dec | regular | DraftKings | 6 (ML/spread/total × 2 sides) |
| 10 Jan (wild card), 25 Jan (conf. champ.), 8 Feb (Super Bowl) | postseason | DraftKings | 6 |

A 2025 backfill will therefore give **every game its box score** and **roughly the last
three regular-season weeks plus the whole postseason their closing lines** — about 30% of
games. The first thirteen weeks have no closing line from any free source; The Odds API's
historical endpoint is paid tiers only. The game log already reports "N of M with a closing
line", so partial coverage is honest by construction. The boundary lies between 16 Nov and
21 Dec; one more sampling pass would pin the exact week.

**F5 — There is no season, only dates.** `Game` has `StartsAt` and nothing else; every reader
filters by a rolling window (`BoardService` floor/horizon, `GameLogService` since/now). ESPN
already states the answer per event — `season.year` and `season.type` — and the adapter reads
it only to *discard* preseason. A February playoff game is stamped `year: 2025`; the MLB Stats
API carries `gameType`. The raw material for a season column is flowing past and being dropped.

**F6 — Backfill cannot express two seasons.** `Backfill:Since` is one date for every sport.
"MLB from 25 March 2026" and "NFL from 4 September 2025" cannot both be true, and a rolling
`Days` covers a different stretch each run (the option's own doc says so).

**F7 — "Only MLB and NFL" is two config lines plus two queries.** `Ingestion:Sports` and
`Backfill:Sports` gate everything that ingests; `BoardPanel` and `PlayersPanel` list
`db.Sports` unfiltered, so NBA and NHL stay in the pickers until `Sport.Enabled` is honoured.

**F8 — The only per-user data in the schema is the journal.** Games, teams, players, stats
and closing lines are global reference data; `JournalEntries` (and the ops tables) are the
operator's. That is the shape you want if data is ever shared: one table needs an owner,
nothing else does.

## 4. Proposed design

### 4.1 Scope to MLB and NFL
- `Ingestion:Sports = ["mlb","nfl"]` and `Backfill:Sports` the same.
- Set `Enabled = false` on the `nba`/`nhl` rows and make the two pickers filter on it. One
  small change, and `Sport.Enabled` finally means something.
- NBA/NHL residue (336 games, ~10k stat rows): leave it, or purge with a script in the
  pattern of the existing `purge-*.sql`. Your call; nothing reads it once the pickers hide it.
- Remove the dead `balldontlie` configuration and `.env` key, or leave it inert. It costs
  nothing but confuses the Ops feed list.

### 4.2 A season column, not a season table
Add to `Game`: `SeasonYear int` and `SeasonType` (`Regular | Postseason`, with `Preseason`
and `Exhibition` reserved so the excluded kinds can be *named* if ever stored). Populate from
the provider at ingest — ESPN's per-event `season.year`/`season.type`, MLB's `gameType` — and
backfill existing rows in the migration with a per-sport rule (`SeasonCalendar`: MLB is the
calendar year; NFL is the year of the September start, so January and February belong to the
prior year). Index `(SportId, SeasonYear, StartsAt)`.

Why a column and not a `Seasons` table: the provider stamps every event, so the truth is on
the game; a table would be a second copy to keep aligned. `SeasonCalendar` (static, per sport)
carries what is *not* on the game — display labels and the start dates §4.3 needs. ESPN also
publishes `startDate`/`endDate` per league season (2025 NFL: 31 Jul 2025 → 12 Feb 2026);
worth reading into the calendar rather than hardcoding, but not on the critical path.

Filtering: a `Season` parameter beside `sportKey` in `BoardService`, `GameLogService` and
`MatchupCrossReference`; a season gate (`DeskSwitch`) on the Board, Team, Player and History
windows, defaulting to the current season. `GameLogService`'s "since" becomes the season's
start rather than a day count when a season is selected.

### 4.3 Per-sport backfill start
Replace the single `Backfill:Since` with `Backfill:Seasons` — a map of sport key to start
date (`mlb: 2026-03-25`, `nfl: 2025-09-04`) — or derive it from `SeasonCalendar` and keep
`Since` only as an override. Coverage arithmetic (`GetCoverageAsync`) becomes per sport, which
also removes the "447 days held of 292 wanted" class of confusion the code already comments on.

### 4.4 Load plans
- **MLB catch-up (now, no code change):** run the backfill. `Since` is already 25 Mar; it
  skips the 152 held days and walks 24 Aug → today newest-first, inserting the missing games,
  finalising the three stuck ones, and fetching box scores and closes. About 12 days and 180
  requests.
- **NFL 2025 (after §4.3):** walk 4 Sep 2025 → 12 Feb 2026 (ESPN's published season end).
  About 162 scoreboard days, most empty and checkpointed as such, and 285 game summaries. At
  the configured 600 ms pacing that is roughly five minutes. Expect 285 games, every box
  score, players created from them, and closes from late December onward (F4).
- **NFL 2026 (already running):** the-odds-api scans promote to closes at kickoff (ADR 0010)
  and ESPN closes arrive after each game. Full coverage from now on.

### 4.5 Fix the three-day results window (F2)
Owed days should be *any* non-final game older than `ResultsAfterStart`, bounded by the
season start (or a generous cap such as 45 days), not by three days. One condition and one
test that models a five-day outage.

### 4.6 Verification
The coverage table in §2 came from plain SQL over `Games`, `PlayerGameStats`, `ClosingLines`
and `BackfillCheckpoints` grouped by sport and year. Once `SeasonYear` exists it groups by
season instead, and it belongs in the History window as "expected / held / with stats / with
close" per season — the number that says whether "accounted for" is true.

## 5. Sequenced plan

1. Scope: config to `mlb`,`nfl`; `Sport.Enabled` honoured in pickers; disable nba/nhl.
   *(test: picker hides a disabled sport)*
2. Results-window fix (§4.5). *(test: a five-day outage is healed)*
3. Run the MLB catch-up backfill; confirm 0 finals without stats and 0 stuck Live.
4. `SeasonYear`/`SeasonType`, `SeasonCalendar`, migration. *(tests: a February NFL game maps
   to 2025; MLB gameType mapping; ESPN season-block mapping)*
5. Per-sport backfill start (§4.3) and per-sport coverage. *(test: two sports, two starts)*
6. Run the NFL 2025 backfill; verify against §4.4; pin the ESPN closing-line boundary week.
7. Season gate in the desk; readers take a season. *(render tests on the gate; service tests
   on bounds)*
8. History window coverage-per-season report (§4.6).

## 6. Costs and risks
- ESPN is unofficial and unmetered; the backfill is paced and the identity is deliberate
  (`.NET/10.0`; a browser identity is refused). Keep the pacing.
- ESPN's historical odds retention is theirs to change; the F4 boundary could move.
- `the-odds-api` credits are untouched by all of this: backfill refuses metered sources.
- No migration touches the `OddsSnapshots` partitions; the season column is on `Games` only.

## 8. What was done (2026-09-05)

Everything in §5 except the coverage report in the History window, in one pass:

- **Scope.** Both hosts poll `mlb` and `nfl`; `Sport.Enabled` is honoured by the pickers and
  NBA/NHL are disabled (rows left in place). The dead `balldontlie` configuration is untouched.
- **Results window** widened to a configurable `GamePasses:ResultsLookback` (45 days) with
  integration tests modelling a five-day outage. The Worker's `appsettings.json` also had a
  duplicate `"//"` comment key that stopped it starting at all — fixed.
- **MLB catch-up** ran through the Worker: 12 days walked, 141 games, 0 errors. Zero finals
  without stats, zero stuck Live games, one final in the whole season without a close.
- **NFL 2025** loaded the same way: 285 games (272 regular, 13 postseason), every one with a
  box score, 1,862 players, 18,387 stat lines, 0 failed days. ESPN closes for 108 games from
  25 November onward — the boundary is earlier than the December sample suggested (F4).
- **Season column.** `Game.SeasonYear` and `Game.SeasonType`, stamped by ESPN's per-event
  season block at ingest (`CanonicalGame` carries it; the resolver writes it and corrects it
  on re-sight), with `SeasonCalendar` as the rule where no stamp exists. The migration stamps
  existing rows by that rule. The NFL rule is the league's own — kickoff is the Thursday
  after Labor Day, the regular season ends on the Monday of week 18 — because a fixed day of
  the month misfiled week 18 of the 2026 season as playoffs on the first attempt.
- **Per-sport backfill starts** (`Backfill:Seasons`), so MLB from 25 March and NFL from the
  previous September walk in one run.
- **Season gate** on the Team, Player and Game windows, drawn from the seasons each actually
  has, hiding itself when there is only one; a season tag shows regardless. The Game window
  draws team and roster data from the game's season, or the one before for a preview.
- **Publishing the data.** `scripts/publish-data.ps1` streams a custom-format `pg_dump`
  (~3 MB) into `data/snapshots/` with a manifest; `scripts/restore-data.ps1` restores it on
  another machine. Both stream through `cmd` because the container's `/tmp` is a tmpfs that
  `docker cp` cannot reach. `CLAUDE.md` carries the workflow.

Tests: 362 in `LineOps.Tests`, 262 in `LineOps.Web.Tests`, all green.

## 7. Open questions
1. "Share data for users": the desk's own consumers (this document's reading), or a second
   user? If the latter, F8 makes it small — an owner on `JournalEntries` and an auth layer —
   but it is a different project.
2. Purge the NBA/NHL rows, or just hide them?
3. Is ~30% closing-line coverage for NFL 2025 acceptable, or should a paid historical source
   be priced?
4. Should the postseason be on by default in season filters, or its own position on the gate?
