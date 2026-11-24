# ADR 0024 — The data is tiered by licence: visitors read what may be shown

**Status:** Accepted. Built as a trial on 2026-11-14, amended 2026-11-15 (below), and merged into `LineX_Development`. `Sharing:Mode` is `Local` everywhere it runs today.

**Builds on:** [ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md), whose split
between a stats port and a book market is what makes the tiers separable, and
[ADR 0018](0018-an-operator-and-visitors.md), whose operator and visitor are the two readers.

**Plan:** [Going public](../specs/2026-09-27-going-public-plan.md) §4.

**Extended by:** [ADR 0026](0026-the-odds-api-is-the-default-source.md), which builds the Odds API spine adapter the amendment's spine analysis describes.

## Context

The desk is reached from more than one device, and one day by people who are not its operator.
ADR 0018 made that safe for the controls: a visitor reads and changes nothing. It said nothing
about what a visitor reads, because until now everything on the desk came under the same
terms. It does not. The plan's §4 read each provider's published terms:

| Source | What it supplies | Terms, in short | May a visitor see it? |
|---|---|---|---|
| The Odds API | Scans and closes from a book market | Display in a UI allowed, commercial display included; derived values allowed | Yes |
| balldontlie | Seeded, no adapter yet | Public display and betting products allowed | Yes |
| ESPN (undocumented JSON) | Schedules, scores, box scores, play-by-play, fighter profiles, the injury report, a reference close | Automated extraction and commercial use prohibited | No |
| MLB Stats API | The game identity spine, probable pitchers | "Individual, non-commercial, non-bulk use" | No |
| odds-api.io | An alternative odds adapter, off | Free tier is for evaluation; a paid plan's terms unread | No |

The one thing standing between a hosted desk and a desk others may lawfully look at is that it
would show ESPN- and MLB-derived numbers to people other than the developer. Every row those
numbers live in already says where it came from: `ClosingLines`, `OddsSnapshots`,
`PlayerGameStats`, `StatSnapshots`, `GamePlays` and `GameDrives` each carry a `SourceId`. What was
missing was a word on the source saying what its terms allow, and a reader that asks.

## Decision

**A source carries its licence.** `Source.Licence` is `PersonalUse` or `Displayable`.
`PersonalUse` is zero, the default, so a source nobody has classified is one the desk keeps to
itself. `SourceLicences.Known` in `XLines.Core` holds the table above by key; the initializer
seeds from it, and migration `SourceLicence` adds the column (stored by name, as `Kind` is) and
names the two displayable providers by key, so a database restored from an older snapshot is
classified the same way a fresh one is seeded.

**The readers ask; the host answers.** `IDataTier.CanSeePersonalUseSources` lives in
`XLines.Core`, beside the contracts, because the readers that ask are in `XLines.Data`, which
knows nothing of the desk. `AddXLinesData` registers `FullDataTier`, which says yes, so the
Worker, the tests and any host that says nothing read everything. The web host registers
`DeskDataTier`, which answers

> `Sharing:Mode == Local || IDeskAccess.CanOperate`

and reads `CanOperate` each time, as ADR 0018's access does, so it fails closed with it.
`Sharing:Mode` is `Local` by default: the laptop is unchanged. `Shared` refuses to start on a desk
whose `Access:Mode` is not `Gated`, because an open desk makes everyone its operator and "the
operator alone" would be everyone.

**Filtered at the read service, not at each panel.** This is ADR 0018's catalogue rule applied to
rows. A reader reads the set of sources this person may see once
(`DataTierQueries.VisibleSourceIdsAsync`) — null when it is all of them, so a local desk sends the
same SQL it always has — and narrows each provider-supplied table to it (`FromVisible`). It is the
set that *may* be read, not the set that may not, so a source added later is hidden until someone
classifies it. Three readers do this now:

- **`BoardService`**: scans, closes and the passing lines the starting quarterback is read from.
  A game only ESPN closed is unpriced for a visitor, and says so in the board's own words.
- **`GameLogService`**: the closes a log is graded against, a player's game log and seasons, and a
  game's box score. A game only ESPN closed goes ungraded — not graded against nothing, not lost.
- **`MatchupCrossReference`**: the stat lines form is averaged from. No visible lines, no form.

Nothing new is drawn for the omission. The desk already says "no fair close" for a game the
market never covered, and already shows a preview for a game with no box score (plan §4,
consequence 2). The project's own rule does the work as written.

**A snapshot can carry only the displayable tier.** `publish-data.ps1 -DisplayableOnly` leaves out
the rows of `PlayerGameStats`, `StatSnapshots` and `FighterProfiles` (and the journal and
play-by-play, as always). A personal-use close sits in the same table as the market's, which
`pg_dump` cannot split, so the dump is restored into a throwaway server inside the sidecar, the
personal-use rows of `ClosingLines`, `ClosingLineMisses` and `OddsSnapshots` are deleted there, and
that copy is what is dumped. ESPN's and MLB's own words about a game — last play, situation,
linescore, status detail, probable pitchers — and the injury report are cleared in the same pass.
The script refuses rather than publishes if a personal-use row survives. The manifest says
`"tier": "displayable"`; an ordinary snapshot says `"full"`. `-OutDir` writes it somewhere other
than `data/snapshots`, and `-Commit` publishes only from there.

## Consequences

- **A shared or hosted desk may lawfully be shown to others only for the displayable tier.** With
  `Sharing:Mode=Shared` behind a gate, a visitor reads the book market's odds and closes and what
  is derived from them — best price, the spread between books, CLV — and the readers above show
  nothing ESPN or MLB supplied. That is a statement about those readers, not yet about the desk:
  see Remaining readers.
- **The operator's own view is unchanged.** Signed in as the operator, or on any local desk,
  every reader returns exactly what it returned before; the existing suite passes untouched.
- **The tier follows the provider, not the kind of data.** The day the balldontlie adapter writes
  box scores, a visitor sees them with no change to any reader, because balldontlie is already
  `Displayable`. ESPN can stay the operator's second source for confirming a final (plan §4,
  consequence 1) without ever reaching a visitor.
- **The schedule is still ESPN's.** Games, teams, players and final scores are read from ESPN (and
  MLB's spine), and every odds row hangs off a game. A visitor therefore still sees fixtures and
  results ESPN supplied. Scores are facts, and The Odds API also supplies events and scores, but
  until a displayable source writes the schedule, this is the largest gap between "tiered" and
  "lawful to show". The displayable snapshot carries them for the same reason.
- **A new reader must ask.** A panel that reads a provider-supplied table directly bypasses the
  tier, as a control that forgot `CanOperate` bypasses ADR 0018. `IFromSource` marks every such
  table so `FromVisible` can be applied to it.

## Remaining readers

What a visitor on a shared desk can still reach that a personal-use source supplied, after the
amendment below:

- **The spine.** Games, teams, players and final scores (see the amendment's spine analysis). It
  wants a displayable schedule source and a column saying who wrote each game, not a filter.
- `DataQuality` (read by the History window, and by the alert engine) — counts of finals with and
  without stats or closes, per season. Left as it is: they count the operator's collection rather
  than repeat anything ESPN stated, and making History operator-only would reverse ADR 0018, which
  kept it readable with its walk controls hidden. It is outside the guard's folders (it lives in
  `XLines.Reliability`), so a change to what it returns should be read against this ADR.
- `BoardService`'s "last priced" time, read across every source's scans to tell "never pulled"
  from "skipped": a timestamp of the operator's pull, not a price.
- `JournalService`'s CLV reference — operator-only already (ADR 0018), so out of a visitor's reach.

## Amendment — 2026-11-15: the remaining readers are gated, and a guard keeps new ones honest

**What is gated now.** Every reader on the list this ADR first left open asks the tier. A local
desk and the operator read exactly what they read before; the existing suites pass, with one line
added to the Web suite's bench (`DeskTestContext` registers `FullDataTier`, as `AddXLinesData`
does for a host that says nothing).

| Reader | Gate | Why that gate |
|---|---|---|
| `GamecastService` plays, scoring plays, drives, probability line | `FromVisible` on `GamePlays` and `GameDrives` | Both carry `SourceId`; a visitor's gamecast is the empty state a game has before its first play |
| `GamecastService` header | The board's game (`BoardService.GetRowAsync`), blanked there | One projection, so the chart cannot disagree with the row |
| `FightService` bout, bouts, tape, fighter | The tier asked yes or no: no profile, the bout's card blanked | `FighterProfiles` and `Bouts` have no `SourceId` because ESPN is their only writer — single-source by construction, so there is no set of sources to narrow to |
| `BoardService` games, row and bouts | `PersonalUseColumns.ForTier` | Columns, not rows |
| `BoardService` quarterback | The injury report ignored for a visitor | `Player.InjuryStatus`/`InjuryNote` are ESPN's; the starter is read from the visible lines alone |
| `GameWindowData.GameAsync` (the Game window's title) | `PersonalUseColumns.ForTier` | The same game the row carries, blanked the same way |
| `SearchService.PlayersAsync` order | `FromVisible` on the counted `PlayerGameStats` | An order is still a reading of the box scores; a visitor's falls back to by name |
| `PlayersPanel` games-played count | `FromVisible` | A count of ESPN's lines is ESPN's number |
| `OddsPanel`, `MarketMovement`, `BoardPanel`'s moves today | `FromVisible` on `OddsSnapshots` | Market data, not operational: only odds-api.io's evaluation feed is personal-use, and it reaches no visitor even while switched on |

No window was made operator-only: every window in question shows market data or the spine, which
a visitor may see. ADR 0018's catalogue rule is for windows that are wholly the operator's.

**The columns.** `PersonalUseColumns.ForTier(Game)` clears, for a visitor, MLB's
`HomeProbablePitcher`/`AwayProbablePitcher` and ESPN's whole state of play: `Period`,
`DisplayClock`, `ClockSeconds`, `StatusDetail`, `PossessionTeamId`, `Down`, `Distance`, `YardLine`,
`DownDistanceText`, `PossessionText`, `IsRedZone`, `HomeTimeouts`, `AwayTimeouts`, `LastPlay`,
`Linescore`, `Situation` and `LiveUpdatedAt`. `ForTier(Bout)` clears the card — event, segment,
place on the card, weight class, title, the distance (to zero, which the Game window now draws as
no distance rather than a guessed "3 rounds"), the records going in, method, round and clock — and
keeps that the game is a fight and its `Outcome`, which is to a bout what the final score is to a
game. Both change an untracked entity in place and return it, and do nothing for the operator.
One helper, so the condition is written once and every projection that hands a game to the desk
uses it.

**The guard.** `DataTierGuardTests` reads `src/XLines.Data` and `src/XLines.Web` (migrations
aside): a `.cs` or `.razor` file that names a tiered set — every `DbSet` on `XLinesDbContext`
whose rows are `IFromSource`, found by reflection, plus `FighterProfiles` and `Bouts` — must also
name `IDataTier`, or sit on a short, commented allow-list (the initializer, which writes; the
context, which declares). Ingestion and reliability are writers and the Worker's jobs, and are not
scanned. It proves a reader's author had to decide, not that the decision was right;
`DataTierReaderTests` and `DataTierRemainingReaderTests` prove that, reader by reader.

### The spine a visitor still sees

Even with every reader gated, a visitor on a shared desk reads these, all written by ESPN or the
MLB Stats API:

- **`Games`**: `StartsAt`, `HomeTeamId`/`AwayTeamId`, `Status` (scheduled, live, final),
  `HomeScore`/`AwayScore`, `SeasonYear`/`SeasonType` (provider-stamped), `DoubleHeaderGame` (MLB);
  for a fight, `Bouts.Outcome`. `ExternalIds` (ESPN's and MLB's ids) are held but not drawn.
- **`Teams`**: `Name` and `Abbrev` (for the UFC, each fighter's name, since a fighter is a team).
- **`Players`**: `FullName`, `Position`, `TeamId` — wherever a player is named (the search, the
  Players window, a roster). `Status` is held but not drawn.

What The Odds API's own endpoints could supply, from its published v4 documentation
(<https://the-odds-api.com/liveapi/guides/v4/>) and its coverage table
(<https://the-odds-api.com/sports-odds-data/sports-apis.html>), both read 2026-11-15:

- **Events** (`GET /v4/sports/{sport}/events`) returns `id`, `sport_key`, `sport_title`,
  `commence_time`, `home_team` and `away_team`, and does not count against the quota. That is
  `StartsAt` and both sides by full name, for every league the desk prices, the UFC included.
- **Scores** (`GET /v4/sports/{sport}/scores`) returns the same plus `completed`, `scores`
  (`name`, `score`) and `last_update`; live scores refresh about every 30 seconds; `daysFrom` reaches
  back at most three days; it costs 1 credit, or 2 with `daysFrom`. That is `HomeScore`/`AwayScore`
  and a status (not started, in play, `completed`). The coverage table lists scores for MLB, NBA
  and NFL, and none for MMA.
- **Participants** (`GET /v4/sports/{sport}/participants`, 1 credit) returns `full_name` and `id`
  for a league's teams (players only for individual sports); it says to treat the list as a
  whitelist, and it names no team's players.
- **Historical events** (`GET /v4/historical/sports/{sport}/events`, 1 credit) give past fixtures
  at a snapshot time. No historical scores endpoint is documented, so a final older than three days
  is one this source cannot tell us unless the desk kept it at the time.

What none of these supply: abbreviations, a season year or type (the desk's own `SeasonCalendar`
can stamp both), MLB's doubleheader number, any player, and any fight's result.

**A minimal displayable spine** would take:

1. **Provenance on the spine** — a `SourceId` for who created each `Game` and who wrote its score
   (two columns, or a small `GameSources` table), and the same for `Teams`. That is a schema change
   and is deliberately not part of this amendment. A visitor's readers would then keep to games
   whose fixture and score a displayable source wrote, by the same `FromVisible` rule.
2. **An Odds API spine adapter**: the free events call for fixtures (the odds ingestion already
   resolves its events to games by sides and start), and one scores call a league a day with
   `daysFrom=3` — 2 credits a league a day, about 180 a month for the three team leagues, inside
   the budget credit pacing already keeps.
3. **The desk's own names**: abbreviations as the desk's own data (a committed table), since a
   full team name is all The Odds API gives.
4. **What it cannot cover, said plainly**: players (balldontlie, already `Displayable` and seeded,
   is the candidate; its coverage has not been read for this), UFC results (no scores for MMA), and
   history older than the day the adapter started keeping scores. A visitor's desk would show those
   as not held, in the words it already uses for a game nobody priced.
