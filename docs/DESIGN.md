# LineOps — Sports-Data Ingestion & Analytics Operations Platform

**Status: BUILT.** All seven phases implemented and verified. 460 tests passing (unit + adapter-fixture + bUnit component + Testcontainers integration), `dotnet format` clean, full stack running in Docker over HTTPS. Phase 7 (*operate*) is the ongoing part.

**Stack:** .NET 10 · Blazor Web App (Interactive Server) · MudBlazor 9.7 · PostgreSQL 17 · Docker
**Cost:** $0 for the v1 scope (see §0) — all sources on permanent free tiers, hosted locally.
**v1 scope:** Games, players, and stats for NFL / NBA / MLB / NHL; mainstream markets only (moneyline, spread, totals). Player props and exotic markets are the designed extension path, not v1.
**Repository:** `C:\Deploy\LineOps` — see §8 for build and run steps.

**Purpose (public):** An operations platform that ingests sports statistics and market line data daily from multiple external sources, stores line-movement history as time-series, computes performance analytics (closing-line value, ROI, bankroll), and monitors its own ingestion health with KPIs, alerting, and an incident log.

**Purpose (career):** Plug the one real gap on the resume — on-call/KPI/RCA operational reliability — while doubling down on the Blazor/MudBlazor/.NET/Postgres headline stack, for the Starbucks Application Developer and Senior Application Developer roles.

> §11 lists every place the built system diverged from this design and why. Those divergences are the most interesting interview material in the document, because each one came from hitting a real constraint.

---

## 0. Cost model — verified against current pricing (July 2026)

**The v1 scope runs at $0.** Verified, with the honest boundaries stated.

| Component | Source / plan | What's free | Verified limit |
|---|---|---|---|
| Odds (primary) | **odds-api.io** free tier | Moneyline, spreads, totals · all 34 sports incl. NFL/NBA/MLB/NHL · live + pre-match · 2 recreational books (e.g., DraftKings, FanDuel) | 100 req/h, 500 req/day, forever, no card |
| Odds (secondary/reconciliation) | **The Odds API** Starter | All sports, books, and markets — but billed in credits (markets × regions per call) | 500 credits/mo — treat as a small reconciliation budget, never the primary feed |
| Players & stats | **balldontlie** free tier | NBA, NFL, MLB, EPL — teams, players, games, stats | Free tier with rate limits; NHL stats fall to ESPN |
| Scores/schedules/stats backfill | **ESPN undocumented JSON** | Scoreboards, schedules, box scores, athletes across 20+ sports | Free, no auth — but unofficial and can change without notice |
| Offline fixtures | **built-in demo sources** | Full slate with drifting prices, rosters, box scores | $0, no network — see §11 |
| Database | PostgreSQL 17 in Docker | Everything | $0, local |
| Hosting | Your own machine (Docker Compose) | Web + worker + DB | $0 — the daily poll cadence doesn't need cloud hosting |
| CI | GitHub Actions, public repo | Build/test minutes | Free for public repos |

**Budget math for the daily cadence:** a full slate poll costs **2 requests per sport** (one `/events`, one `/odds/multi` batching every event), so four sports is 8 requests. With intraday movement polls the day lands in the low hundreds — inside odds-api.io's 500/day on its own, before touching the second source. `CreditBudgetGuard` (§4) enforces this rather than trusting it: a run that would breach an hourly, daily, or monthly ceiling is **refused and recorded as `Partial`**, not attempted.

**The three honest caveats:**
1. **$0 holds for v1 scope, not every conceivable bet.** Free odds tiers cover moneyline/spread/totals — exactly the "no obscure bets yet" scope. **Player props at meaningful volume are the first thing that costs money** (The Odds API's 500 credits/mo technically include props but evaporate in days). Props are therefore the documented upgrade path (§13), not a v1 promise.
2. **Free tiers are "development and testing" use** per odds-api.io's terms. A personal portfolio/analytics project fits; reselling the data or going commercial would not.
3. **ESPN endpoints are unofficial** and can break without notice. In this project that's a controlled risk by design — breakage feeds the incident/RCA loop (§5) — and every ESPN-sourced capability has a fallback or is non-critical.

**Depth limitation to state plainly:** at 2 books, "line shopping" is a comparison, not a market scan. That's fine for v1 — CLV, ROI, and bankroll analytics don't need book breadth, they need *time depth*, which is free. More books is a paid upgrade, listed in §13.

---

## 1. Positioning & framing rules (non-negotiable)

The public identity of this project is a **data-ingestion and analytics operations platform**. It is never described as a betting tool.

**Naming & language rules:**
- Repo name/tagline: "LineOps — multi-source sports data ingestion & analytics operations platform." Never "betting app," "wagering," "picks," or "gambling."
- The journal feature is a **bet journal**: it *records* wagers the user placed elsewhere (stake, book, line, result) for performance analytics. The app has zero integration with any sportsbook's wagering flow, no deposit/withdrawal concepts, no "place bet" button anywhere in the UI or code. UI verbs: "Log entry," "Record result" — never "Place bet."
- README carries one plain sentence: *"LineOps does not place wagers and does not integrate with any wagering system."* That sentence is the conflict-of-interest firewall — it makes the non-wagering scope explicit to any employer (including a regulated-gaming employer like Aristocrat) reading the repo. **As built:** it appears in the README *and* as a standing note at the top of the Journal window, so the scope is stated in the product, not just the docs.
- Interview/resume framing leads with the *operations* story: multi-source ingestion, time-series modeling, KPIs/SLOs, alerting, incidents/RCAs. The analytics (CLV, ROI) are the domain payload — and they're the stronger engineering story anyway: CLV requires storing full line history and joining a journal entry to the market state at two points in time, which is a real data problem; a "place bet" button would just be a POST.
- The `JournalEntry` entity's own XML doc says it: *"this entity is a ledger row, never an instruction."* Framing enforced in code, not just prose.

**Why this framing wins twice:**
1. It removes any conflict-of-interest read for someone employed in regulated gaming.
2. It's the more impressive system: closing-line value, ROI curves, and bankroll analytics demonstrate time-series modeling and analytical SQL; wager placement would demonstrate nothing (and is legally impossible via public APIs anyway).

**COTS extension — explicitly out of scope.** The Senior req's "extending commercial off-the-shelf applications" bullet is the one qualification no side project can convincingly fake, so this project doesn't try. That bullet is handled in interviews with real work: extending MudBlazor's component library in the Aristocrat platform migration, administering/extending Jenkins, IIS deployment ownership, and Okta integration. Nothing in this spec should be contorted to look COTS-ish.

---

## 2. Architecture (as built)

```
┌──────────────────────────────────────────────────────────────────┐
│ LineOps.slnx                                                     │
│                                                                  │
│  LineOps.Web          Blazor Web App, global Interactive Server   │
│    ├─ Windowing/  WindowManager · WindowCatalog (the desk)        │
│    ├─ Windowing/  Desk · AppWindow · Rail · RailMenu             │
│    ├─ Panels/     Dashboard · Odds · Players · Journal ·          │
│    │              Performance · Ops · Incidents · Runs            │
│    │              (ordinary components; know nothing of windows)  │
│    └─ wwwroot/js/windowing.js — drag/resize, browser-side (§6)    │
│                                                                  │
│  LineOps.Worker       Worker-SDK host (OPTIONAL, own container)   │
│    └─ Program.cs + appsettings only; composes the library below   │
│                                                                  │
│  LineOps.Ingestion    LIBRARY — adapters, scheduler, settlement   │
│    ├─ IngestionScheduler   (BackgroundService, daily + intraday)  │
│    ├─ OddsIngestionService · StatsIngestionService                │
│    ├─ SettlementService    (grading + CLV resolution)            │
│    ├─ EntityResolver       (cross-source id/name matching)        │
│    ├─ CreditBudgetGuard    (refuses runs over free-tier ceiling)  │
│    └─ Adapters: IOddsSource / IStatsSource / IFailureInjectable   │
│         OddsApiIoAdapter · TheOddsApiAdapter                      │
│         EspnStatsAdapter  · DemoOddsSource · DemoStatsSource      │
│                                                                  │
│  LineOps.Reliability  Shared reliability library (§5)             │
│    ├─ KpiCalculator · AlertEngine · IncidentService               │
│    └─ ReliabilityEvaluator (BackgroundService)                    │
│                                                                  │
│  LineOps.Core         Domain entities · OddsMath · Grading ·      │
│                       PerformanceAnalytics  (no dependencies)     │
│                                                                  │
│  LineOps.Data         EF Core 10 + Npgsql · migrations ·          │
│                       partition maintenance · seeding             │
│                                                                  │
│  PostgreSQL 17        Docker, not published to the host (§7)      │
└──────────────────────────────────────────────────────────────────┘
```

**Key decisions and their rationale (these are interview talking points):**

- **Blazor Web App with global Interactive Server render mode** — MudBlazor doesn't support static SSR and doesn't officially target .NET 10 yet; global interactivity is the known-good configuration. Knowing a framework's rendering-model constraints is exactly the "advanced platform experience" bullet. → [ADR 0004]
- **`LineOps.Ingestion` is a library; `LineOps.Worker` is the host.** Originally one Worker-SDK project. Containerising it produced `NETSDK1152` — two executables in one publish output, each with its own `appsettings.json`. The error was the symptom; the real problem was that a web app referencing another app's *executable* project is a layering smell, and whichever config file won would have been nondeterministic. Splitting by role fixed both. → [ADR 0005]
- **Web and Worker are separate processes** sharing Core/Data/Reliability. The UI never blocks on ingestion; ingestion failures degrade freshness, not availability. Topology is a compose profile and a startup registration, not a code change — both hosts call the same `AddLineOpsIngestionScheduler()`.
- **Adapter pattern per external source** behind `IOddsSource`/`IStatsSource`. Sources differ in auth, rate limits, schemas, and billing (The Odds API bills credits = markets × regions, not per request — a genuine reconciliation problem). Normalisation to canonical records happens at the adapter boundary, so nothing above it knows a provider's shape.
- **Defensive parsing by design.** Adapters read every field through alias fallbacks (`id`/`eventId`/`_id`, prices as number *or* string, teams as string *or* nested `{name}`) and return zero rows on an unrecognised shape rather than throwing. Schema drift therefore surfaces as a *volume anomaly* in the KPIs — a warn — instead of a crash. → §5, [ADR 0003]
- **Three integration styles, deliberately:** synchronous REST pulls (adapters), scheduled bulk slate ingest (batched `/odds/multi`, one call per sport regardless of game count), and post-final settlement processing that joins the journal against the time-series. Matches the sync/async/bulk transport bullet in the Senior JD. *(The original design proposed `System.Threading.Channels` between fetch and persist; see §11 for why that was dropped.)*

---

## 3. Data model (PostgreSQL, as built)

The centerpiece is **odds as append-only time-series snapshots** — odds change continuously, so the model stores observations, never "current values."

```sql
-- Reference
sport(id, key unique, name, enabled)
team(id, sport_id, name, abbrev, external_ids jsonb)
player(id, sport_id, team_id null, full_name, position,
       status, external_ids jsonb)            -- team_id null = free agent / unassigned
source(id, key unique, name, kind, base_url,  -- kind: Odds|Stats
       enabled, rate_limit_per_hour null,
       rate_limit_per_day null,
       monthly_credit_budget null,
       failure_mode null)                     -- dev-only injection, persisted (§5)
game(id, sport_id, home_team_id, away_team_id,
     starts_at timestamptz, status,           -- Scheduled|Live|Final|Postponed
     home_score null, away_score null,
     external_ids jsonb)                      -- id per source; matching problem lives here

-- Time-series core (append-only, never updated)
odds_snapshot(
     id bigint generated by default as identity,
     captured_at timestamptz not null,
     game_id, source_id, book text,
     market text,                             -- v1: h2h|spread|total; TEXT not enum, so
                                              -- props/futures slot in with no migration
     outcome text, line numeric(10,2) null,
     price_american int,
     player_id null,                          -- null for v1 team markets; ready for props
     ingestion_run_id,
     PRIMARY KEY (captured_at, id)            -- partition key must lead the PK
)  PARTITION BY RANGE (captured_at);
   -- monthly partitions + a DEFAULT partition so no row can ever be rejected;
   -- lineops_ensure_odds_partition(timestamptz) creates them on demand
   -- ix (game_id, market, book, captured_at)
   -- ux (game_id, source_id, book, market, outcome, captured_at)

stat_snapshot(id, game_id, source_id, payload jsonb,
     captured_at, ingestion_run_id)

player_game_stat(id, player_id, game_id, source_id,
     stat_line jsonb,                         -- pts/reb/ast, pass yds, SOG… per sport
     captured_at, ingestion_run_id)
  -- unique (player_id, game_id, source_id) — upsert on backfill

-- Bet journal (records only — no wagering)
journal_entry(id, note null, game_id null, market, outcome,
     player_id null,
     free_text_market null,                   -- log ANY bet today (props, parlay legs,
                                              -- futures) before a feed covers it;
                                              -- CLV stays null until one does
     book, line_taken numeric(10,2) null, price_taken int,
     stake numeric(12,2), placed_at timestamptz,
     result text,                             -- Pending|Win|Loss|Push|Void
     payout numeric(12,2) null,
     parlay_group_id uuid null,               -- groups legs of one parlay
     -- CLV, held as plain columns rather than an FK — see ADR 0002
     closing_snapshot_id bigint null,
     closing_captured_at timestamptz null,    -- partition key, so lookups prune
     closing_price int null)                  -- denormalised: CLV survives pruning

-- Operations (§5)
ingestion_run(id, source_id, job_key,         -- odds:slate | odds:movement | stats:boxscore…
     started_at, finished_at null,
     status,                                  -- Running|Success|Partial|Failed
     rows_ingested int, requests_made int,
     credits_spent int, error text null)
kpi_daily(day date, source_id, freshness_minutes,
     success_rate, rows_ingested, run_count,
     requests_made, api_credits_used,
     PRIMARY KEY (day, source_id))
alert(id, rule_key, source_id null, severity,  -- Info|Warn|Critical
     message, triggered_at, resolved_at null, incident_id null)
incident(id, title, severity, status,          -- Open|Mitigated|Resolved
     opened_at, resolved_at null,
     timeline jsonb,                           -- [{at, note}]
     root_cause null, corrective_actions null) -- the RCA
```

**Design points worth defending in an interview:**

- **Append-only + native monthly partitioning** — the same discipline as the 2TB partitioned-Postgres migration on the resume, at hobby scale. "Current line" is a query (`newest row per game/book/market/outcome`), never a mutable column. Old months detach or drop as a metadata operation instead of a mass `DELETE`. → [ADR 0001]
- **The partition key forced a real trade-off.** A unique constraint on a partitioned table must include the partition key, so the PK is `(captured_at, id)` — which means `odds_snapshot` **cannot be a foreign-key target**. The closing-snapshot link became three plain columns. That turned out *better*: denormalising `closing_price` means **CLV survives dropping old partitions**, where an FK would have silently lost it. → [ADR 0002]
- **`external_ids jsonb`** on game/team/player: cross-source entity resolution (ESPN's game id ≠ odds-api's id) is a genuine reconciliation problem. `EntityResolver` tries the provider's own id first (exact, cheap), falls back to normalised team names within a ±24h window, then records the discovered id so the next run takes the fast path.
- **CLV materialisation:** when a game finishes, `SettlementService` finds the last pre-start snapshot matching the entry's book + market + outcome, falling back to *any* book if the entry's own book was never tracked (a cross-book close is weaker than a same-book one but far better than no CLV). This join across the journal and the time-series is the single best technical talking point in the app.
- **`market` as text, `player_id` nullable, stat lines as jsonb** — chosen so props, futures and new sports are additive. Designing v1 so v2 needs no migration *is* the "scalable, durable" bullet.

---

## 4. Ingestion (as built)

`IngestionScheduler` is a `BackgroundService` in the `LineOps.Ingestion` library, hosted by the web app by default or by `LineOps.Worker` in its own container.

**Schedule (free-tier friendly):**
- **Daily 09:00 local** — bulk slate ingest, then yesterday's box scores, then a settlement pass.
- **Intraday every 3 h** — movement snapshots, but only if a game actually starts within 36 h. No games close to starting means no requests spent.
- **Post-final** — score settlement, grading, CLV resolution.

The loop ticks every minute and asks *what is due*, rather than sleeping until the next job. A restart therefore never skips a window, and the schedule survives clock drift. Any exception inside a tick is logged and the loop continues — a crashed scheduler would take the whole platform silent, which is worse than one failed run.

**Pipeline per run:** fetch (adapter) → normalise to canonical records → resolve entities → **diff against last known price** → persist → record `ingestion_run` → KPIs derive from that table.

**Store-on-change, not store-on-poll.** *(New during build — the most consequential design change; see §11.)* Before insert, each price is compared with the newest stored observation for its `(game, book, market, outcome)` key. Unchanged prices are skipped. This:
- makes re-running any window **idempotent by construction** — no unique-constraint collisions to catch,
- cuts storage by roughly an order of magnitude versus one row per poll,
- and means every stored row is a *real line move*, so movement charts show signal instead of sampling noise.

**Resilience:**
- `Microsoft.Extensions.Http.Resilience` (Polly v8) standard handler per HTTP source: retry with jittered backoff, circuit breaker on sustained failure, per-attempt timeouts.
- `CreditBudgetGuard` checks hourly requests, daily requests, and monthly credits **before** each run and refuses rather than overrun. Providers meter differently, so adapters report their true cost — The Odds API's is read from its own `x-requests-last` response header rather than estimated.
- Every run writes an `ingestion_run` row, success or failure. That table is the sole raw feed for the reliability layer; a run that vanished would blind the KPIs.

**Sources (v1 roster, all free — see §0):**
- **odds-api.io** — primary odds: moneyline/spread/totals, NFL/NBA/MLB/NHL, 2 books. Two calls per sport (`/events` then `/odds/multi`).
- **The Odds API** — secondary, for cross-source reconciliation only; hard 500-credit/mo budget.
- **ESPN undocumented JSON** — schedules, scores, box scores, NHL gap-fill. No auth. Verified pulling real rosters in testing.
- **balldontlie** — players/teams/games/stats (registered; enable with a key).
- **Demo odds + stats fixtures** — deterministic offline sources so a cold clone runs with no keys (§11).

The daily bulk ingest includes a **player/stats pass**: roster upsert by external id (players move teams mid-season, so team is refreshed every sync) and post-final box scores into `player_game_stat`.

---

## 5. Reliability layer — the reason this project exists

`LineOps.Reliability`, referenced by both hosts. **Framing:** "I extracted the reliability concerns into a shared library" — the Senior JD's "improve patterns/standards/shared libraries" bullet, verbatim.

**KPIs** (derived from `ingestion_run`, materialised into `kpi_daily`, shown in the Ops Center):
- **Freshness** — minutes since the last *successful* run. A later failure does not reset it. SLO < 26 h.
- **Success rate** — successes / completed runs, 7-day rolling. SLO ≥ 95%. `Partial` counts as failure; `Running` is excluded.
- **Volume anomaly** — today's rows vs. trailing 7-day median, warn below 50%. Returns *null* with fewer than three days of history rather than guessing, because a false alert on day one trains you to ignore alerts.
- **Budget burn** — requests/credits against each provider's ceiling.

**Two kinds of zero.** Because prices are only written on change, a healthy run on a quiet market writes zero rows — and so does a provider silently returning `HTTP 200` with a broken payload. Conflating them means either paging on every quiet Tuesday or never noticing a real outage. Status is therefore derived from what the *provider returned*, not from what was written:

| Provider returned | Rows written | Status | Meaning |
|---|---|---|---|
| Prices | > 0 | `Success` | Normal |
| Prices | 0 | `Success` | Quiet market |
| Nothing | 0 | `Partial` | Suspicious — likely schema drift |
| Threw | 0 | `Failed` | Hard failure |

→ [ADR 0003]. This is the single best operational-thinking talking point in the project.

**Alerting:** `AlertEngine` evaluates rules on a timer and **reconciles** against open alerts — at most one open alert per `(rule, source)`, messages refreshed as the numbers drift, and auto-resolution when the condition clears without anyone clicking. A source that has never run is skipped entirely: unconfigured is not an outage.

**Incident log with RCAs:** a critical alert can be promoted manually, or `ReliabilityEvaluator` auto-opens one after a critical persists across N evaluation cycles (transient blips don't earn an incident). **`ResolveAsync` throws unless both a root cause and a corrective action are supplied** — the discipline is enforced by the API, not by good intentions, so the accumulated history is a real RCA log rather than a list of things that broke.

**On-call simulation:** per-source failure injection — `error` (503), `timeout`, and `empty` (HTTP 200, no rows — the silent one). Persisted on the `source` row rather than held in memory, so a drill survives a restart and is visible to anyone reading the config. Only development fixtures implement `IFailureInjectable`; no real-provider adapter does. This is how you honestly say "I built and operated the on-call loop" about a solo project.

**Ops Center UI:** KPI tiles with SLO status, per-source health cards, drill panel, open-alert feed with one-click incident promotion, incident list + timeline + RCA editor, filterable ingestion-run history.

**The runbook is in the tool, not beside it.** `docs/runbook.md` is embedded in `LineOps.Reliability` and rendered inside the incident that raised the rule, so triage steps are in front of whoever is working it. And the relationship is a **test**: `RunbookCoverageTests` asserts a bijection between the `AlertRules` constants and the runbook's rule headings, in both directions. This came from a real failure — `budget_pressure` shipped documented, configured, and unreachable, because the budget data lived in `LineOps.Ingestion` and the reference direction is `Ingestion → Reliability`, so the alert engine structurally could not see it. The fix split *measuring* consumption (`BudgetCalculator`, in the reliability layer) from *enforcing* it (`CreditBudgetGuard`, still in ingestion), and made the drift a red build rather than a thing to remember.

**Standard telemetry beside the bespoke layer** → [ADR 0015]. OpenTelemetry carries signals to whatever the operator already runs; the reliability layer keeps the domain judgments no generic exporter can make (two kinds of zero, unconfigured-is-not-an-outage, a median needs three days, unmetered is not zero). Instrumentation is BCL only — `ActivitySource` and `Meter` names live in `LineOps.Core.Diagnostics`, so the working libraries emit with no vendor dependency and `LineOps.Observability` is the single project that references OTel. The observable gauges publish the project's *own* KPIs (`lineops.source.freshness_minutes`, `lineops.budget.utilisation`, `lineops.incidents.awaiting_rca`), computed by the same `KpiCalculator` the Ops Center uses rather than reimplemented — which is what makes the bespoke layer legible to standard tooling without becoming a second source of truth. Gauges read a cached snapshot, never the database: a metrics callback runs on the collection path, and blocking it on I/O is how a monitoring system becomes the outage.

**Liveness and readiness are different questions.** `/health` depends on nothing, so a database outage cannot get a container killed and restarted into the same outage. `/ready` reports Postgres reachable *and* schema at the expected version — a process pointed at a database with pending migrations starts, accepts traffic, and fails every request, and no restart fixes that. Verified by stopping Postgres: `/health` stayed 200, `/ready` returned 503, and it recovered on its own.

---

## 6. Interface — a desk, not a set of pages

The UI is a **window manager**. One route hosts draggable, resizable, minimisable windows on
a shared desk, with a rail (taskbar) that docks to any of the four edges. → [ADR 0007]

Why: triaging an ingestion failure means reading source health, the incident, and the runs
behind it *together*; a page-based UI can only ever show one. Pages fought the product.

- **`WindowManager`** (scoped, one per circuit) owns placement, stacking, focus and rail
  edge, so tiling, workspaces and keyboard actions can move windows — a drag is not the only
  path. **`WindowCatalog`** is the registry: adding a window is one entry.
- **Panels are ordinary components.** They receive their window id as a cascading value and
  know nothing else about being hosted. That inversion is what makes *any* sub-page a window.
- **Workspaces** are named layouts stored as *fractions* of the desk — "Morning triage" (ops,
  incidents, runs), "Line watch" (slate beside a movement chart), "Review" (journal beside
  performance) — so a layout saved on a monitor still fits a laptop. Plus tile, columns,
  cascade.
- **Capacity is advice, not a rule.** `RecommendedCapacity` derives from the viewport
  (~420×300 per readable window); the launcher says so once exceeded. The operator decides.
- **Drag and resize run entirely in the browser.** Blazor Server routes events over a
  SignalR circuit, so a `@onpointermove` handler would put a network round trip inside every
  frame of a drag. `windowing.js` mutates the element directly at pointer rate and calls
  .NET once, on pointer-up, with the final rectangle.

**The signature — the pulse strip.** Each window's title bar carries a 2px band encoding
*that window's own* state, mirrored on its rail chip. A minimised window still reports: the
Ops chip stays amber while you work in Journal. Peripheral awareness is the only reason a
desk beats tabs, so it is the one loud element and everything around it stays quiet.

**Visual language.** Apple's HIG: a true-neutral surface ramp (`--surface-0` … `--surface-3`)
with hairlines that separate rather than outline, and **one** accent — systemBlue `#0A84FF`,
spent only on interactivity, focus and selection. Buttons state weight, not hue: Filled /
Tinted / Plain, at most one Filled per context. State colours are role-named
(`--state-positive` / `-warning` / `-negative`) and land on the *values* that report — numbers,
tags, the pulse strip — never on the controls, because an interface where every button is
coloured has no primary action. Materials go on anything that floats; anything holding data
stays opaque. Type is real SF via `-apple-system` with bundled Inter as the off-platform
fallback, one family throughout — a column of odds is that face plus `tabular-nums`, not a
second mono. → [ADR 0016]

### Panels

- **Slate** — today's games, current moneyline per game, counters, manual **Ingest now**.
- **Odds Explorer** — per-game movement chart, one series per book/outcome on a shared time axis, each carrying its last price forward (a missing point means *unchanged*, not *unknown*). Plus a current-market table with implied probability.
- **Players & Stats** — searchable roster by sport, games-logged count, and per-player game logs whose **columns are derived from the data** rather than hard-coded, since stat shapes differ per sport and source.
- **Bet Journal** — log entries, auto-graded on final score, per-entry CLV once the close resolves. Free-text market option for anything without a feed. *No wagering affordances of any kind (§1).*
- **Performance** — ROI, net profit, win rate, "beat the close" rate and average CLV, bankroll curve, breakdowns by market and by book.

Charts via MudBlazor. All maths lives in `LineOps.Core` as pure functions — `OddsMath` (American ↔ decimal ↔ implied probability, no-vig, break-even rate), `Grading`, `PerformanceAnalytics` — and is unit-tested against hand-checked values rather than against its own output.

---

## 7. Security posture (as built)

Deliberate choices, not defaults. → [ADR 0006]

| Choice | Why |
|---|---|
| **HTTPS only, port 9443** | No HTTP listener exists to downgrade to or forget. 9443 avoids the 8080/8443/3000/5000 crowd. |
| **`127.0.0.1:9443:9443`** | Loopback only. Docker's short syntax binds `0.0.0.0` and would expose the app to the whole LAN — and Docker writes its own iptables rules, so a host firewall doesn't reliably save you. |
| **Postgres not published** | The app reaches it over the compose network; no host port needed. Opt in via `compose.dev.yml` for `psql`. |
| **Cert volume-mounted read-only** | Never `COPY`ed into the image — that breaks dev/prod parity and risks shipping a private key in a layer. |
| **No credentials in the repo** | Passwords in `.env` (gitignored); `.env.example` committed. `*.pfx`/`*.p12`/`*.key`/`*.pem` ignored globally. `AddLineOpsData` **throws** rather than falling back to a hardcoded password — a fallback credential is one that eventually reaches production. |
| **Non-root · read-only rootfs · `cap_drop: ALL` · `no-new-privileges`** | The app writes only to `/keys` and `/tmp`, so everything else is immutable. Verified by probe, not assumed. |
| **SCRAM-SHA-256 at initdb** | Password auth can never negotiate down to md5 or trust. |
| **Memory limits** | A runaway query degrades one container, not the host. |

**Beyond localhost:** do not change the binding to `0.0.0.0`. Terminate TLS at a reverse proxy with a real certificate (README shows a Caddy service handling ACME automatically, proxying to `web:9443`). The dev certificate is `localhost`-only and trusted solely on the machine that generated it; Kubernetes would use a TLS secret rather than a mounted PFX.

---

## 8. Build steps

### Prerequisites

| Tool | Version used | Needed for |
|---|---|---|
| Docker Desktop | 29.6.1 (Compose v5.3.0) | Everything |
| .NET SDK | 10.0.204 | Only for host-side development and tests |
| PowerShell | 7+ | The setup script |

Docker alone is enough to *run* LineOps. The SDK is only needed to develop or run tests.

### 8.1 First-time setup

```powershell
cd C:\Deploy\LineOps

# Generates .env with strong random secrets and exports the HTTPS dev certificate
# to %USERPROFILE%\.aspnet\https\LineOps.Web.pfx. Safe to re-run; use -Force to regenerate.
./scripts/setup.ps1

# Optional but recommended — removes browser warnings.
# Deliberately not run by the script: it modifies the machine-wide trust store.
dotnet dev-certs https --trust
```

If doing it by hand instead: `cp .env.example .env`, fill in `POSTGRES_PASSWORD` and `CERT_PASSWORD`, then
`dotnet dev-certs https -ep "$env:USERPROFILE\.aspnet\https\LineOps.Web.pfx" -p <CERT_PASSWORD>`.
The PFX password must match `CERT_PASSWORD` exactly, and the file must be `.pfx` — a `.crt`/`.key` pair fails with *"server mode SSL must use a certificate with the associated private key."*

### 8.2 Run the whole stack

```powershell
docker compose up -d
```

Open **<https://localhost:9443>**.

On first start the web container waits for Postgres to report *healthy* (not merely *started* — the app migrates on boot and would otherwise race an unready server), applies migrations, creates the current and next two monthly partitions, seeds sports and sources, and runs one ingest so the dashboard isn't empty.

```powershell
docker compose logs -f web      # follow ingestion, alerts, settlement
docker compose ps               # status and published ports
docker compose down             # stop, keep the database volume
docker compose down -v          # stop and DELETE all data
```

**No API keys are required.** The demo fixtures generate a full slate with drifting prices, and the ESPN adapter needs no auth, so real schedules and box scores flow in for free.

### 8.3 Build images explicitly

```powershell
docker compose build            # both images
docker compose build web        # just the app
```

Both Dockerfiles use the **repository root** as build context (they need `Directory.Packages.props` and the referenced projects) and copy `.csproj` files before source so `restore` caches independently of code changes.

### 8.4 Run the ingestion worker as its own container

```powershell
docker compose --profile worker up -d
```

Set `Ingestion__HostScheduler=false` on the `web` service at the same time, or both processes poll the same providers and burn the free-tier budget twice.

### 8.5 Develop against the code (hot reload)

```powershell
# Publishes Postgres on 127.0.0.1:5433 — a separate overlay so an exposed
# database is opted into, not forgotten
docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres

# Supply the credential out of band; it is deliberately not in appsettings.json
$env:ConnectionStrings__LineOps = "Host=localhost;Port=5433;Database=lineops;Username=lineops;Password=<POSTGRES_PASSWORD from .env>"

dotnet run --project src/LineOps.Web      # http://localhost:5263
```

Use this loop for UI work — hot reload and a debugger beat a 30-second image rebuild per CSS tweak. Use Docker to verify: containerising is what caught the publish-conflict layering smell, the Data Protection key reset, and the LAN-exposed database.

### 8.6 Build and test from source

```powershell
dotnet restore
dotnet build                              # solution: 6 projects + tests
dotnet test                               # 460 tests
dotnet format --verify-no-changes         # CI enforces this
```

Integration tests start their own throwaway PostgreSQL via Testcontainers, so Docker must be running. They deliberately do **not** use the in-memory provider: native partitioning, `jsonb`, and `timestamptz` offset handling don't exist there, so a green suite would prove nothing about the schema that ships.

### 8.7 Database migrations

```powershell
dotnet tool install --global dotnet-ef    # once

# Point at whichever database you mean; no credential is embedded in the factory
$env:LINEOPS_CONNECTION = "Host=localhost;Port=5433;Database=lineops;Username=lineops;Password=<pw>"

dotnet ef migrations add <Name> -p src/LineOps.Data -o Migrations
dotnet ef database update -p src/LineOps.Data
dotnet ef migrations has-pending-model-changes -p src/LineOps.Data   # CI runs this
```

The app migrates itself on startup, so this is only needed when *authoring* a schema change.

**`odds_snapshot` is hand-written SQL inside the initial migration**, not scaffolded, because EF cannot express `PARTITION BY RANGE`. A new migration touching that table must preserve the partitioning, the `DEFAULT` partition, and `lineops_ensure_odds_partition`.

### 8.8 Known gotchas

| Symptom | Cause | Fix |
|---|---|---|
| `28P01 password authentication failed` | `POSTGRES_PASSWORD` only applies at **initdb**; an existing volume keeps its original password | `docker exec lineops-postgres psql -U lineops -d lineops -c "ALTER ROLE lineops WITH PASSWORD '<new>';"` — rotate rather than `down -v`, which discards data |
| Cert fails to load silently | PFX password mismatch, or `CERT_PATH` doesn't resolve | Verify the path mounts, and that `-p` matched `CERT_PASSWORD` |
| `MSB3027 … file is locked by LineOps.Web` | A running host instance holds the DLLs | Stop `dotnet run` before `dotnet build` |
| `NETSDK1152` duplicate publish files | Two executable projects in one publish graph | Already fixed by the library/host split — don't re-merge them ([ADR 0005]) |
| Browser warns on the certificate | Dev cert not trusted | `dotnet dev-certs https --trust`, then restart the browser |

---

## 9. Engineering practices

- **Repo layout:** `src/` (6 projects), `tests/`, `docs/adr/`, `docs/runbook.md`, `scripts/`, `.github/workflows/`.
- **Central package management** — `Directory.Packages.props` pins every version once, with transitive pinning on. Added after a real EF Core 10.0.4-vs-10.0.10 mismatch broke the build; this is the fix that stops it recurring.
- **Tests (460):** hand-checked odds maths; grading including every push case; adapter parsing against recorded fixtures containing the awkward real shapes (nested team objects, string prices, an unmodelled market, a malformed row); Testcontainers integration covering freshness, success rate, volume anomaly, alert reconciliation, auto-resolution, rollup idempotency, and full settlement with CLV; bUnit component tests covering the desk design system.
- **CI:** GitHub Actions — restore, build, `dotnet format --verify-no-changes`, test, plus a job that fails if the model has pending migrations.
- **Docs:** sixteen ADRs and a runbook that names each alert, its urgency, and its triage steps. Runbooks are an operations-maturity signal reviewers rarely see in a side project.

**ADR index:**

| ADR | Decision |
|---|---|
| 0001 | Odds append-only and partitioned by month |
| 0002 | The closing-snapshot reference is not a foreign key |
| 0003 | Distinguishing "no data" from "no movement" |
| 0004 | Global Interactive Server render mode |
| 0005 | Ingestion library separate from its worker host |
| 0006 | Container security posture and HTTPS |
| 0007 | A window manager instead of pages |
| 0008 | Gloss as an affordance, and a seam for MudBlazor — *superseded in part by 0016* |
| 0009 | History backfilled only from unmetered sources |
| 0010 | Odds are scans until first pitch, then one closing line |
| 0011 | ESPN is the stats port; odds come from a book market |
| 0012 | Jobs are named, and triggered by state rather than the clock |
| 0013 | The board, and the floating layer the desk reserved — *amended by 0016* |
| 0014 | A real feed, a credit budget, and what counts as a game |
| 0015 | Standard telemetry beside the bespoke reliability layer |
| 0016 | Weight replaces hue; materials replace moulding |

---

## 10. Build sequence — as built

| Phase | Exit criterion | Result |
|---|---|---|
| **1. Walking skeleton** | Snapshot lands in Postgres from a button click; MudBlazor renders | **Met** — 384 snapshots on the first click; MudBlazor 9.7 on .NET 10 confirmed |
| **2. Scheduled ingestion + 2nd source** | Two sources ingest unattended; re-runs don't duplicate | **Met** — scheduler + 4 adapters; store-on-change makes re-runs no-ops |
| **3. Reliability layer v1** | Kill a source → alert fires within one cycle | **Met** — success-rate alert fired at 44% |
| **4. Journal + analytics** | Log entry → game finals → auto-grades with CLV | **Met** — graded Win, $250 payout on +150/$100, +25% CLV |
| **5. Incidents + failure injection** | Full drill: detect → alert → incident → RCA | **Met** — incident #1 with timeline and RCA editor |
| **6. Players, stats + polish** | Players browsable; repo cold-reviewer-ready | **Met** — 92 players incl. live ESPN data; README, 6 ADRs, runbook, CI |
| **7. Operate** | ≥ 2–3 genuine incidents with written RCAs | **Ongoing** — the part that compounds |

Phases 1–3 alone cover the reliability gap; everything after compounds. The stopping point after Phase 3 was deliberately still resume-worthy.

**Phase 7 is now the whole job.** Leave it running, and when a free API breaks — it will — work the incident properly: timeline, root cause, corrective action, and a commit that references the RCA. Three real entries in that log are worth more in an interview than any amount of additional feature work.

---

## 11. Design changes discovered during build

Every one came from hitting a real constraint. These are the strongest interview material in the document, because they show judgment under a constraint rather than a plan executed on paper.

| Original design | As built | Why |
|---|---|---|
| One `LineOps.Ingestion` Worker project | Library + separate `LineOps.Worker` host | Containerising produced `NETSDK1152`; the real problem was a web app referencing another app's executable. Suppressing the error would have hidden a nondeterministic config win. → [ADR 0005] |
| `System.Threading.Channels` between fetch and persist | Direct batched persist | The channel would have been ceremony: a slate is a few hundred rows arriving in one response, so there is nothing to decouple. Claiming an async pipeline that buys nothing is worse than not having one. |
| One row per poll, dedupe by natural key | **Store-on-change** | Unchanged prices carry no information. This made re-runs idempotent *by construction*, cut storage ~10×, and made every stored row a real line move. |
| `RowsIngested == 0` means trouble | Status from what the *provider* returned | Store-on-change made "0 rows" ambiguous — quiet market vs. silent outage. Conflating them means alert fatigue or blindness. → [ADR 0003] |
| FK from `journal_entry` → `odds_snapshot` | Three plain columns | Postgres can't FK a partitioned table without the partition key. Denormalising `closing_price` turned the constraint into a benefit: CLV survives partition pruning. → [ADR 0002] |
| SharpAPI as second odds source | The Odds API | Better documented credit accounting, and it reports true spend in a response header — so the budget guard uses the provider's own number instead of an estimate. |
| Real providers only | **Demo fixture sources**, on by default | A portfolio repo must run for a cold reviewer with no keys. Deterministic offline sources make every feature — movement charts, CLV, drills — work at $0 with no signup. |
| HTTP on 8080 | HTTPS only on 9443, loopback-bound | The first pass published Postgres on `0.0.0.0` with a repo-committed password. Docker's short port syntax is the trap. → [ADR 0006] |
| Nav-drawer, one page at a time | **Window manager** — every page is a panel on one desk | Ops work is inherently multi-view: health, incident and runs are read together. Pages fought the product. → [ADR 0007] |
| MudBlazor components throughout | MudBlazor for charts only; chrome hand-built | The launcher is the primary way windows get created; it should not inherit another library's positioning and z-index rules, especially opening away from a rail that can be on any edge. |
| Connection string in `appsettings.json` | Throws if unconfigured | A fallback credential is a credential that eventually reaches production. |

Three bugs that only containerisation revealed, all worth mentioning: the publish conflict above; Data Protection keys written to a path that vanishes with the container (silently invalidating every live Blazor circuit on replacement); and a `DateTimeOffset.UtcNow.Date` that bound with the machine's local offset and was rejected by `timestamptz`.

---

## 12. Feature → resume/JD mapping

| Build artifact | JD language it evidences |
|---|---|
| Scheduled bulk ingest + batched REST adapters + settlement pass | "integration and data transport patterns (synchronous/asynchronous, bulk, loosely coupled)" |
| `LineOps.Reliability` shared library | "improve patterns/standards/**shared libraries** to reduce technical debt" |
| KPI rollups + Ops Center | "**report operational KPIs**" |
| Alert engine + failure-injection drills | "participate in **on-call support**, troubleshoot and remediate incidents" |
| Incident log + enforced RCAs + corrective-action commits | "lead **root-cause analysis**" |
| Partitioned time-series, entity resolution, CLV join | "data models, batch jobs" / advanced platform components |
| 460 tests: xUnit + fixtures + Testcontainers + GitHub Actions | "automate test coverage and support continuous build/integration" |
| 16 ADRs + runbook + README | "maintaining clear documentation for operations and users" |
| Runbook rendered in-incident + bijection test against `AlertRules` | documentation that cannot silently drift from the system it documents |
| OpenTelemetry traces/metrics → Aspire dashboard, `/health` + `/ready` | "monitoring", "supportability" — the standard tooling an ops team already runs |
| Blazor/MudBlazor Ops UI on .NET 10 | "UI components" + reinforces the resume's headline stack |
| Container hardening, HTTPS, no committed secrets | "secure, scalable, durable" outcomes |
| *(not covered — by design)* | COTS extension → answered in interview with MudBlazor extension work, Jenkins, IIS, Okta at Aristocrat |

---

## 13. Extension path (designed-in, deliberately deferred)

| Future capability | What it takes | Cost trigger |
|---|---|---|
| **Player props** | New `market` values + populate the existing `player_id`; one adapter method | First paid feature — The Odds API 20K ($30/mo) is the cheapest on-ramp |
| **More books** (true line shopping) | Config change on a paid odds tier | Paid tier |
| **More sports** (soccer, tennis, NCAA…) | New `sport` rows + adapter mappings; free tier already covers 34 sports | Free |
| **Parlay modeling** | `parlay_group_id` already exists; add combined-odds maths | Free |
| **Futures markets** | Same `market`-as-text mechanism + a season-long settlement job | Free |
| **Cloud hosting** | Containerised already; add a reverse proxy for a real cert (§7) | Free–$5/mo |
| **Notifications** (line moves to phone) | New channel on the existing alert engine | Free |

None of these change the core tables. `market` as text, nullable `player_id`, and jsonb `external_ids`/`stat_line` were chosen precisely so growth is additive — designing v1 so v2 needs no migration *is* the "scalable, durable" bullet.

---

**Resume bullet for the PROJECTS section:**

> **LineOps — Sports-Data Ingestion & Analytics Operations Platform** — .NET 10 / Blazor / MudBlazor / PostgreSQL / Docker
> Built and operate a multi-source data platform that ingests daily sports statistics and betting-market data via resilient REST integrations (retry, circuit breaking, per-provider rate/credit budgeting), stores line movement as monthly-partitioned time-series in PostgreSQL, and computes closing-line-value, ROI and bankroll analytics. Designed a reusable reliability library reporting operational KPIs (freshness, success rate, volume-anomaly detection) with automated alerting and an incident log that enforces written root-cause analyses. Containerised over HTTPS with non-root, read-only, capability-dropped services; 460 unit, fixture and Testcontainers integration tests in GitHub Actions CI.
