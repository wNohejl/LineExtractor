# ADR 0024 — The data is tiered by licence: visitors read what may be shown

**Status:** Proposed (trial, branch `trial/source-tiering`)

**Builds on:** [ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md), whose split
between a stats port and a book market is what makes the tiers separable, and
[ADR 0018](0018-an-operator-and-visitors.md), whose operator and visitor are the two readers.

**Plan:** [Going public](../superpowers/specs/2026-09-27-going-public-plan.md) §4.

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
itself. `SourceLicences.Known` in `LineOps.Core` holds the table above by key; the initializer
seeds from it, and migration `SourceLicence` adds the column (stored by name, as `Kind` is) and
names the two displayable providers by key, so a database restored from an older snapshot is
classified the same way a fresh one is seeded.

**The readers ask; the host answers.** `IDataTier.CanSeePersonalUseSources` lives in
`LineOps.Core`, beside the contracts, because the readers that ask are in `LineOps.Data`, which
knows nothing of the desk. `AddLineOpsData` registers `FullDataTier`, which says yes, so the
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

Not yet gated; each reads ESPN- or MLB-derived data a visitor on a shared desk can reach:

- `FightService` — `FighterProfiles` (ESPN, no `SourceId`: the whole table is personal-use) and
  `Bouts` (the card, method and result, from ESPN).
- `GamecastService` — `GamePlays` and `GameDrives` (ESPN; both carry `SourceId`, so `FromVisible`
  applies directly).
- The live state on `Game` — `LastPlay`, `Situation`, `Linescore`, `StatusDetail`, down and
  distance — and `HomeProbablePitcher`/`AwayProbablePitcher` (MLB): columns, not rows, so they
  need a projection that blanks them rather than a filter.
- `Player.InjuryStatus`/`InjuryNote` (ESPN's injury report), read by `BoardService` for the
  quarterback, and shown wherever a player is.
- `PlayersPanel.razor` — counts `PlayerGameStats` directly.
- `SearchService` — orders players by their `PlayerGameStats` count (an ordering, not a number).
- `OddsPanel.razor`, `MarketMovement.razor` and `BoardPanel.razor`'s moves-today count — read
  `OddsSnapshots` directly; only odds-api.io's evaluation feed is personal-use, and it is off.
- `DataQuality` (Ops) — counts of finals without stats or closes: aggregates, not the data.
- `JournalService`'s CLV reference — operator-only already (ADR 0018), so out of a visitor's reach.
- The schedule itself (above), which wants a displayable source rather than a filter.
