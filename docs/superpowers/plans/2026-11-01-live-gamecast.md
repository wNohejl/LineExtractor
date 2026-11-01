# Live gamecast — implementation plan

**Spec:** `docs/superpowers/specs/2026-10-24-live-gamecast-research.md` (§5, §6.1–6.4, §8 steps 1–3).
**Branch:** a short-lived branch merged into `LineX_Development` and deleted (CLAUDE.md).
**Commits:** one per spec step — Step 1 = Tasks 1–4, Step 2 = Tasks 5–7, Step 3 = Tasks 8–9.

## Global constraints

- Read `CLAUDE.md` first. Never add a `Co-Authored-By` trailer; never run `git push`.
- Every ESPN field is optional (spec §7): a missing `situation`, linescore or play list parses to
  null/empty, never throws.
- Tests first (superpowers:test-driven-development) for every parser and service change. Parser
  tests read fixtures under `tests/LineOps.Tests/Fixtures`; persistence tests use
  `PostgresFixture` (Testcontainers — Docker is running).
- Fixtures stay small (a few KB, hand-trimmed from real captures). Real captures taken
  1 Nov 2026 during live NFL games are in
  `C:\Users\waden\AppData\Local\Temp\claude\C--Deploy-LineOps--claude-worktrees-live-gamecast-f20911\3229fa10-ab5b-45b0-b860-73e796e4f339\scratchpad\espn\`:
  `nfl-scoreboard.json` (3 live, finals, pre), `nfl-summary-live-401873032.json` (LAC @ LAR,
  Q2, drives.current + previous), `nfl-summary-final-401873030.json` (MIN @ DET final),
  `nfl-probs-401873032.json` (core `probabilities?limit=5`, carries `spreadCoverProbHome`,
  `totalOverProb`; the summary's `winprobability[]` carries only `homeWinPercentage`,
  `tiePercentage`, `playId`). No MLB game is live (season over): MLB live shapes are
  hand-written from ESPN's field names (`situation.balls`, `strikes`, `outs`, `onFirst`,
  `onSecond`, `onThird`, `batter`, `pitcher`).
- Build: `dotnet build src/LineOps.Web`, `dotnet build tests/LineOps.Tests`,
  `dotnet build tests/LineOps.Web.Tests` clean (no new warnings); `dotnet test` on the test
  projects you touched green. When other agents may be building in the same tree, build to an
  in-repo scratch path, e.g. `-p:BaseOutputPath=tests/LineOps.Tests/bin/<tag>/`, and delete it
  after (never `%TEMP%`: `Repo.Root()` walks up for `LineOps.slnx`).
- Never `Stop-Process` LineOps.Web. A web host you start runs on its own port with
  `--Ingestion:HostScheduler=false --Reliability:HostEvaluator=false` unless told otherwise.
- `src/LineOps.Web/wwwroot/css/lineops.css` (or wherever the desk stylesheet is) is single-owner:
  new styles go in a component-scoped `.razor.css` beside the component.
- Windows follow ADR 0020 (`PanelTitle`, `PanelToolbar`, `PanelSection`), ADR 0007/0013 (every
  window is a panel the window manager hosts), ADR 0016 and the `apple-hig-components` skill.
- Jobs follow ADR 0012: named, triggered by state, listed in the catalogue the Pull menu reads.
- One EF migration per step that changes the schema, generated with `dotnet ef migrations add`
  (the model snapshot must match).
- Comment density, naming and voice match the surrounding code (long `<summary>` explaining
  *why*, British spelling as the code uses it).

## Interfaces fixed by this plan

### Addressed change notices (Task 2)

- A notice payload is a comma list of tokens; a token is `topic` or `topic:id`
  (`games:4123`). The notifier emits `games:{GameId}` for every changed `Game` /
  `PlayerGameStat` / `StatSnapshot` row it can address (`Game.Id`, or the row's `GameId`), and
  `odds:{GameId}` for `OddsSnapshot`/`ClosingLine`. Rows with no id (or an entity type with no
  game) emit the bare topic. More than 50 ids for one topic in a save → the bare topic instead
  (pg_notify's payload is capped at 8000 bytes, and a backfill touches thousands).
- Matching (in `DeskPanelBase`): a panel watching topic `T` with no id watch hears `T` and every
  `T:x`. A panel that also declares ids for `T` (`WatchesIds` — `IReadOnlyDictionary<string,
  IReadOnlySet<string>>` or an equivalent virtual returning the ids per topic; ids as strings so
  the Desk stays app-neutral) hears bare `T` and `T:id` for its ids only. `DataTopics.All`
  (fallback poll, reconnect) is bare topics, so it still reaches everyone.
- `DataTopics.Plays = "plays"` is added in Task 6, addressed as `plays:{GameId}`.

### Live state on `Game` (Task 1)

Nullable columns on `Game`: `Period int?`, `DisplayClock string?` ("4:12"), `ClockSeconds
int?`, `StatusDetail string?` (ESPN `status.type.shortDetail`: "Q3 4:12", "Halftime",
"Final/OT"), `PossessionTeamId int?` (our `Team.Id`, resolved from ESPN's team id),
`Down int?`, `Distance int?`, `YardLine int?` (ESPN's value as given; document its frame in the
XML comment after checking it against `possessionText` in the capture), `DownDistanceText
string?` ("2nd & 6 at NE 34"), `PossessionText string?`, `IsRedZone bool?`, `HomeTimeouts
int?`, `AwayTimeouts int?`, `LastPlay string?`, `Linescore` jsonb (`{"home":[10,7],"away":[3,0]}`
— a small typed class mapped with `ToJson()` or a jsonb string, whichever `PlayerGameStat.StatLine`'s
neighbours make idiomatic), `Situation` jsonb string (sport-specific: MLB `balls`, `strikes`,
`outs`, `onFirst`, `onSecond`, `onThird`, `batter`, `pitcher`), `LiveUpdatedAt
DateTimeOffset?`. Situation fields clear to null once the game is no longer in play.

`CanonicalGame` gains one optional `CanonicalLive? Live = null` parameter (a new record in
`ISourceAdapter.cs`) carrying the same fields with the possession as ESPN's team id string.

### Play store (Tasks 5–6) — exact file, created verbatim by Task 5 *and* Task 6

`src/LineOps.Core/Contracts/CanonicalGamecast.cs`:

```csharp
namespace LineOps.Core.Contracts;

/// <summary>
/// One game's play-by-play as the provider states it at one moment: the drives, every play in
/// them, the scoring plays and the per-play probabilities. Plays carry stable provider ids and
/// sequence numbers, which is what lets the store upsert instead of reload (spec §6.2).
/// </summary>
public record CanonicalGamecast(
    string SourceGameId,
    string? State,
    IReadOnlyList<CanonicalDrive> Drives,
    IReadOnlyList<CanonicalPlay> Plays);

public record CanonicalDrive(
    string SourceDriveId,
    int Sequence,
    string? SourceTeamId,
    string? Description,
    string? Result,
    int? Yards,
    int? OffensivePlays,
    string? TimeElapsed,
    bool IsScore,
    bool IsCurrent,
    int? StartPeriod,
    string? StartClock,
    int? StartYardLine,
    int? EndPeriod,
    string? EndClock,
    int? EndYardLine);

public record CanonicalPlaySituation(
    int? Down,
    int? Distance,
    int? YardLine,
    int? YardsToEndzone,
    string? DownDistanceText,
    string? PossessionText,
    string? SourceTeamId);

public record CanonicalPlay(
    string SourcePlayId,
    long Sequence,
    string? SourceDriveId,
    int? Period,
    string? Clock,
    int? ClockSeconds,
    string? TypeId,
    string? Type,
    string? Text,
    int? HomeScore,
    int? AwayScore,
    bool IsScoring,
    bool IsTurnover,
    bool IsPenalty,
    int? Yardage,
    string? SourceTeamId,
    CanonicalPlaySituation? Start,
    CanonicalPlaySituation? End,
    decimal? HomeWinPct,
    decimal? SpreadCoverPctHome,
    decimal? TotalOverPct,
    DateTimeOffset? Wallclock,
    DateTimeOffset? Modified);
```

Entities (Task 6), in `src/LineOps.Core/Entities/Gamecast.cs`, tables `game_play` and
`game_drive` following the context's naming convention:

- `GamePlay`: `Id long`, `GameId int` (FK, cascade), `SourceId int` (FK to `Source`, as
  `PlayerGameStat` does), `SourcePlayId string`, `Sequence long`, `DriveSeq int?`, `Period
  int?`, `Clock string?`, `ClockSeconds int?`, `TypeId string?`, `Type string?`, `Text string?`,
  `HomeScore int?`, `AwayScore int?`, `IsScoring bool`, `IsTurnover bool`, `IsPenalty bool`,
  `Yardage int?`, `TeamId int?`, `StartSituation` jsonb string, `EndSituation` jsonb string,
  `HomeWinPct decimal?`, `SpreadCoverPctHome decimal?`, `TotalOverPct decimal?`, `Wallclock
  DateTimeOffset?`, `Modified DateTimeOffset?`, `UpdatedAt DateTimeOffset`. Unique
  `(GameId, SourceId, SourcePlayId)`; index `(GameId, Sequence)`.
- `GameDrive`: `Id long`, `GameId int`, `SourceId int`, `SourceDriveId string`, `Sequence int`,
  `TeamId int?`, `Description`, `Result`, `Yards`, `OffensivePlays`, `TimeElapsed`, `IsScore`,
  `IsCurrent`, start/end period, clock, yard line. Unique `(GameId, SourceId, SourceDriveId)`.

---

## Step 1 — the scoreboard keeps up (spec §5)

### Task 1: Parse and keep the live state

**Files:** `src/LineOps.Core/Contracts/ISourceAdapter.cs`, `src/LineOps.Core/Entities/Reference.cs`
(`Game`), the `Game` configuration in `src/LineOps.Data/LineOpsDbContext.cs` (or its
configuration file), one migration `LiveGameState` in `src/LineOps.Data/Migrations`,
`src/LineOps.Ingestion/Adapters/EspnStatsAdapter.cs` (`ParseScoreboard`),
`src/LineOps.Ingestion/Services/EntityResolver.cs` (`ResolveGameAsync`),
`tests/LineOps.Tests/Fixtures/espn.scoreboard.json`, tests under `tests/LineOps.Tests`.
Do **not** touch `IngestionJobs`, `IngestionScheduler`, `IngestionOptions`, `ChangeNotifier`,
anything in `LineOps.Desk` or `LineOps.Web`.

1. Fixture: add to `espn.scoreboard.json` one live NFL event trimmed from the capture (keep
   `status` with `displayClock`, `period`, `type.{state,detail,shortDetail}`;
   `competitors[].linescores`; `situation` with `down`, `distance`, `yardLine`, `possession`,
   `isRedZone`, `homeTimeouts`, `awayTimeouts`, `downDistanceText`, `possessionText`,
   `lastPlay.text`) and one hand-written live MLB event (or a separate
   `espn.mlb.scoreboard.live.json` if the existing fixture is NFL-only) with `situation.balls`,
   `strikes`, `outs`, `onFirst/onSecond/onThird`, `batter.athlete.displayName`,
   `pitcher.athlete.displayName`, `lastPlay.text`. Existing tests must still pass unchanged.
2. Tests (red first): the live NFL event parses to a `CanonicalLive` with every field above;
   the final event has `Live` with period/clock/linescore but no situation; a scheduled event's
   situation is null; the MLB event's balls/strikes/outs/runners land in `Situation` json and
   `LastPlay`; a payload with `situation` absent or malformed parses without throwing.
3. `CanonicalLive` record and `CanonicalGame.Live`; `Game` columns as in *Interfaces*; one
   migration.
4. `ResolveGameAsync` writes the live fields **change-only**, like status and score today: only
   assign a property when its value differs, so an unchanged poll saves nothing and fires no
   notice. Resolve possession from ESPN's team id to our `Team.Id` the way the resolver already
   matches teams (home or away of this game — never create a team for it). Clear the situation
   fields when the canonical game is not in play. Persistence test on `PostgresFixture`: a
   second resolve of the same canonical game reports no changes / `SaveChanges` writes 0 rows;
   a moved clock writes one.

### Task 2: Addressed change notices

**Files:** `src/LineOps.Data/Changes/ChangeNotifier.cs` (`DataTopics` too),
`src/LineOps.Desk/Windowing/DeskPanelBase.cs`, `src/LineOps.Desk/Windowing/DeskSignals.cs` only if
needed, `src/LineOps.Web/Services/DataChangeListener.cs` only if needed, tests in
`tests/LineOps.Tests` and `tests/LineOps.Desk.Tests` (or wherever DeskPanelBase is tested).
Do **not** touch entities, ingestion, or any panel.

Implement the *Addressed change notices* interface exactly. Tests: notifier payload for a save
touching games 1 and 2 is `games:1,games:2`; a save touching 51 games is `games`; an entity with
no game id gives its bare topic; a panel with no ids hears `games:7`; a panel watching ids
`{7}` hears `games:7` and `games` but not `games:8`; `DataTopics.All` reaches both. Update the
XML docs that describe the payload (`DataTopics`, `ChangeNotifier`, `DeskSignals` summary).

### Task 3: The live pass is its own job

**Files:** `src/LineOps.Ingestion/Services/IngestionJobs.cs`,
`src/LineOps.Ingestion/Services/IngestionScheduler.cs`,
`src/LineOps.Ingestion/Configuration/IngestionOptions.cs`, the job catalogue the Pull menu reads
(find it from `espn:slate`'s registration), `appsettings*.json` of `LineOps.Worker` and
`LineOps.Web` if they set `LivePoll`/`TickInterval` (comment keys stay unique: `"//1"`, `"//2"`),
tests. May call but not change `EntityResolver`'s public surface beyond an optional parameter.

1. `espn:live`: for each league-day with a game in play, fetch the scoreboard (same adapter
   call `RunSlateAsync` uses) and resolve **only** events whose canonical status is live, or
   final/postponed where our row is still `Live` (just-final). Skip team-identity refresh and
   the MLB spine sync. `espn:slate` keeps everything else and no longer runs as the live pass.
2. `GamePassOptions.LivePoll` default **15 s**. The scheduler ticks at `LivePoll` while any game
   is in play (the `LivePollIsDueAsync` predicate) and at `TickInterval` otherwise — e.g. the
   delay before the next tick is `min(TickInterval, LivePoll)` while live. `RequestDelay` still
   applies to every request.
3. Tests: the live job given a scoreboard with one live, one just-final, one scheduled and one
   long-final event resolves exactly the first two; the scheduler's next delay is 15 s with a
   live game and `TickInterval` without; the catalogue lists `espn:live`.

### Task 4: The Game window reloads its row and shows the clock

**Files:** `src/LineOps.Web/Components/Panels/GamePanel.razor` (and `.razor.cs` if any), the
`Scoreline` / `StatusLabel` helpers (find them), `PanelBase.cs` in `LineOps.Web` only for a
shared situation-line helper, `src/LineOps.Data/CrossReference/BoardService.cs` only if the Board
needs the bare-topic behaviour kept explicitly, tests in `tests/LineOps.Web.Tests`.

1. `GamePanel` watches `games` with ids `{GameId}` (and `odds` with `{GameId}`), `journal` bare.
   On a `games:{id}` notice it reloads **the row only** (one query) and re-renders the title;
   bets, form, rosters and box score reload only on `journal`, or on `games` when the status
   flipped to Final. Keep a way to know which topic arrived (extend `RefreshAsync` with the
   tokens heard, or an overload — coordinate with Task 2's `DeskPanelBase` API).
2. Title while live: score, then period/clock (`StatusDetail` or "Q3 4:12") as a tag, and the
   situation line as `Detail`: "CHI ball, 2nd & 6 at NE 34 · Last: D.Maye pass incomplete…"
   (MLB: "Top 7 · 2 out · 1-2 · runners on 1st, 3rd"). Nothing drawn when the fields are null.
   `Report()` the same short status to the rail chip.
3. bUnit/Web tests: a `games:{other}` notice does not reload; `games:{id}` reloads the row
   only; the title renders clock and situation from a live `Game`.

## Step 2 — the play store and the feed (spec §6.2–6.3)

### Task 5: Parse ESPN's summary into a gamecast

**Files:** `src/LineOps.Core/Contracts/CanonicalGamecast.cs` (verbatim from *Interfaces*),
a new partial `src/LineOps.Ingestion/Adapters/EspnStatsAdapter.Gamecast.cs` (a static
`ParseGamecast(JsonDocument/stream, ...)` plus an adapter method
`FetchGamecastAsync(sportKey, sourceGameId, ct)` and
`FetchProbabilitiesAsync(...)` for the core `probabilities?limit=1000` resource, through the
adapter's existing HTTP client, URL builder and `RequestDelay`), fixtures
`tests/LineOps.Tests/Fixtures/espn.summary.live.json` and `espn.probabilities.json` trimmed from
the captures (2 previous drives with 2–3 plays each, `drives.current` with 2 plays, 1–2
`scoringPlays`, matching `winprobability` rows, `header.competitions[0].status`), tests.
Do not touch entities or the DbContext.

Tests: drives in order with `IsCurrent` on the current one; plays carry ids, sequence, period,
clock + seconds, scores, flags, yardage, start/end situations, their drive id; win % paired by
`playId`; probabilities parse spread-cover and total-over keyed by play id (from the `play.$ref`
tail) and merge onto plays; a summary with no `drives` (the existing `espn.summary.json`) parses
to empty lists.

### Task 6: The play and drive tables

**Files:** `src/LineOps.Core/Contracts/CanonicalGamecast.cs` (verbatim — identical file to
Task 5's), `src/LineOps.Core/Entities/Gamecast.cs`, `LineOpsDbContext` (DbSets, configuration),
one migration `GamePlays`, `DataTopics` (`Plays`, `For` → `plays`, addressed `plays:{GameId}`
by Task 2's rules), a writer `src/LineOps.Ingestion/Services/GamecastWriter.cs` that upserts a
`CanonicalGamecast` for a known `Game.Id` + `Source.Id` (insert new, update modified — compare
`Modified` and the fields, as `PlayerGameStat`'s writer does — never delete), tests on
`PostgresFixture`. Do not touch the adapter.

Tests: first write inserts N plays and drives; the same gamecast again writes 0 rows; a play
with a new `Modified` and text updates in place; the notifier emits `plays:{id}`; `DriveSeq`
links plays to drives.

### Task 7: GamecastFeed follows games in play

**Files:** `src/LineOps.Ingestion/Services/GamecastFeed.cs` (a `BackgroundService`),
`IngestionOptions` (a `Gamecast` section: `Enabled` true, `Poll` 15 s, `ProbabilityPoll` 60 s,
`MaxFollowedGames` 4, `Sports` ["NFL"]), registration wherever the scheduler is registered so it
runs only where `HostScheduler` is on (Worker, or web host when it hosts the scheduler — never
both), appsettings comment keys unique, tests.

Loop: every `Poll`, find games in play (same predicate as `LivePollIsDueAsync`) for the enabled
sports, ordered by start, capped at `MaxFollowedGames` (log once when the cap bites); for each,
fetch the summary, merge probabilities every `ProbabilityPoll`, write through `GamecastWriter`
in a fresh scope/context. When a followed game goes final: one more summary write, run the
existing box-score parse/write for that game once (the path `espn:results` uses), and stop
following it. A failure on one game logs and moves on; it never ends the loop. Tests with a fake
adapter and `PostgresFixture` (or the existing job-test pattern): follows only live games, caps,
stops after final with one box-score write. Worker host must start.

## Step 3 — the Gamecast window (spec §6.1, §6.4)

### Task 8: GamecastService

**Files:** `src/LineOps.Data/Gamecast/GamecastService.cs` (+ registration beside
`BoardService`'s), tests on `PostgresFixture`.

Reads: `HeaderAsync(gameId)` (the `Game` with teams and live fields, the pre-game close from
`ClosingLine` — spread and total — and the game's sport); `PlaysSinceAsync(gameId, long
afterSequence)` ordered by sequence; `DrivesAsync(gameId)` with play counts; `ProbabilitySeries
(gameId)` (sequence, home win, spread cover, total over); `ScoringPlaysAsync(gameId)`. Read
models are small records, no entities leak to the window. `AsNoTracking`.

### Task 9: The Gamecast window

**Files:** `src/LineOps.Web/Components/Panels/GamecastPanel.razor` (+ `.razor.css`),
`src/LineOps.Web/Components/Snippets/FieldStrip.razor` (+ `.razor.css`), a JS module
`src/LineOps.Web/wwwroot/js/gameclock.js` written in the style of
`src/LineOps.Desk/wwwroot/js/windowing.js` (`startClock(el, seconds, running)`), `WindowCatalog`,
`WindowShortcuts`, `GamePanel.razor` (a Gamecast key beside Movement and Head to head), the
Board row actions (a Gamecast action while the game is live), tests in `tests/LineOps.Web.Tests`.

- Title: matchup and score; clock and period as tags; situation as `Detail`.
- Field strip: 100-yard SVG/CSS bar — ball spot, line of gain, possession arrow, red-zone tint —
  from `YardLine`, `Distance`, `PossessionTeamId`. NFL only; absent fields draw nothing.
- Drive log (`PanelSection`): current drive first, newest play first (clock, down & distance,
  text, yardage, a tag for score / turnover / penalty); previous drives collapsed to the result
  line ("7 plays, 62 yds, 3:48 · TD"); one open at a time via `RowActions`; `@key` per row;
  `<Virtualize>` past 150 rows.
- Win probability (`PanelSection`): `DeskChart` line over sequence, spread-cover and total-over
  as toggleable series, the pre-game close as a reference.
- Scoring summary; live box score reusing the Game window's box-score grid.
- Watches `games` with `{id}` (title, field strip) and `plays` with `{id}`: on `plays:{id}`
  fetch `PlaysSinceAsync(last)` and append — no full reload. `Report(PulseState.Active,
  "Q3 4:12 · CHI ball")` on each append.
- No in-play odds section. No MLB strip. Empty states name why (no plays yet; not followed).
