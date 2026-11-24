# The next phases — research and design

**Date:** 2026-09-22
**Status:** Research and plan, researched against `LineX_Development` at `d1120f0`; Phases 1–6 done (§6–§11)
**Method:** the live database (read-only, measured today), a read of every panel under
`src/LineOps.Web/Components/Panels`, the ingestion, data, reliability and worker code, and
ADRs 0009–0017. Claims that carry a phase were checked by hand; file:line references are to
`d1120f0`.

The short version: **the desk is well built and hardly used, and the data under it went dark
on the day the NFL season started.** Ingestion has not run since 11 September. The journal
holds zero entries, so the analytics half of the product — CLV, ROI, bankroll — has never met
a real bet. Beneath that sits a date bug that files every evening game under the wrong day.
The next phases therefore run in this order: restore and correct the data, then make the
money path right, then make the desk live, and only then widen what it shows.

---

## 1. What the database says today

| | Measured | Reading |
|---|---|---|
| Last ingestion run | `espn:slate` 2026-09-11 23:34 UTC; odds 2026-09-09 | `lineops-postgres` exited 11 Sep. NFL week 1 Sunday onward was never seen. |
| MLB 2026 | 2,199 final, **10 Live**, 11 Scheduled in the past | 3058–3060 (4 Sep, 21:40/22:10 ET) sat at `Live` for a week *while the host ran* — see §2.1. |
| NFL 2026 | 2 final, 49 past games still `Scheduled` | Weeks 1–2 missing entirely. |
| NFL 2025 | 285 final, 285 with stats, 108 with an ESPN close | As the seasons spec left it. |
| Closing lines, MLB 2026 | 2,198 of 2,199; **102 from a market**, the rest ESPN reference | The book-market path of ADR 0011 has closed ~5% of games. |
| Odds scans | 2,264 rows: `h2h` 1,414, `spread` 850; **no totals**; one source | Line polling is `Manual`; totals are excluded to save credits. |
| Journal | **0 entries** | Performance, CLV and settlement have never run on real data. |
| Ingestion runs | 7 stuck at `Running` since July/August | Orphans from killed hosts; nothing reaps them. |
| Alerts / incidents | 9 alerts (1 open volume anomaly), 3 incidents, all closed | The reliability loop works. |
| Snapshot | `data/snapshots/lineops.dump` taken 2026-09-05 | Seventeen days stale; the laptop is behind the desktop. |

## 2. Findings

### 2.1 Evening games are filed under the wrong day (bug, confirmed)
ESPN's scoreboard takes a US-local date (`EspnStatsAdapter.cs:59`). The platform builds its
dates from UTC: the slate asks for `DateOnly.FromDateTime(DateTime.UtcNow)`
(`IngestionJobs.cs:173`), and the results sweep derives a game's day from
`StartsAt.UtcDateTime` (`IngestionJobs.cs:403`). No code in `src/` names a time zone.

From 8pm ET (00:00 UTC) the slate pass fetches *tomorrow's* board, and any game starting at or
after 8pm ET is owed results on a date whose scoreboard does not list it. That covers Sunday
and Monday Night Football and most West Coast MLB. Games 3058–3060 are the proof: they are
stuck at `Live` since 4 Sep although the host ran until 11 Sep. Commit `7bdba3d` ("a results
sweep backs off a date it cannot heal") treated the symptom.

### 2.2 The money path has four holes
1. **Void never settles.** `JournalEntry.IsSettled` omits `Void` (`Journal.cs:85`), so a voided
   entry keeps its "Settle…" menu and counts toward "N pending" (`JournalPanel.razor:180,256`).
2. **Postponed games never void their bets.** Cancelled, postponed and suspended all map to
   `Postponed` (`EspnStatsAdapter.cs:477`); nothing moves a bet on one out of `Pending`.
3. **CLV ignores the line.** Settlement matches a close on book, market and outcome
   (`SettlementService.cs:120-160`) and `ComputeClv` compares prices only. A spread taken at
   −1.5 −110 and closed at −2.5 −110 scores as zero CLV when it was a point and a half of value.
4. **Parlays are straight bets.** `ParlayGroupId` is indexed and read by nothing; legs grade
   and aggregate independently.

Also: `BackfillMissingClvAsync` reloads every settled entry without a close on every tick,
forever, including ones that can never get one (free-text markets).

### 2.3 The desk is static
No panel refreshes itself; the only timer is the footer clock (`DeskFooter.razor:51`). The
README's promise that a minimised window's pulse "still tells you something changed" is not
kept. Windows do not tell each other about writes: a wager logged in `BoardWager` does not
reach an open Journal or Performance window, and Performance loads once with no Refresh
(`PerformancePanel.razor:79`). The layout, the window ceiling and the primary live in memory
per circuit (`WindowManager.cs:6`), so a reload loses them, and there are no user workspaces.

### 2.4 Journal and Performance are first drafts
- Journal: no edit or delete; 200-row cap with no paging or filter; book is free text
  defaulting to `draftkings`; the game picker offers the last 7 days capped at 80; `Note`,
  `PlayerId` and `ParlayGroupId` are stored but never shown.
- Performance: no filter by season, sport, book or date; the "bankroll" is cumulative profit
  because `startingBankroll` is never passed (`PerformanceAnalytics.cs:97`); the curve orders by
  placement, not settlement; no average CLV or beat-the-close rate in the headline.

### 2.5 Navigation stops short
No global search. Players opens an inline log instead of the Player window, which
`WindowShortcuts.cs:52` says was the fix. Team names are not links there. A game cannot show
your bets on it. Line movement lists the last 14 days only (`OddsPanel.razor:17`). Every
keystroke in Players runs two queries unthrottled, and `%`/`_` go into `ILIKE` unescaped.

### 2.6 Operations blind spots
- Every alert rule is about a *source* (`AlertEngine.cs:12-18`). Stuck-Live games, finals
  without stats, finals without a close and days owed results are what actually went wrong in
  §1 — and only hand-run SQL notices them.
- Line polling is `Manual`, but the odds source still carries the 26-hour critical freshness
  rule. A day without a manual pull reads as an outage — the "criticals are furniture" failure
  ADR 0017 warned about.
- Nothing reaps `Running` runs whose host died.
- `docker-compose.yml:119` still sets `Ingestion__Demo__Enabled` (retired, ADR 0017); the
  `balldontlie` source is configured with no adapter.
- `MlbStatsApiAdapter` is registered (`IngestionServiceCollectionExtensions.cs:96`) and called
  by nothing: doubleheaders still match on start time, and probable pitchers are fetched by no
  one (ADR 0014's "spine").
- ESPN delay and rain statuses fall through to `Scheduled` (`EspnStatsAdapter.cs:479`).

### 2.7 Coverage and documentation drift
No test covers a panel (18 of them), `TheOddsApiAdapter` (the one live odds feed), the
scheduler, store-on-change or backfill resume. `DESIGN.md` still describes draggable windows,
edge docking and the Slate (§277–317). The seasons spec's per-season coverage report (§4.6) was
never built. `KpiDailies` is computed every tick and read by nothing.

---

## 3. The phases

Each phase is a sitting or two on `LineX_Development`, ends with the verification in
`CLAUDE.md`, and is independently shippable. Phases 1 and 2 are not optional; 3–6 are ordered
by value and can be reordered.

### Phase 1 — Back on the air, on the right day
*Goal: every game on the correct date, NFL 2026 caught up, and the platform tells you when
data is missing before you do.*

1. **A league clock.** `LeagueCalendar.LocalDate(sportKey, instant)` in Core, using
   `America/New_York` for both leagues (ESPN's scoreboard day). The slate, the live poll, the
   results sweep, the board's day boundary ("stops at midnight", `d1297be`) and the backfill all
   ask it instead of `DateTime.UtcNow`. *Tests: a 20:15 ET kickoff is owed on its ET date; the
   slate at 23:30 ET asks for today; DST changeover weekends.*
2. **Reap orphans.** On host start, and in the evaluation tick, close `Running` runs older than
   a job's ceiling as `Failed` with `Error = "host stopped"`.
3. **Data-quality rules** beside the source rules, one alert per (rule, sport): `stuck_live`
   (Live > 6h after start), `finals_without_stats`, `finals_without_close` (MLB/NFL current
   season, grace 24h), `results_owed_days` (> 1). These are the §2 coverage queries from the
   seasons spec, promoted from a checklist to a rule. Warn, not Critical.
4. **Freshness follows the polling mode.** When line polling is `Manual`, the odds source's
   freshness rule measures against the last *requested* pull, or is suppressed — an unrequested
   feed is not stale.
5. **Delay statuses** map to `Live` (`STATUS_DELAYED`, `STATUS_RAIN_DELAY`, end-of-period).
6. **Remediate and refresh.** Start the hosts; run the backfill for 11 Sep → today (the
   checkpoint makes it resumable); re-sweep the 4 Sep and 27 Aug stragglers; confirm zero
   stuck Live and zero finals without stats; `publish-data.ps1 -Commit`.
7. Drop `Ingestion__Demo__Enabled` from compose; remove or keep inert the `balldontlie`
   configuration (seasons spec §4.1 left it open).

*Done when:* the coverage queries read zero, a Monday night game settles without a sweep
retry, and the snapshot manifest is dated this week.

### Phase 2 — The money path is right
*Goal: the first real bet you log grades, settles and scores CLV correctly.*

1. `IsSettled` includes `Void`; void entries settle to zero and leave the pending count.
2. **Auto-void.** A game `Postponed` past its league's make-up window (MLB: the series ends;
   NFL: 7 days) voids its pending straight bets. Distinguish `Cancelled`/`Suspended` in the
   status mapping so a suspended MLB game that resumes is not voided.
3. **Line-aware CLV.** Match the close on line as well as price where the market has one. When
   the lines differ, express CLV in points (`LineTaken − ClosingLine`, signed by side) alongside
   the price CLV, and convert both to a no-vig probability edge so they sum into one number.
   Record which comparison was made (`ClvBasis`: same line / line moved / cross-book).
4. ~~**Parlays grade as a group.**~~ Moved to Phase 4 (see §7): nothing can create a parlay
   yet, and how its stake is stored belongs with the form that creates it.
5. **Stop retrying the hopeless.** A settled entry gets a bounded number of CLV attempts
   (or a `ClvUnavailable` stamp once its game's closing window has passed).

*Done when:* `SettlementService` tests cover void, postponement, a moved spread, a two-leg
parlay with a push; a hand-logged NFL wager settles end to end.

### Phase 3 — A live desk
*Goal: an open window shows the truth without a click, and one window's write reaches the
others.*

1. **A desk event bus.** A scoped `DeskEvents` per circuit with typed events (`GamesChanged`,
   `OddsChanged`, `JournalChanged`, `AlertsChanged`). In-circuit writers (wager, settle)
   publish directly.
2. **Cross-process changes via Postgres `LISTEN/NOTIFY`.** The Worker already writes through
   `LineOpsDbContext`; the ingestion services `NOTIFY lineops_changes, '<kind>:<sport>'` after
   a run that wrote rows. The Web host holds one listening connection (a hosted service) and fans
   out to circuits through a singleton hub. No polling, one connection, and it works whichever
   host did the ingest. Fallback: a 60s version check when the listener is down.
3. **Panels subscribe in `PanelBase`**, debounce to one reload per second, and re-`Report()`
   their pulse — which makes the README's minimised-window promise true.
4. **Persist the desk.** Layout, ceiling, primary and theme to `localStorage` through the
   existing `windowing.js`; named user workspaces saved beside the three built-ins. Restore on
   circuit start.
5. A **"Bet a game"** built-in workspace: Board → Game → Place wager → Journal.

*Done when:* a result landing in the Worker updates an open Board's score and the Journal's
pending count within seconds; a reload restores the desk.

### Phase 4 — Journal and Performance, finished
1. Journal: edit and delete (delete is a sheet with confirmation; a settled entry's edit
   re-settles), paging and filters (status, sport, season, book, date), book as a picker over
   `Sources`/known books, `Note` shown, game picker as a search over the season rather than 7 days.
2. Performance: filters by season, sport, book, market and date; a real bankroll with a stored
   starting amount; the curve by settlement time; headline average CLV and beat-the-close %;
   breakdowns by sport and by `ClvBasis`. `AsNoTracking` throughout.
3. From a Game window, "Your bets on this game" — the one link the journal needs to feel wired
   into the desk.
4. **Parlays, end to end** (moved from Phase 2). A way to log one, and grading as a group: every
   leg wins → win at the product of the legs' prices; a push or void drops a leg and reprices;
   one loss loses the group. Stake and payout on the parlay, not the legs, and Performance
   counts it once. Decide the storage with the form — a `Parlays` row the legs point at is the
   likely answer, since legs sharing a stake by convention would need every reader to fold them.

### Phase 5 — Find anything
1. **A command palette** (Ctrl+K): teams, players, games by matchup and date, windows,
   workspaces. One `SearchService` over indexed columns, debounced 200ms, `ILIKE` escaped.
2. Players rows open the Player window; team names link to Team (`WindowShortcuts` already
   names the rule).
3. Line movement takes any game with scans or a close, by season, not the last 14 days.
4. Board: filter by team and a text box, sharing the palette's search.

### Phase 6 — More market, same budget
Only after the above: widening the data is worth little while the journal is empty and the
data is misfiled.

1. **Totals, priced.** The Odds API bills `markets × regions`; totals take a scan from 2 to 3
   credits. At 500 credits a month and the September burn so far (66 credits), automatic
   polling with the existing `LinePollPlanner` has room for all three markets on NFL (≈16
   games a week, one scan per slate window) and moneyline + total on MLB. Make `Markets` a
   per-sport option and let the planner report projected month-end burn in Ops before it is
   switched on.
2. **Line polling to `Auto`** once Phase 1's freshness change lands, so the book-market path
   of ADR 0011 closes more than 5% of games.
3. **Wire the MLB Stats API spine** (ADR 0014): doubleheader disambiguation by `gamePk`, and
   probable pitchers stored on the game and shown on the Board and Game windows — the single
   most useful handicapping fact the platform fetches and throws away.
4. **Entity resolution scoped by team** (ADR 0009 open item): two ESPN athletes sharing a name
   stop merging.

### Standing work, alongside any phase
- **Tests for the paths that carry money or data:** bUnit tests for Journal settle,
  BoardWager and Performance; unit tests for `TheOddsApiAdapter` parsing, store-on-change and
  backfill resume; the §2.1 date cases.
- **History window coverage report** (seasons spec §4.6): expected / held / with stats / with
  close, per season. The data-quality rules in Phase 1 compute the same numbers.
- **`DESIGN.md` catches up** with the fixed-row desk, the Board's absorption of the Slate and
  the full panel list.
- **Keep TicketMiser in step.** Phase 3's event bus and persistence, and Phase 5's palette,
  live in the desk layer that was carried to `C:\OS\Tix\TicketMiser`. Build them domain-free
  (the palette takes an `ISearchProvider`) so they port as files, not rewrites.

---

## 4. Decisions that are yours

1. **Is the journal going to be used?** If not, Phases 2 and 4 shrink to the void fix and the
   desk becomes a data and ops showcase; if so, they are the core of the product. This
   document assumes yes.
2. **Totals and automatic polling (Phase 6.1–6.2)** spend the free credit tier deliberately.
   Worth the planner's projection first.
3. **Player props** (`OddsSnapshot.PlayerId` exists, unused) are deliberately out: each prop
   market is another credit multiplier, and the free tier cannot carry them. A paid tier is the
   precondition.
4. **Still open from the seasons spec:** purge NBA/NHL residue or hide it; whether ~38% NFL 2025
   closing-line coverage (108 of 285) wants a paid historical source; postseason on by default.
5. **A second user** stays a separate project (seasons spec F8): an owner on `JournalEntries`
   and an auth layer.

## 5. Risks
- The date change touches every scheduled job; land it with the DST and late-kickoff tests
  first, and run the backfill after, not before.
- `LISTEN/NOTIFY` needs a dedicated, long-lived Npgsql connection outside the EF pool, with
  reconnect; the fallback check keeps the desk honest when it drops.
- Line-aware CLV changes numbers already shown. With zero journal entries this is the cheapest
  moment it will ever be.
- A plaintext The Odds API key sits in `src/LineOps.Worker/appsettings.Local.json` (gitignored,
  not in history). Moving it to user-secrets, as the Web host does, removes the one copy that
  could be committed by accident.

---

## 6. What was done — Phase 1 (2026-09-22)

- **League clock** (`b338e14`). `LeagueClock` (US Eastern) answers every "which day" question
  about games: slate, results sweep, backfill walk, season rules, the board's midnight. Two
  more instances of the same bug surfaced on the way: the backfill checkpointed tonight's ET
  slate while it was in play (how 3058–3060 got stuck), and `SeasonCalendar` filed a Monday
  night week-18 game as postseason. The slate pass also fetches the day of any game still in
  play from before midnight. ESPN statuses are read by `state` when the name is unlisted.
- **Operations** (`c053004`). Three sourceless data rules (`unfinished_games`,
  `finals_without_stats`, `finals_without_close`) with runbook sections; `OrphanRuns` closes
  runs a stopped host left Running (7 reaped); `OddsOnDemand`, set from the polling mode,
  stands the odds freshness rule down under Manual polling.
- **Images** (`2d60640`). Neither image built (`docs/runbook.md` excluded from the context,
  Observability not restored) and the worker image lacked the ASP.NET framework; all fixed.
  The retired demo flag is gone from compose.
- **Remediation.** Worker run in Docker; backfill walked 34 days, 0 failed, 225 games, 18,652
  rows. NFL 2026 weeks 1–2 complete (32 finals, all with stats and closes); every MLB final
  has a box score. The four "Scheduled" games of 27 Aug were not owed results — they were
  duplicate fixtures from a second The Odds API event id a minute off the real games, the only
  such rows in history; deleted with their 62 duplicate closes. `unfinished_games` resolved
  itself on the next tick. Snapshot refreshed (`1a73847`).
- **Not done:** the balldontlie configuration is untouched (inert). Why the resolver accepted
  a second odds id for an existing fixture on 27 Aug is unexplained; it has not recurred.

Tests: 394 in `LineOps.Tests`, 263 in `LineOps.Web.Tests`, all green — run in the .NET SDK
container, because Smart App Control on the desktop blocks some fresh unsigned builds.

---

## 7. What was done — Phase 2 (2026-09-22)

- **Void is settled, not graded.** `IsSettled` now includes `Void`; a new `IsGraded` (win, loss,
  push) is what ROI, the bankroll curve and the breakdowns count, so a returned stake no longer
  dilutes the return. The Journal keeps its Settle menu on a void, so an automatic one can be
  corrected.
- **Postponed games void their bets** once they are 36 hours past their start unplayed
  (`SettlementService.VoidPostponedAfter`). ESPN's postponed, cancelled, suspended and abandoned
  are one status here, so this does not tell a suspended MLB game that resumes from one that is
  called; a game made up inside the window is graded on the make-up, and the Settle menu covers
  a book that ruled otherwise.
- **CLV knows the line.** Settlement records the close's number and book on the entry
  (`ClosingPoints`, `ClosingBook`; migration `ClosingPointsAndBook`). `ClvResult` carries the
  points gained from the bettor's side; when the line moved, points decide beat-the-close and
  the price difference is not averaged, because it compares prices for different bets. The
  Journal shows `+1 pts` where the line moved and says in the tooltip what was compared, and
  names the fallback book when the entry's own book had no close. The spec's single
  no-vig number that sums points and price was not built: converting points to probability
  needs a per-sport model of how often games land on each number, and a guessed one would be
  a fabricated reading.
- **Same-book matching is case-blind.** The odds feed writes `draftkings`, ESPN's reference
  `DraftKings`; an exact match missed the entry's own book and fell through to cross-book.
- **CLV retries are bounded** to settled entries with a real market whose game started in the
  last seven days (`ClvSearchWindow`), instead of every unresolved entry on every tick for ever.
- **Parlays moved to Phase 4** (above).

Tests: 407 in `LineOps.Tests`, 263 in `LineOps.Web.Tests`, all green (in the SDK container).
The Worker applied the migration on start. The end-to-end check against a hand-logged wager
waits for the first real one — the journal is still empty, and a test row in it would be data
the analytics then count.

---

## 8. What was done — Phase 3 (2026-09-23)

- **A change feed.** `ChangeNotifier`, a save interceptor on every `LineOpsDbContext`, sends
  `pg_notify('lineops_changes', 'games,runs')` after each save, naming the topics its rows belong
  to (`DataTopics`). One interceptor covers every writer — the worker, the web host's own
  schedule, the wager form, settlement — and crosses the process boundary. Bulk
  `ExecuteUpdate` calls (the orphan reaper) do not pass through it; nothing waits on them.
- **One listener per web host.** `DataChangeListener` holds a dedicated connection on
  `LISTEN`, reconnects on its own, publishes every topic once on reconnect, and while it is down
  publishes every topic once a minute, so windows degrade to polling rather than going quiet.
  The footer says which: `live` or `polling`.
- **Panels refresh themselves.** `DeskSignals` (in the desk, domain-free) carries topics;
  `PanelBase` subscribes for any panel that names `Watches`, settles for a second so a burst is
  one reload, and reloads through `RefreshAsync`. Twelve panels watch: Board, Game, Head to head,
  Team, Player, Line movement, Journal, Performance, Ops, Incidents, Runs, History. The Board's
  follow-ups and the wager form do not — a form refreshing under a hand is worse than stale.
- **The desk survives a reload.** `DeskLayout` — windows, order, weights, minimised, the ids they
  were opened on, focus, ceiling, primary, resolution — goes to `localStorage` under a per-brand
  key, written half a second after a change and only when it differs. Operators can save the row
  as a named workspace (Window manager → Workspaces); it appears in the brand menu under "Saved
  here". A new built-in, "Bet a game": Board, Journal, Performance.
- **Verified live** in the browser against the running worker: with the desk untouched, a live
  poll moved Reds–Braves from 2–1 to 2–2 on the open Board; a reload put the three-window
  workspace back; `psql LISTEN` showed the worker announcing `games,runs`.

Found on the way, and fixed:

- **The web image served a page that never started.** Its restore ran against project files only,
  and the .NET 10 SDK adds `Microsoft.AspNetCore.App.Internal.Assets` (Blazor's
  `_framework/blazor.web.js`) only for a project whose Razor components it can see — so the
  `--no-restore` publish shipped without the script, and the circuit never connected. Publish now
  restores once the source is in.
- **The Inter font 404'd** since the desk became its own project (`ea2a860`): the stylesheet moved
  to `LineOps.Desk` and asks for `../fonts/`, the files stayed in `LineOps.Web`. Moved, with the
  OFL licence TicketMiser's copy already carries.
- **Postgres had stopped again**, cleanly, ~22 hours earlier (a fast-shutdown request, cause
  unknown — no script in the repo stops it). The hosts crash-looped on it until it was started.

Not done: the desk shows times in the server's zone (`ToLocalTime`), which in a container is UTC.
Tests: 416 in `LineOps.Tests`, 273 in `LineOps.Web.Tests`, all green (in the SDK container).

---

## 9. What was done — Phase 4 (2026-09-23)

- **Parlays are bets.** A `Parlays` row holds the stake, the book's quoted price when there was one,
  the result and the payout; its legs are journal entries pointing at it (the old
  `ParlayGroupId` became the foreign key; deleting a parlay deletes its legs). `ParlayGrading`
  grades it the way books do — a loss loses it, a push or void drops a leg and reprices, all
  pushed returns the stake — and pays a clean sweep at the quoted price, otherwise at the
  product of the legs. A win with nothing to price it by stays pending with the reason rather
  than being paid at an invented number. Settlement grades parlays in the same pass as their
  legs. `ParlayGrading.Ledger` is what every money figure reads: straight bets as they are, each
  parlay once, legs never. Legs are still priced against the close, so CLV counts them.
- **The Journal is editable.** Edit (a change to anything the grade depends on sends a graded
  entry back to pending and through settlement again; a note alone does not), delete with a
  confirmation, a parlay form, filters by status, period, sport and book, "show more" in steps
  of 100, notes shown, a book picker over the common books and every book the data has seen
  (written lower-case, so "DraftKings" and "draftkings" are one book), and a game picker that
  searches the season by team — "reds braves" — nearest to today first. `JournalService` holds
  all of it, so the panel has no queries of its own.
- **Performance is filterable and has a bankroll.** Period, sport, season, book and market; a
  starting bankroll stored with the data (`AppSettings`), a bankroll metric, and the curve by
  when bets settled (`SettledAt`, new) rather than when they were placed. Breakdowns by market,
  sport and book, and a table of what CLV was measured against — same number, line moved,
  another book's close, no close — with beat-the-close and the average for each.
- **The Game window lists your bets on the game**, straight and legs, with result and CLV.

A bug the tests caught before any person could: `SaveEntryAsync` guarded the draft-writing call
behind `id is not null &&`, so a new entry was saved blank. Verified live: the forms, filters,
book list and the season-wide game search render and work against the running stack; nothing
was saved, since the first entry in this journal should be a real one.

Tests: 437 in `LineOps.Tests`, 273 in `LineOps.Web.Tests`, all green (in the SDK container). The
worker applied the `ParlaysAndSettlement` migration on start.

---

## 10. What was done — Phase 5 (2026-09-23)

- **A command palette.** Ctrl+K (⌘K) from anywhere, or the search key in the header. It lives in
  the desk and knows nothing about sports: the desk supplies its own windows and workspaces, and
  an application registers an `IDeskSearch` (scoped, so it opens windows on the circuit's own
  desk) for what it knows — LineOps' `AppDeskSearch` adds teams, players and games. Empty, it is
  a keyboard launcher over the windows that open on nothing; typing asks the application after a
  200 ms pause; arrows and Enter pick, Escape or a click outside closes. A window whose name
  starts with what was typed leads the application's results — found live, when "board" opened a
  Giants player called Board instead of the Board.
- **One search rule.** `SearchService` (Data) finds teams, players and games: every word must
  match one of the fields it could mean ("reds braves", "smith rams"), typed `%` and `_` are
  characters, hidden leagues stay hidden, players who have played lead, games nearest to today
  first. The palette, the journal's game picker and the Players window use it, and the Board uses
  its in-memory twin (`MatchesAllWords`) to filter the loaded slate by team.
- **Players finds people; the windows say how they are going.** A row opens the Player window
  (it used to keep a lesser game log of its own, cut to eight stat columns alphabetically), a
  team name opens the Team window, the search waits for a pause, and a capped list says so.
- **Line movement** lists the games that have moves on record — the ones it can draw — instead of
  the last fourteen days, and the season-wide game picker reaches any other; its empty state says
  where a finished game's close is.

Found by the tests: the palette marked its highlighted row `aria-selected=""` — Blazor renders a
bare boolean that way — which screen readers do not read as selected. The rest of the desk's
ARIA state attributes already used strings.

Verified live: Ctrl+K → "mets" → Enter opened the Mets' Team window; "board" → Enter opens the
Board; the Board's team filter narrowed the slate to the Blue Jays' games.

Tests: 447 in `LineOps.Tests`, 280 in `LineOps.Web.Tests`, all green (in the SDK container).

---

## 11. What was done — Phase 6 (2026-09-23)

- **A scan's price comes from the adapter.** `IOddsSource` now says what markets it asks for per
  sport (`MarketsFor`) and what a scan of a sport bills (`CreditsPerScan`); The Odds API's is its
  market count, one region unit. `SourceOptions.MarketsBySport` sets a list per sport — e.g.
  totals for NFL only. The planner paces on the adapters' figures (the configured
  `CreditsPerSportPerScan` is now only the fallback for a source that declares nothing — ADR
  0012's open item), the Pull lines menu quotes each sport's real markets and credits, and Ops
  shows, before anything is switched on, what each sport's scan buys and costs and how often
  automatic polling would scan at that price. The planner cannot overspend; a longer list shows
  up as a slower cadence, which is its real cost.
- **Not switched on.** Totals and automatic polling spend the free tier deliberately, and that
  is the operator's call (§4.2). Nothing here changes a market list or the polling mode.
- **The MLB spine is called.** `MlbSpineService` reads MLB's schedule after each slate pass (at
  most every 30 minutes, today and tomorrow) and annotates ESPN's games — it never creates one:
  MLB's `gamePk`, the doubleheader number (in play order), and both probable starters, as MLB
  states them, including taking a scratched one back. Team names agree between the two
  providers for all thirty clubs; the MLB team id is written onto each team on first match. The
  Board shows "Game 2" and "Van Eyk v Gibson" under the matchup; the Game window shows the
  starters in full. First live pass: 16 of 16 of the day's games matched, all with starters,
  including a Blue Jays–Orioles doubleheader numbered 1 and 2. Tomorrow's games attach once ESPN
  has created them.
- **Two people with one name are two players.** The name fallback in player resolution no longer
  merges an athlete into a player the same source already knows under a different id, and where
  several same-named players remain, the team decides — or nothing is merged (ADR 0009's open
  item). Players already merged before this remain merged; splitting them needs a re-ingest of
  their games.

Found on the way: the Board cut "CJ Van Eyk" to "Eyk" (last word as surname); it now drops only
the first name. The Docker hosts have no odds source at all — the key lives in the host-only
`appsettings.Local.json`, `.env` has none, and the image's `TheOddsApi.Enabled` is false — so odds
are pulled only when the web host runs from the SDK.

Tests: 455 in `LineOps.Tests`, 285 in `LineOps.Web.Tests`, all green (in the SDK container).

## 12. After the six phases

What the plan named and did not do, for whoever picks it up:
- Totals and automatic polling (§4.2) — priced in Ops, waiting on the operator.
- Player props — a paid tier first (§4.3).
- Splitting players merged before Phase 6.
- Display times in one zone rather than the server's (a task is open for it).
- Porting the desk changes to TicketMiser (a task is open for it).
- `DESIGN.md` still describes the desk as it was before these phases.
