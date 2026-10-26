# UFC fights — implementation plan

Spec: `docs/superpowers/specs/2026-10-26-ufc-fights-design.md`. Work lands on
`LineX_Development`, one commit per task, pushed after each.

## Task 1 — Core: fights as games

- `src/LineOps.Core/Entities/Fights.cs`: `Bout`, `BoutOutcome`, `FighterProfile`.
- `src/LineOps.Core/Analytics/FightSports.cs`: `IsFight`, `ElapsedRounds`, method short forms,
  result sentence, `FighterNames.Same` (accent-folded, surname + initial fallback).
- `Grading`: `GradeFight` — moneyline win/loss/push, NC void, rounds total.
- Tests: `FightGradingTests`, `FighterNamesTests`.

## Task 2 — Schema

- `LineOpsDbContext`: `Bouts` (PK `GameId`, FK cascade), `FighterProfiles` (PK `TeamId`).
- Migration `UfcFights`. `DatabaseInitializer` seeds `ufc`.

## Task 3 — ESPN adapter

- `EspnLeagues`: `ufc` → `mma/ufc`.
- `CanonicalBout` on `CanonicalGame`; `CanonicalTeamRef.Qualifier` for a name two fighters share.
- `EspnStatsAdapter`: for a fight sport, scoreboard → fight center per card → bouts, fighters,
  fight stat lines; closes from the core odds endpoint (`homeAthleteOdds`).
- `IFighterSource` + ESPN implementation: athlete, athlete stats, career statistics.
- Tests: `EspnFightCenterTests` (UFC 300 fixture), `EspnFighterTests`, athlete-odds case.

## Task 4 — Resolution and persistence

- `EntityResolver`: a team match never takes a row this source knows under another id; a name
  two fighters share is qualified; odds sources attach to an existing bout in either corner and
  never create one; outcome names canonicalised to the bout's fighters.
- `StatsIngestionService`: upsert `Bout` rows.
- `FighterProfileService` + job `espn:fighters`; results sweep and backfill call it.
- `SettlementService`: fight grading, NC void.
- Odds adapters: `ufc` sport keys.
- Tests (Postgres): bout persistence and re-walk, odds bout matching both corners, NC void.

## Task 5 — Configuration

- Both hosts: `Sports` and `Backfill:Sports` add `ufc`; `Backfill:Seasons:ufc` = 2024-04-13;
  `MarketsBySport:ufc` = moneyline, total. Worker comment keys stay unique.

## Task 6 — Desk

- `FightService` (Data/CrossReference): tale of the tape, fighter view.
- `TaleOfTheTape.razor`, `FighterView.razor`; `GamePanel` fight tabs; `BoardForm`/`FormSnippet`
  for fights; `TeamPanel` delegates to `FighterView`; `BoardPanel` fight chips and result;
  `Scoreboard` result line.
- bUnit tests for the tape's rows and the board's fight result.

## Task 7 — Backfill and data

- Run the walk from 2024-04-13 for `ufc`, then the fighter pass. Check: events held vs ESPN's
  calendar, finals without stats, stuck Live, closes.
- `scripts/publish-data.ps1 -Commit`.

## Task 8 — Docs

- ADR 0021 (a fight is a game between two fighters); CLAUDE.md scope; README; runbook.
