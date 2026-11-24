# ADR 0026 — The Odds API is the default source; ESPN is the opt-in extra

**Status:** Accepted, 2026-11-20, and merged into `LineX_Development`. Not yet verified against the live API (Consequences).

**Builds on:** [ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md) (one stats
port at a time, prices from a book market), [ADR 0014](0014-a-real-feed-a-credit-budget-and-what-counts-as-a-game.md)
(the credit budget and its pacing), [ADR 0021](0021-a-fight-is-a-game-between-two-fighters.md) (a fight
is attached only to a bout ESPN holds) and [ADR 0024](0024-data-tiered-by-licence.md), whose amendment's
"spine a visitor still sees" is the design built here.

## Context

ESPN became opt-in on 2026-11-20: its terms prohibit automated access, so a fresh clone calls nothing
of it until its operator has read why and turned it on. That left a clone with no schedule and no
scores at all — the board empty however good the key. The owner's decision: **the default is The
Odds API, on each user's own free key (500 credits a month).**

The provider's v4 guide (<https://the-odds-api.com/liveapi/guides/v4/>, read 2026-11-20) says:

- `GET /v4/sports/{sport}/events` — `id`, `sport_key`, `sport_title`, `commence_time`, `home_team`,
  `away_team`; "does not count against the usage quota".
- `GET /v4/sports/{sport}/scores` — the same plus `completed`, `scores` (`name`, `score` as a string)
  and `last_update`; `daysFrom` 1 to 3 returns completed games; "the usage quota cost is 2 if the
  daysFrom parameter is specified … otherwise 1"; live scores update about every 30 seconds; the
  scores id matches the odds id.
- `/odds` costs one credit per market per region; `x-requests-last` and `x-requests-remaining` come on
  every call. The guide does not list which sports have scores (it links a coverage page); ADR 0024's
  amendment read that page on 2026-11-15: MLB, NBA, NFL, and none for MMA.

## Decision

**A second stats source, behind the same port.** `TheOddsApiScheduleAdapter` implements
`IStatsSource` (fixtures from `/events`; box scores and rosters return nothing and ask nothing) and a
new `IScoreSource` (final scores from `/scores?daysFrom=3`). It shares the odds adapter's key handling
(the desk's runtime key first, then configuration), league keys and credit headers through
`TheOddsApiKey`/`TheOddsApiWire`. It is registered only while ESPN is off — by configuration's key in
`AddXLinesIngestion`, or unconditionally by `AddRuntimeOddsKey` on a desk, where it says for itself
whether a key is held (`IStatsSource.IsConfigured`, which `SourceRegistry` now filters on as it does for
odds sources). ESPN on: ESPN is the stats port exactly as before and this source is not registered at
all. ESPN off with a key: this is the stats port. ESPN off, no key: nothing, and the desk asks for a
key. `SourceRegistry.Port` names the three states for the desk's sentences.

**Its own source row, one account.** The schedule writes under a new row, `the-odds-api-schedule`
(Kind `Stats`, Licence `Displayable`, the same `MonthlyCreditBudget`), seeded *off* so a desk on ESPN
never lists it; `ReferenceReconciler` turns it on where it is registered. A row of its own because the
reliability layer reads freshness, success and volume per source — a fixtures pass every half hour
under the prices row would make an unpulled lines feed look fresh — and because `EntityResolver`
believes a stats source about start times and a book not. One account, because it is one key:
`SourceAccounts` names the pair, `MonthlyCredits` counts spend and the provider's remainder across both
rows (so the guard, the planner and Ops all see one month), a budget alert is raised on the prices row
only, and the resolver recognises a game the other row created by the shared event id — exactly, so a
doubleheader's two games stay two. No migration: the row is seeded by key on start, as every source is.

**Two jobs, two prices, never bundled.**

| Job | Calls | Cost | Runs |
|---|---|---|---|
| `odds:fixtures` (Today) | `/events` per league, today to eight days on | free | on request, and on the schedule: the first tick, every 6 h, and every 30 min while a game starts within 3 h |
| `odds:scores:{league}` (Costs credits) | `/scores?daysFrom=3` | 2 credits | on request, with its price and what is left stated before the press |
| `odds:scores` (scheduler) | the same, per league due | 2 credits a league | only when scanning is **Automatic**: once per league per league-day, after none of its games is still being played, only while the month affords it above the reserve |

Only a completed game with both scores is written (`final`); an in-play score is not, because the next
call may be a day away. Games that started more than three days ago without a final are counted and
named ("cannot get one from The Odds API") and never asked for. The fixtures job skips the guard (it
is free); the scores job goes through `CreditBudgetGuard` like every metered run. `LinePollPlanner`
sets aside what automatic scores will spend to the month's end before pacing lines, and Ops states both.

**Every metered press says so first.** `CreditAllowance` answers for the account: a pull the guard
would refuse is shown unavailable with the reason; one over 3 credits, or one that would leave less
than the 100-credit reserve, asks inline (`DeskJob.Confirm`, drawn by `DeskJobMenu` beside the key);
metered entries sit together under "Costs credits", say "2 credits · 412 left" where the provider has
reported the remainder, and free ones say "free". Tooltips explain what each calls and that credits
reset on the 1st; the price itself is always visible text.

**Seasons, names, scope.** The provider states no season: the resolver stamps a new game from
`SeasonCalendar`. It gives full names only: `TheOddsApiTeams` is the desk's own table of MLB, NBA and
NFL clubs (ESPN's abbreviations, and other names — "LA Clippers", "Athletics"), sent only by this
adapter as `CanonicalTeamRef.KnownAs`/`DeskAbbrev`; the resolver tries them only after the name itself
matched nothing, and an abbreviation only with the club's last word agreeing. A league before its
`Backfill:Seasons` start is not scheduled (the NBA from 2026-10-20). UFC is not scheduled from here,
and with ESPN off a UFC lines pull is not offered: there are no MMA scores, and a book's fight attaches
only to a bout ESPN holds. History before the day a user starts keeping scores is not available from
this source; the Board's Date range stays ESPN's and says what the default source can do instead.

## Consequences

- **A fresh clone with a key works:** fixtures, prices and final scores, and the journal settles from
  them. Box scores, player stats, play-by-play, injuries and fight cards remain ESPN's.
- **A desk on ESPN is unchanged.** The schedule adapter is not registered, its row stays off, the job
  list and the scheduler's passes are as they were, and nothing new reads or writes the journal.
  `OddsDefaultSourceTests` holds this against a seeded ESPN desk byte for byte.
- **The free month, honestly.** Lines cost a credit per market per league: 2 on the Docker path
  `setup.ps1` writes (moneyline and spread; `THE_ODDS_API_TOTALS=` blank), 3 with totals (a host run's
  default). Scores cost 2 a league. In November, with the NBA and NFL in season, automatic scores take
  about 4 a day — 120 a month — leaving 500 − 100 reserve − 120 = 280 for automatic lines: about 70
  two-league scans at 2 markets (one every ~10 hours), or 46 at 3 (one every ~16). On request only, a
  league priced and scored once a day is 4 credits a day (120 a month) at 2 markets, 5 (150) at 3;
  three leagues so is 360 or 450. A spring month with MLB in season adds a third league's scores
  (180 a month on Automatic).
- **The spine is displayable where this source wrote it** (ADR 0024): its rows come from a
  `Displayable` source. A per-game provenance column remains the amendment's open item; nothing here
  changes a visitor's readers.
- **Not verified against the live API.** No call was made with a real key; the adapter and its tests
  follow the published shapes.
