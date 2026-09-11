# Reusing the LineOps desk for a ticket price tracker

**Date:** 2026-09-11
**Status:** Plan, researched against `LineX_Development` at `9b5c2fe`
**Audience:** the developer starting the second project

The second project is a ticket price tracker: compile the price of a given event's tickets
from several marketplaces on a schedule, keep the history, and show where the best price is
now and how it got there. This document records what in LineOps is reusable, what is not,
where the price data can come from, and a phased plan for building it on the same stack
(.NET 10, Blazor Interactive Server, MudBlazor 9.7, PostgreSQL 17, Docker).

The short version: **the desk is domain-free apart from five files, the reliability layer is
domain-free entirely, and the ingestion layer's shape transfers whole.** A ticket tracker is
LineOps with events instead of games, marketplaces instead of books, and a price
observation instead of an odds snapshot. Most of the work is the domain, not the chrome.

---

## 1. What the front end is, measured

| Area | Files | Lines | Role |
|---|---|---|---|
| `Windowing/` | 5 | 1,348 | `WindowManager`, `WindowCatalog`, `WindowShortcuts`, `FollowUpLauncher`, models |
| `Components/Windowing/` | 7 | 925 | `Desk`, `AppWindow`, `WindowBar`, `DeskHeader`, `DeskFooter`, `RailMenu`, `Glyph` |
| `Components/Desk/` | 43 | 3,926 | The primitive set: buttons, fields, grid, tabs, sheets, dialogs, toasts, metrics, chart |
| `Components/Layout/` | 4 | 309 | `MainLayout`, reconnect modal |
| `Theming/` | 3 | — | `DeskTheme` tokens and `MudTheme`, `ThemeService`, mode |
| `wwwroot/css/` | 2 | 4,096 | `lineops.css` — the whole visual system |
| `wwwroot/js/` | 4 | 616 | `desk-glide`, `windowing`, `theme`, `dialogs` |
| `Components/Panels/` | 19 | 6,467 | **Sports domain.** Board, Game, Team, Player, Journal, Ops, … |
| `Components/Snippets/` | 5 | 608 | **Sports domain.** Floating follow-ups |
| `tests/LineOps.Web.Tests` | 16 | — | bUnit render tests on a `DeskTestContext` bench |

The design is recorded in three ADRs, which the new project should adopt as written:

- [ADR 0007](../../adr/0007-window-manager-ui.md) — one route, fixed header and footer, one
  horizontal row of full-height windows, eviction never silent.
- [ADR 0013](../../adr/0013-the-board-and-the-floating-layer.md) — a board of best numbers
  with a spread rail, and a floating layer for comparison that is never modal.
- [ADR 0016](../../adr/0016-apple-feel-design-system.md) — weight over hue, materials over
  moulding, sheets and alerts as the modal tier, one accent.

### 1.1 Where the domain leaks into the chrome

Only five files under the infrastructure directories reference the sports domain:

| File | What it references | Treatment |
|---|---|---|
| `Windowing/WindowCatalog.cs` | every panel type, the workspace list | **App-owned by design.** Each app writes its own. |
| `Windowing/WindowShortcuts.cs` | catalogue keys (`Team`, `Player`, `Game`, …) | **App-owned.** Each app writes its own. |
| `Components/Desk/PriceCell.razor` | `BestOffer` from `LineOps.Data.CrossReference` | Sports. Becomes a `PriceCell` over a ticket offer, or is dropped. |
| `Components/Desk/PullMenu.razor` | `IngestionJobs` | Generic in shape; the job list is data. Keep, point at the new `IngestionJobs`. |
| `Components/Desk/RunbookSteps.razor` | `LineOps.Reliability.Runbook` | Reliability is being carried over whole, so this comes with it. |

Inside the windowing core itself the coupling is three lines and two brand marks:

- `WindowManager.cs` calls `WindowCatalog.Find(key)` once; `WindowModels.cs` names
  `WindowCatalog.Ops` once as the default primary; `WindowShortcuts.cs` is entirely
  catalogue lookups. The catalogue is a static class.
- The wordmark `LINE<em>OPS</em>` is hard-coded in `Desk.razor` (empty desk) and
  `RailMenu.razor` (brand trigger). `lineops.css` contains the string once. The theme id is
  `apple-dark`, not a product name.

Everything else in `Windowing/`, `Components/Windowing/`, `Components/Desk/` (40 of 43 files),
`Layout/`, `Theming/`, the CSS and the JS is product-agnostic and moves as-is.

### 1.2 What the tests give you

`DeskTestContext` registers MudBlazor services and `ThemeService` and puts JS interop in
loose mode; every desk render test sits on it. Fifteen of the sixteen test files are about
primitives and windowing (`WindowBarTests`, `FollowUpLauncherTests`, `DeskCapacityTests`,
`DeskSheetTests`, …) and move unchanged. `PriceCellTests` is sports and goes with `PriceCell`.

---

## 2. What else transfers from the back end

The split into `Core / Data / Ingestion / Reliability / Observability / Web / Worker` is the
right shape for a second scraper-and-history product, and more of it is generic than the
names suggest.

**Generic, carry over whole**

- `LineOps.Reliability` — touches only `IngestionRuns`, `Sources`, `Alerts`, `Incidents`,
  `KpiDailies`. Freshness, success rate, volume anomaly, budget burn, the alert engine,
  incidents that cannot close without a root cause, the runbook. Nothing sports in it.
- `LineOps.Observability` — OpenTelemetry wiring.
- Ingestion services with no domain reference: `IngestionScheduler` (state-driven, no fixed
  hours), `CreditBudgetGuard` + `BudgetCalculator` (a source declares per-hour, per-day or
  monthly limits; the guard refuses a run rather than overrun), `BackfillCoordinator`,
  `SourceRegistry`, `OddsFeedStatus` (rename), `OddsRetentionService` (the promote-then-prune
  pattern), `ReferenceReconciler` (enabled flags follow configuration).
- `Core/Entities/Operations.cs` and the `Source` half of `Reference.cs` — runs, alerts,
  incidents, KPI dailies, sources with rate limits and a failure-injection mode.
- `Core/Contracts/ISourceAdapter.cs` — the adapter contract shape: a `Key`, a fetch that
  returns canonical records plus a `FetchCost`.
- `Data`: `DatabaseInitializer` (migrate, partitions, seed, self-heal on start), the
  `lineops_ensure_odds_partition` monthly-partition function (rename), `PostgresFixture`
  with Testcontainers for integration tests.
- The whole operations shell: `docker-compose.yml` hardening, `compose.dev.yml`, `.env`
  + `scripts/setup.ps1`, `publish-data.ps1` / `restore-data.ps1` (the snapshot workflow),
  the CI workflow (build, `dotnet format --verify-no-changes`, test, EF pending-model check),
  `.gitattributes`, `CLAUDE.md` working rules.

**Pattern transfers, code does not**

- `OddsIngestionService` — store-on-change (a price that has not moved is not written),
  resolve-then-persist, the budget reservation before the fetch. Rewrite for prices.
- `EntityResolver` — one entity across sources by `ExternalIds` jsonb, fast path on the
  provider's id, slow path on identity plus a drift window. Rewrite for events; keep the two
  lessons this week taught: the window is one fixture's drift, not a day, and only the
  schedule authority may move a start time.
- ADR 0010's "scans until the event, then one close" — for tickets the analogue is
  "observations until the event starts, then one *final price* row", and the same retention
  policy prunes the stream afterwards.
- `SettlementService` / CLV — "what I paid versus what it closed at" becomes "what I paid
  versus the price the day of the event", the same join, the same one-line analytic.

**Sports, leave behind**

`Game`, `Team`, `Player`, `Sport`, `SeasonCalendar`, `OddsMath`, `Grading`, the ESPN, The
Odds API, odds-api.io and BALLDONTLIE adapters, `HistoryBackfillService`, `LinePollPlanner`
(its cadence-from-allowance idea returns in §4), `SettlementService`, every panel and snippet.

---

## 3. Where ticket prices can come from

Researched 2026-09-11. Verify terms before shipping; each portal's terms govern caching,
display and attribution.

| Source | Access | What you get | Fit |
|---|---|---|---|
| **SeatGeek Platform API** | Free `client_id` from seatgeek.com/account/develop, passed as a query parameter. No rate limit published. | `/events` with search, performer, venue, date and taxonomy filters; each event carries a `stats` block: `lowest_price`, `average_price`, `highest_price`, `listing_count`. **No per-listing or per-section prices** — the FAQ says there are no plans to expose them. | **Primary.** Exactly the "one number per source per observation" the board wants. |
| **Ticketmaster Discovery API v2** | Free key from developer.ticketmaster.com. **5,000 calls/day, 5 requests/second.** | Event search; each event carries `priceRanges` (type `standard`, currency, `min`, `max`) for US, CA, AU, NZ, MX; max is capped at 2,000 with `listingsExtendBeyondMax`. Primary-market prices, so a different signal from the resale sites. Note the Discovery *Feed* (bulk file) dropped price fields on 2025-03-11; the API still returns them. | **Primary.** The quota maps straight onto `Source.RateLimitPerDay`. |
| **StubHub API** | By application: affiliates@stubhub.com for buyer-side apps. Catalog and Sales APIs. | Resale listings and prices once approved. | **Phase 2.** Write the adapter against the contract; register it when a key arrives, the way LineOps handles an odds key. |
| **Vivid Seats, TickPick** | No public API. Only third-party scrapers (Apify and the like). | Per-listing prices with section and row. | **Out of scope initially.** Their user agreements are arbitration-bound and scraping is a terms question the project should not start on. The adapter contract leaves the door open. |

Two consequences for the design:

- The unit of observation is **an event's price summary per source** (lowest, average,
  highest, listing count), not a listing. That keeps the board honest across sources that
  will never give listings, and it is what makes store-on-change cheap.
- Primary (Ticketmaster) and resale (SeatGeek, StubHub) prices are different markets. The
  board should say which it is showing, the way LineOps says "closed" versus live — and never
  average them together, per the reasoning in ADR 0011.

---

## 4. The ticket domain, mapped onto LineOps

| LineOps | Ticket tracker | Notes |
|---|---|---|
| `Sport` | `Category` (concert, sport, theatre — SeatGeek taxonomy ids) | `Enabled` gates the pickers, reconciled from config on start. |
| `Team` / `Player` | `Performer` | Artist, team, production. `ExternalIds` jsonb per source. |
| — | `Venue` | New. Name, city, capacity; `ExternalIds`. |
| `Game` | `Event` | `PerformerId`, `VenueId`, `StartsAt`, `Status` (scheduled, postponed, cancelled, past), `ExternalIds`. |
| `Source` (book / stats) | `Source` (marketplace), `Kind` = `Primary` / `Resale` | Rate limits and monthly budgets as now. |
| `OddsSnapshot` (partitioned, append-only) | `PriceObservation` (partitioned by month, append-only) | `EventId`, `SourceId`, `ObservedAt`, `Currency`, `Lowest`, `Average`, `Highest`, `ListingCount`, `IngestionRunId`. Written only on change. |
| `ClosingLine` | `FinalPrice` | One row per event per source, promoted at event start; the stream is pruned after. |
| `JournalEntry` (a wager) | `Purchase` | What you paid, where, when, how many. |
| CLV | Paid-versus-final | `Purchase` joined to `FinalPrice`; positive means you beat the day-of price. |
| — | `Watch` | New. An event the operator is tracking, with an optional target price and a notify-on-drop flag. Watches are what the scheduler polls; nothing is fetched unasked (the LineOps rule). |
| `Alert` rules: freshness, success rate, volume, budget | Same four, plus `price_drop` and `target_reached` per watch | The engine is untouched; two rules are added. |

The **cadence** comes from `LinePollPlanner`'s idea rather than its code: what is left of a
source's daily allowance, minus a reserve, divided by the cost of one sweep over the
watchlist, spread over the hours remaining. Ticketmaster's 5,000/day makes this concrete:
40 watched events at one request each is 200 sweeps a day, or one every seven minutes, and
the planner should say so on the Ops window as LineOps does.

The **partition** function is the same SQL with a new name; the retention service is the
same code with `OddsSnapshots` renamed.

---

## 5. The window catalogue for the tracker

Every LineOps window has a direct counterpart; the catalogue groups are unchanged.

| Group | LineOps | Tracker | What changes |
|---|---|---|---|
| Data | Board | **Watchlist** | Best current price per watched event, the source holding it, and a spread rail with one tick per source. Primary and resale badged, never merged. |
| Data | Line movement | **Price history** | The chart: lowest price per source over time, event start marked, purchases marked. |
| Data | Players | **Performers** | Search and pick; opens Performer. |
| Analytics | Journal | **Purchases** | What was paid; paid-versus-final resolves on its own after the event. |
| Analytics | Performance | **Savings** | Total paid versus day-of prices, by source and category. |
| Operations | Ops, Incidents, Runs, History | Unchanged | Reliability is carried over whole. History becomes the backfill of past events' final prices where a source offers it. |
| System | Window manager, Parts bin | Unchanged | |
| Destinations | Game / Team / Player / H2H | **Event / Performer / Venue** | `RequiresSubject`, opened by following a name. |
| Follow-ups | All bets / Place wager / Form | **All sources / Log purchase / Trend** | Same floating layer, same `FollowUpLauncher`. |

The board-to-purchase workflow is the LineOps one: click a row, three actions appear, each
opens a window while there is room and floats when there is not.

---

## 6. The plan

### Phase 0 — Bootstrap (half a day)

1. New repository, `TicketOps` (or your name). Copy `Directory.Packages.props`, `.gitattributes`,
   `.gitignore`, `.editorconfig`, `docker-compose.yml`, `compose.dev.yml`, `.env.example`,
   `scripts/`, `.github/workflows/ci.yml`, `CLAUDE.md`. Rename `lineops` in compose and
   scripts (container name, database, user, partition function).
2. Solution with the same seven projects, renamed. Keep the `Directory.Packages.props`
   central versioning; MudBlazor stays at 9.7.0 so the desk CSS matches.

### Phase 1 — Lift the desk (2 days)

Copy, do not extract yet (see §7 for why):

1. `Windowing/` minus `WindowCatalog.cs` and `WindowShortcuts.cs`; `Components/Windowing/`;
   `Components/Desk/` minus `PriceCell.razor`, `PullMenu.razor`, `RunbookSteps.razor` (the
   last two return in Phase 3 once ingestion and reliability exist); `Components/Layout/`;
   `Theming/`; `wwwroot/css/lineops.css` (rename), `wwwroot/js/`.
2. Make the two brand marks a parameter: a `DeskBrand` record (wordmark, tagline) registered
   in DI and read by `Desk.razor` and `RailMenu.razor`.
3. Write the new `WindowCatalog` with the §5 entries pointing at placeholder panels, and a
   `WindowShortcuts` for Event / Performer / Venue. `WindowModels.cs` line 120 names the
   default primary window; point it at Watchlist.
4. Copy `tests/LineOps.Web.Tests` minus `PriceCellTests`; the bench and fifteen files should
   pass on the first build. This is the acceptance test for the lift.
5. Copy ADRs 0007, 0013, 0016 into the new `docs/adr` with a note that they were adopted.

### Phase 2 — Domain and the first two sources (1 week)

1. Entities per §4; `DbContext` with the partitioned `PriceObservations` and the monthly
   partition function; `DatabaseInitializer` seeding categories and the sources with their
   published limits (Ticketmaster 5,000/day and 5/s; SeatGeek unlimited-unknown, give it a
   conservative per-hour ceiling of your own).
2. `IPriceSource` contract: `FetchEventsAsync(query)` for discovery and
   `FetchPricesAsync(IReadOnlyList<ExternalEventRef>)` returning canonical observations plus
   `FetchCost`. Same shape as `IOddsSource`.
3. Adapters: SeatGeek (`/events?performers.slug=…` and `/events/{id}`, read `stats`),
   Ticketmaster (`/discovery/v2/events` with `keyword`/`attractionId`, read `priceRanges`).
   Both behind `HttpClient` with the retry, circuit-breaker and per-attempt timeout policies
   copied from `IngestionServiceCollectionExtensions`, and a User-Agent set explicitly (the
   ESPN outage in LineOps was an absent User-Agent).
4. `EventResolver`: fast path on `ExternalIds[source]`; slow path on performer + venue +
   start within six hours; only the source that created the event may move its start.
5. `PriceIngestionService`: reserve budget, fetch for the watchlist, resolve, write only
   observations whose lowest, average, highest or listing count changed.
6. Integration tests on the Postgres fixture for: store-on-change, resolver drift, budget
   refusal. Adapter parse tests from saved JSON fixtures, as `tests/LineOps.Tests/Adapters`
   does.

### Phase 3 — Operations and the scheduler (3 days)

1. Carry `Reliability` and `Observability` over unchanged; add the `price_drop` and
   `target_reached` rules to the alert engine.
2. `IngestionScheduler` as-is; `PricePollPlanner` from `LinePollPlanner`'s cadence rule;
   `RetentionService` promoting `FinalPrice` at event start and pruning after;
   `ReferenceReconciler` as-is.
3. `IngestionJobs` with named jobs: `watchlist:prices`, `events:discover`, `prices:finalise`.
   `PullMenu` and `RunbookSteps` return from LineOps now that their dependencies exist.
4. Ops, Incidents, Runs windows are the LineOps panels with the source names changed.

### Phase 4 — The tracker's own windows (1 to 2 weeks)

Watchlist board (a `BoardService` over `PriceObservations` with the same best-per-market
composition and spread rail, one "market" per source kind), Price history chart (`DeskChart`
already exists), Performers, Event / Performer / Venue destinations, Purchases and Savings,
the three follow-ups. Reuse `BoardPanel`'s structure — filter row, metric strip, grid, row
actions — and delete what does not apply rather than starting from a blank panel.

### Phase 5 — Extract the shared desk (only when it earns it)

When both products are stable and a fix has had to be made twice, move
`Windowing/` + `Components/Windowing/` + `Components/Desk/` + `Layout/` + `Theming/` + CSS +
JS into a Razor Class Library, `Desk`, published as a NuGet package from GitHub Packages, with
an `IWindowCatalog` interface replacing the static class and `DeskBrand` as a required
service. Both apps then take it as a package reference. Until then, two copies and a
`docs/desk-origin.md` naming the commit the copy was taken from.

---

## 7. Decisions and risks

**Copy first, extract later.** A shared library across two repositories means a package
feed, versioning and a release step on every desk tweak, for one developer. The desk changed
this week (the strip became drawers). Copying costs one afternoon now and an afternoon of
diffing later; extracting costs a week now and a tax on every change until both products
settle. Phase 5 is written down so it happens deliberately when a third consumer or a
second duplicated fix makes the case.

**Observations, not listings.** SeatGeek will not give listings and Ticketmaster gives a
range, so the board's unit is the per-source summary. Per-section tracking would need StubHub
approval or scraping, and the plan does not depend on either.

**Two markets on one board.** Primary and resale prices for the same seat are different
products. Badge them; never average them. This is ADR 0011's point restated.

**Terms and quotas.** Ticketmaster's 5,000/day is the binding constraint on how many events
can be watched; the budget guard refuses rather than overruns, as in LineOps. Check each
portal's terms on caching and display before the first public deploy; keep source attribution
on every price cell.

**What LineOps taught this week that the new project should start with:** a matching window is
one fixture's drift, not a calendar day; only the schedule authority moves a start time; a
reference number from a stats-style source must never enter a market ranking; a results sweep
must back off a day it cannot heal; the enabled flags on reference rows are the configuration's
to write. All five are in commits `dc08aa5`..`243a409` and each has a test.

---

## Sources

- [SeatGeek Platform API documentation](https://seatgeek.github.io/) — authentication, `/events`, `stats` fields, no per-listing data.
- [Ticketmaster Discovery API v2](https://developer.ticketmaster.com/products-and-docs/apis/discovery-api/v2/) — 5,000 calls/day, 5 requests/second, `priceRanges`.
- [Ticketmaster Discovery Feed release notes](https://developer.ticketmaster.com/products-and-docs/apis/discovery-feed/release-notes/) — price fields removed from the feed on 2025-03-11.
- [StubHub API introduction](https://developer.stubhub.com/docs/overview/introduction/) — access by application.
- [TickPick user agreement](https://www.tickpick.com/terms/user-agreement/) — arbitration terms; no public API.
