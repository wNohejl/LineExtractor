# Going public — deployment, product, and the platform under both

**Date:** 2026-09-27
**Status:** Plan. Phase 0 built on `LineX_Development` (2026-09-27); reassessed 2026-10-24 (§10a).
**Audience:** the developer deciding how far LineOps leaves the laptop, and what it becomes
when it does.

The desk works. It runs on one machine behind `127.0.0.1`, for one person, with no sign-in,
against data it is allowed to read for itself and not allowed to show anyone else. This
document says what changes at each step outward: a link a reviewer can open, a public site,
a product someone pays for, and a platform that other ideas are built on.

The short version:

- **The code is nearly ready and the data is not.** Hosting LineOps is a week of work:
  a sign-in, a reverse proxy, an image pipeline, backups. Showing its data to the public is
  a licensing question. ESPN's terms forbid automated access and commercial use, and MLB's
  data is licensed for individual, non-commercial use. The Odds API's terms *do* allow
  display, including commercial display. The stats port is what has to change, and a
  licensed replacement costs about $10 a sport a month.
- **Go public in three steps, each one a decision rather than a drift:**
  1. an *invite-only showcase* on a $6–12 server,
  2. an *open public desk* once stats come from a licensed source,
  3. a *product* only after the answer to §8's first question is yes.
- **The asset that transfers is the platform, not the sports.** The desk, the reliability
  layer, the budget-paced scheduler and the snapshot workflow have already been carried
  into a second product, TicketMiser, by copying them. The next step is to stop copying:
  publish them as packages that each vertical consumes. That is what turns a portfolio
  piece into something a company can use.

---

## 1. Where it stands, measured

| Area | State | Evidence |
|---|---|---|
| Application | .NET 10, Blazor Interactive Server, one route, windows on a desk | `src/LineOps.Web/Program.cs`, `Windowing/WindowCatalog.cs` |
| Tests | ~720 tests: ~410 unit, fixture and Testcontainers, ~310 bUnit | `tests/` |
| CI | Build, `dotnet format`, test, pending-migration check. **No image publish, no deploy.** | `.github/workflows/ci.yml` |
| Containers | Hardened: non-root, read-only rootfs, `cap_drop: ALL`, loopback-only ports | `docker-compose.yml`, ADR 0006 |
| Identity | **None.** Every visitor can log and delete bets, spend odds credits, start a backfill, inject a fault | no `AddAuthentication` anywhere |
| Tenancy | **None.** `JournalEntry`, `Parlay`, `AppSetting` have no owner | `Core/Entities/Journal.cs` |
| Edge | No forwarded headers, no security headers, no rate limiting, `AllowedHosts: *` | `Program.cs`, `appsettings.json` |
| Scheduling | Web hosts the scheduler and evaluator by default; the worker is opt-in | `Program.cs`, memory note on second instances |
| Data | 2,427 MLB and 557 NFL games, 108k stat lines, 17k closing lines; ~2–3 MB snapshot **committed to a public repository** | `data/snapshots/lineops.dump.json` |
| Secrets | Keys in gitignored `appsettings.Local.json` and `.env`; `.dockerignore` keeps them out of images. The committed snapshot holds no key and no journal row (checked 2026-09-27) | `.dockerignore`, pg_restore audit |

What is already right for hosting: health and readiness endpoints, Data Protection keys on a
volume, migrations and partitions applied on boot, EF's migration lock, a runbook embedded in
the build, and OpenTelemetry ready to export.

---

## 2. What "public" can mean — three stages, and the gate for each

| | Stage 1 — Showcase | Stage 2 — Public desk | Stage 3 — Product |
|---|---|---|---|
| Who sees it | People given an access code: reviewers, recruiters, friends | Anyone with the URL | Accounts, some of them paying |
| What they can do | Read everything that is not personal; change nothing | Same | Keep their own journal, their own bankroll, their own alerts |
| What the operator can do | Everything, after signing in | Same | Same, plus accounts and billing |
| Data sources | Unchanged (ESPN, MLB Stats API, The Odds API free) | **Licensed stats** (balldontlie or MySportsFeeds), The Odds API | Licensed stats; a paid Odds API tier (20K credits, $30/mo) |
| Monthly cost | $6–12 server | ~$12 server + ~$20 stats | $12–25 server + ~$20 stats + $30–59 odds |
| Gate | Phase 0 built and deployed (§9) | The licensed stats adapter built and ESPN off in production (§4) | §8 decision 1 answered yes; tenancy built (§6) |

The stages are ordered by what each one risks, not by effort. A showcase behind a code is
the same personal use the laptop already makes, reachable from somewhere else. A public
desk republishes data, so the data must be data you may republish. A product takes money,
and that raises the question in §8 before any code does.

---

## 3. Deployment architecture

### 3.1 One machine, Compose, a proxy in front

```
                Internet (443)
                     │
              ┌──────▼──────┐   automatic Let's Encrypt, HTTP/3, HSTS
              │    Caddy    │   the only published ports: 80 and 443
              └──────┬──────┘
          compose network (internal, not published)
       ┌─────────────┼───────────────────┐
┌──────▼──────┐ ┌────▼──────┐     ┌──────▼──────┐
│  web :8080  │ │  worker   │     │  postgres   │
│  the desk   │ │ scheduler │────►│  17-alpine  │
│  no sched.  │ │ evaluator │     │  volume     │
└─────────────┘ └───────────┘     └──────┬──────┘
                                         │ nightly pg_dump
                                  ┌──────▼──────┐
                                  │  backups/   │ → off-box copy (R2/B2)
                                  └─────────────┘
```

- **Why a VM and not a platform.** Blazor Server keeps one circuit per visitor in memory
  over a WebSocket; more than one replica needs sticky sessions, and a platform that scales
  to zero drops every circuit. Azure Container Apps and App Service can do this correctly
  (single-revision affinity, ARR affinity) but cost $10–13 a month for the web tier alone
  before a database. One VM at the same price runs all four containers, the way the laptop
  does, and the compose file already exists.
- **Which VM.** A DigitalOcean 2 GB droplet ($12/mo, New York) for a US audience, or a
  Hetzner CX23 (4 GB, €5.49/mo, EU only since the June 2026 price change put US instances at
  $20+) if cost matters more than the extra ~90 ms. Blazor Server is interactive over the
  socket, so latency is felt; for a US reader, pay the $6 difference.
- **Memory budget.** web 512 MB + worker 384 MB + postgres 512 MB + Caddy ~50 MB ≈ 1.5 GB.
  2 GB is the floor.
- **The worker is the only scheduler.** `Ingestion:HostScheduler=false` and
  `Reliability:HostEvaluator=false` on web, always, in production. Two schedulers spend the
  odds credits twice; this is the same rule as the second-instance note, made structural.
- **HTTP inside, HTTPS outside.** Caddy terminates TLS with a real certificate. Web listens
  on plain HTTP on the compose network, which publishes no port. This amends ADR 0006 for
  the hosted case only: the rule was "no HTTP listener anyone can reach", and a listener on
  an unpublished bridge network is not one. The laptop stack keeps HTTPS-only as it is.
- **Forwarded headers.** Web honours `X-Forwarded-For`/`-Proto` only when
  `Hosting:BehindProxy` is set, and trusts the proxy because nothing else can reach it.

### 3.2 Delivery

1. Push to `main` → a workflow builds `lineops-web` and `lineops-worker` and pushes them to
   GitHub Container Registry as `:main` and `:sha-<commit>`.
2. On the server, `deploy/deploy.sh` pulls and restarts: `docker compose pull && up -d`.
   Migrations run on boot as now; EF's migration lock keeps two starting hosts from racing.
3. Rollback is `IMAGE_TAG=sha-<previous> ./deploy.sh`. Nothing else is needed, because a
   migration is additive by the project's own rule (DESIGN §13).

Automating step 2 (an SSH deploy job on the workflow) is left until deploys are frequent
enough to want it; a key that can reach production is a secret worth not having yet.

### 3.3 Backups — the part the reliability story needs

- Nightly `pg_dump -Fc` in the postgres container to a `backups/` volume, keeping 14 days.
- Weekly copy off the box to Cloudflare R2 or Backblaze B2 (both have free tiers at this size).
- **A restore drill every quarter**, recorded as an incident with a root cause and a
  corrective action, the way failure injection drills alerting. A backup nobody
  has restored is a hope.
- The committed snapshot workflow (`publish-data.ps1`) stays what it is: moving development
  data between the developer's machines. It is not the backup.

### 3.4 Observability, hosted

- The desk's own reliability layer stays the operational source of truth (ADR 0015).
- External uptime: a free Better Stack or UptimeRobot monitor on `/health` and `/ready`,
  because a host cannot report its own absence.
- OpenTelemetry to Grafana Cloud's free tier, or the Aspire dashboard bound to loopback and
  reached over an SSH tunnel. The dashboard is anonymous and must never be published.
- Container logs rotate (`json-file`, `max-size`), so a noisy week cannot fill the disk.

### 3.5 Security at the edge

| Control | Where | Why |
|---|---|---|
| Access modes and an operator sign-in | Web (Phase 0, §5) | Without it, the first visitor can spend the month's credits |
| Rate limit on sign-in | Web | Slows guessing a code: 5 attempts a minute per address |
| Security headers: CSP, `frame-ancestors 'none'`, `nosniff`, referrer policy, permissions policy | Web | The desk is never framed and loads nothing from other origins |
| `AllowedHosts` set to the real host name | Configuration | Host-header attacks and cache poisoning |
| HSTS | Web (already) and Caddy | Browsers never try HTTP again |
| Firewall: 22 from your address, 80/443 open, nothing else | The VM | Docker's iptables rules bypass `ufw` for published ports; publishing only Caddy's ports keeps that harmless |
| SSH keys only, unattended upgrades | The VM | The usual |
| Secrets in `/opt/lineops/.env`, mode 600 | The VM | Same model as the laptop; sops or a vault when there is a second operator |

---

## 4. Data — what may be shown, and what replaces what may not

Researched 2026-09-27. This is a reading of published terms, not legal advice.

| Source | Role today | Terms, as published | Public showcase (Stage 1) | Public desk / product (Stage 2–3) |
|---|---|---|---|---|
| **The Odds API** | Odds, the book market | Display "in a UI, website, or mobile app, including for commercial use" allowed; derived values allowed; no resale as a standalone data product; no attribution required | Yes | Yes; paid tier for cadence (20K credits $30/mo, 100K $59/mo) |
| **odds-api.io** | Alternative odds adapter (off) | Free tier described as for evaluation and prototyping; **new free keys paused**; paid from £49/mo | — | Only on a paid plan, after reading the commercial terms |
| **ESPN (undocumented JSON)** | Schedules, scores, box scores, reference closing line | Disney terms prohibit automated extraction and commercial use | Personal use behind a code: the same exposure as the laptop, no more | **No.** Replace before the desk is open to the public |
| **MLB Stats API** | Identity spine (`gamePk`, doubleheaders, starters) | MLBAM copyright: "individual, non-commercial, non-bulk use" only | As above | **No** as a displayed source. Its ids may stay as join keys; confirm before relying on that |
| **balldontlie** | Configured and seeded, **no adapter** | Commercial use, public display and betting products expressly allowed; no reselling raw data; not the sole source for settling wagers | — | **Recommended stats port.** All-Star $9.99/mo per sport; MLB and NFL both covered |
| **MySportsFeeds** | — | Commercial licences priced per league (~C$35–39/mo) | — | The fallback if balldontlie's box scores are too thin |

Three consequences:

1. **Build the balldontlie adapter before Stage 2.** The `Source` row, the options class and
   the `IStatsSource` port already exist; ADR 0011's split (stats from one port, odds from a
   book market) holds unchanged. The one term to design around: balldontlie may not be the
   *sole* source for settling wagers. LineOps grades a journal entry; it settles nothing
   with money. Say so in the ADR, and keep a second source (ESPN locally, or MySportsFeeds)
   able to confirm a final.
2. **ESPN's reference closing line goes away in public.** It fills games no market covered
   (ADR 0011). In public, those games have no close, and the desk already says "no fair
   close" rather than inventing one. That is the project's own rule working as written.
3. **The public repository.** `data/snapshots/lineops.dump` republishes ESPN- and
   MLB-derived data to anyone who clones it. Of every item in this document it is the one
   that is live today. The fix that does not rewrite history (CLAUDE.md forbids another
   rewrite) is **making the repository private**, which also keeps the journal private once
   real bets are logged. Take this decision first (§8, decision 2). Until then,
   `publish-data.ps1` leaves the journal, parlays and operator settings out of the snapshot
   by default (built in Phase 0).

---

## 5. Access model — who may do what

### 5.1 Now: two roles, three modes (Phase 0)

| Mode (`Access:Mode`) | Anonymous visitor | Visitor with the guest code | Operator (operator code) |
|---|---|---|---|
| `Open` *(default)* | Operator — today's behaviour, for the laptop | — | — |
| `Gated` | Sent to the sign-in page | Reads the desk | Everything |
| `Gated` + `AllowAnonymousVisitors` | Reads the desk | Reads the desk | Everything |

- **Visitors never see a personal window.** Journal, Slip, Place wager and Performance are
  marked `OperatorOnly` in the catalogue. The desk filters them from the strip, the command
  palette, workspaces and follow-ups. A saved layout that names one restores without it.
- **Visitors never see a control that writes.** These are hidden: the pull menu, the Ops
  window's evaluate, polling switch, failure injection and drill, incident notes and
  resolution, the History window's backfill controls, and the board's *Place wager* action.
  Blazor Server dispatches events only to handlers in the render tree it sent, so a hidden
  control cannot be invoked from the browser. Hiding the control is the enforcement, not a
  cosmetic layer over one.
- **Codes, not accounts.** Both codes are stored as PBKDF2 hashes in configuration
  (`Access:OperatorCodeHash`, `Access:GuestCodeHash`), generated by `scripts/new-access-code.ps1`.
  A sign-in sets an HTTP-only, secure, SameSite=Strict cookie. Sign-in is a plain form posted
  to a minimal endpoint, because a cookie cannot be set from inside a circuit.
- **`Gated` refuses to start without an operator hash**, the same "unconfigured means throw"
  rule the connection string follows.

### 5.2 Later: accounts (Stage 3 only)

Accounts are the largest single change and the least valuable until the rest works, as the
TicketMiser roadmap concluded. When they come:

- ASP.NET Core Identity on the same Postgres, or an external provider (Auth0, Entra
  External ID) to keep passwords off the box. The external provider is the better default
  for a product that takes money.
- An `OwnerId` on `JournalEntry`, `Parlay` and the per-user half of `AppSetting`
  (the bankroll). The market data stays shared. It is a read model, and it is the reason a
  user's CLV can be computed at all.
- A global EF query filter on `OwnerId`, plus a test that fails if any owned entity lacks
  one. Tenancy that depends on each query remembering is tenancy that leaks.
- Each user's desk layout, which lives in browser storage today, moves to the server with
  the account.

---

## 6. The product — if there is one

### 6.1 Where it would sit

| Product | Price | What it sells |
|---|---|---|
| OddsJam | $200–400/mo | +EV, arbitrage, middles |
| Unabated | ~$199/mo | Sharp odds screen, fair lines |
| Action Network PRO | ~$25/mo | Public and sharp money, picks, tracking |
| **Pikkit Pro** | **~$40/mo** | **Auto-synced bet tracking, CLV, line shopping** — the nearest neighbour |
| BetQL | $20–50/mo | Model picks |

Prices are from third-party reviews (September 2026), except Pikkit's own page.

LineOps is not an odds screen. At four books on a free tier it cannot be one, and competing
at $200 means buying Pinnacle-grade data at scale. What it does that the others do not:

1. **CLV against the fair close, with the basis stated.** The value is measured against a
   de-vigged price and the desk says which book priced it. Most trackers report CLV against
   one book's number.
2. **A journal that is honest about its own data.** It shows coverage, "no close" where
   there was none, and early closes flagged rather than trusted. That is the reliability
   layer's discipline reaching the user.
3. **The desk.** Several windows side by side is how people who do this seriously work,
   and no competitor's UI looks like it.

The honest positioning is a **$10–20/mo bet journal with fair-close CLV and a line board**,
free for the board and paid for the analytics. That undercuts Pikkit while giving up its
sportsbook sync, which needs sportsbook account access this project will not touch.

### 6.2 What a paid product must add

In order of necessity:

1. Accounts and tenancy (§5.2).
2. The licensed stats port (§4).
3. Billing: Stripe Checkout and the customer portal, with a webhook that sets the
   `Entitlement` row. No card data touches the box.
4. Responsible-gambling messaging and an 21+ affirmation at sign-up. No law found requires
   either for an information service. The AGA's code expects both, and every sportsbook
   affiliate programme will.
5. Terms of service and a privacy policy, because the product holds people's betting
   records.
6. **No sportsbook affiliate links** until the licensing is in place. In New Jersey,
   flat-fee affiliates need DGE vendor registration and revenue-share affiliates need an
   ancillary casino service industry enterprise (CSIE) licence. Pennsylvania has its own
   regime, and so does each other state. Affiliate revenue is how this category usually
   makes money, and it is also what makes it a regulated business.

---

## 7. The platform — expanding to other ideas and to companies

### 7.1 What is actually generic (already proven by TicketMiser)

| Piece | Domain-free? | Where |
|---|---|---|
| The desk: window manager, primitives, five theme families, command palette, workspaces | Yes (brand and clock are injected) | `src/LineOps.Desk` |
| Reliability: KPIs, freshness, success rate, volume anomaly, budget burn, the alert engine, incidents that cannot close without an RCA, the runbook | Yes | `src/LineOps.Reliability` |
| Observability: OTel wiring, KPI gauges, health checks | Yes | `src/LineOps.Observability` |
| Ingestion kernel: the state-driven scheduler, `CreditBudgetGuard`, the allowance-paced planner, backfill coordinator, source registry, promote-then-prune retention | Yes in shape; the planner needs a seam (TicketMiser's air-travel plan §5.3) | `src/LineOps.Ingestion/Services` |
| Operations shell: hardened compose, setup and snapshot scripts, CI, the working rules | Yes | repo root, `scripts/` |
| **Access (Phase 0)** | Yes. `IDeskAccess` and `OperatorOnly` live in the desk | `src/LineOps.Desk` |

TicketMiser carried the desk over "with its history", which means it is a fork. Two
forks of the same design system already diverge: the desk changes of September 23 are an
open porting task. A third vertical would make three.

### 7.2 Stop copying: a kit, published as packages

- A new repository (working name **Deskworks**) holding `Deskworks.Desk`,
  `Deskworks.Reliability`, `Deskworks.Observability` and `Deskworks.Ingestion.Kernel`.
  It is built, tested and versioned on its own, and published to GitHub Packages as NuGet
  packages. Its static assets (CSS, JS, fonts) ship inside the Razor class library.
- LineOps and TicketMiser reference package versions instead of project folders. A desk fix
  is released once, and each product takes it with a version bump, in its own time.
- A `dotnet new deskworks-vertical` template: Core, Data, Ingestion, Web and Worker
  projects, a catalogue with one window, one source adapter against a recorded fixture,
  compose, CI and `CLAUDE.md`. A new idea starts from a running, hardened, observable desk
  on day one.
- **Order:** extract from LineOps (the more complete desk), move TicketMiser onto the
  packages second, and create the template last, from what the two consumers turned out
  to need. Do not start before Stage 1 ships. Extraction is refactoring, and refactoring
  wants the deploy pipeline to catch what it breaks.

### 7.3 Which ideas fit the platform

The platform is built for one shape of problem: **scheduled pulls from metered third-party
sources, stored as change-only time series, reduced at an event to one final value, and
compared against what someone actually did.** Odds → closing line → CLV. Ticket prices →
day-of price → paid-versus-final. An idea fits when it has all four parts, and when it has
a licensed source.

| Idea | Source (licensed) | The "close" | Who pays | Fit |
|---|---|---|---|---|
| **Ticket prices** (TicketMiser, built) | Ticketmaster Discovery, SeatGeek | Day-of price | Fans (affiliate), press (report) | Proven |
| **Prediction markets** (Kalshi, Polymarket) | Public read APIs | Contract settlement | The same audience as LineOps | High, and the nearest to LineOps: CLV transfers unchanged. CFTC-regulated venue; reading and displaying is information |
| **Retail price history** (PC parts, GPUs) | Best Buy Products API (free, open); Amazon PA-API (affiliate-gated) | Purchase date price | Buyers (affiliate), like camelcamelcamel and Keepa | High; Keepa proves the model |
| **Energy prices** for small business | EIA (public domain), ISO feeds (ERCOT, CAISO: public) | Settlement interval price | Businesses on time-of-use tariffs | High. A B2B vertical with public-domain data |
| **Vendor reliability desk** | Status-page JSON (Atlassian Statuspage and the like), synthetic probes | SLA period result | Engineering teams | **The platform's own reliability layer as the product.** Freshness, incidents with RCAs, budget burn: what a team wants about its SaaS dependencies |
| **Air fares** (TicketMiser plan, 2026-09-12) | Affiliate-cached fares, DOT fare survey | Departure fare | Travellers | Medium. Planned; sources are thin |
| Freight and container rates | Paid indices | Contract rate | Shippers | Low. No affordable licensed source |

### 7.4 What a company gets

Three ways the same code serves a company, from least to most commitment:

1. **The kit, licensed or open-sourced.** An internal-tools foundation for any team that
   pulls from metered APIs: the budget guard stops the surprise invoice; the incident log
   forces the RCA. Open-sourcing the kit (MIT) and keeping the verticals private is the
   usual split. The kit earns reputation and inbound work, and the verticals keep their data
   arrangements.
2. **A vertical built for them.** For consulting-style work, the template is the first
   week of any engagement already done.
3. **The vendor-reliability desk as a hosted service.** Of all the ideas, this one has the
   clearest paying customer and no data licence problem, because teams point it at their
   own dependencies.

For the developer's career, which is the purpose DESIGN §1 names, the platform story is
stronger than any one vertical. It is *one design system and one reliability library,
serving several products, released as versioned packages, and deployed publicly with a
pipeline.*

---

## 8. Decisions that are yours

1. **Is a commercial betting-analytics product compatible with your employment?** DESIGN §1
   frames LineOps as a data-operations platform, precisely to avoid a conflict-of-interest
   read by an employer in regulated gaming. A paid product for bettors changes that
   reading. Some gaming licensing regimes also restrict what licensed employees may do.
   Check the employment agreement before Stage 3. The showcase and the platform (§7)
   don't raise the question. **Everything in Stage 3 waits on this.**
2. **Make `wNohejl/LineExtractor` private?** Recommended, and before anything is hosted (§4, consequence 3).
   CI minutes on private repositories are metered (2,000 free a month on the free plan). One
   CI run is a few minutes, so this fits.
3. **Where to host.** A DigitalOcean 2 GB droplet in New York ($12/mo) is recommended for
   US readers; a Hetzner CX23 in the EU (€5.49/mo) if cost matters more than latency.
4. **A domain.** Anything under ~$15 a year; Cloudflare Registrar sells at cost.
5. **When to pay for data.** balldontlie All-Star for MLB and NFL (~$20/mo) is the gate
   for Stage 2. The Odds API 20K ($30/mo) is what makes automatic polling affordable (the
   open finding in the improvements spec §2.1).
6. **Whether to extract the kit (§7.2) before or after TicketMiser's next phase.**

---

## 9. The plan, in phases

Each phase ends with something running that can be pointed at.

### Phase 0 — Safe to expose (built now, 2026-09-27)

- [x] This document.
- [x] Access modes, operator and guest codes, operator-only windows and controls (§5.1),
      with tests.
- [x] Edge hardening: forwarded headers behind a proxy, security headers, a sign-in rate
      limit, `AllowedHosts` from configuration.
- [x] `deploy/`: a production compose file (Caddy, web without the scheduler, worker as the
      only scheduler, nightly backups), a `Caddyfile`, `.env.example`, `deploy.sh`,
      `backup.sh`, and the runbook for a new server.
- [x] An image-publish workflow: GHCR on push to `main`, and on demand.
- [x] `publish-data.ps1` leaves personal tables out of the snapshot unless asked.
- [x] ADR 0018 (access) and ADR 0019 (hosted topology).

**Exit:** `Access:Mode=Gated` locally shows a visitor desk with nothing personal and no
control that writes, and an operator desk with everything; `dotnet test` green; the Worker
starts.

### Phase 1 — The showcase is live (a weekend, after decisions 2–4)

- Provision the VM, point DNS, fill `/opt/lineops/.env`, run `deploy.sh`.
- Restore the latest snapshot into the server's Postgres, with `restore-data.ps1`'s logic
  run over SSH.
- Uptime monitor on `/health` and `/ready`; first backup taken and **restored** into a
  scratch database, recorded as an incident with its RCA.
- Share the guest code with the first reviewers.

**Exit:** a URL, a guest code, and one restore drill on record.

### Phase 2 — A licensed stats port (one to two weeks)

- A `BallDontLieStatsSource` adapter for MLB and NFL: schedule, scores and box scores
  against recorded fixtures, with the same entity resolution as ESPN.
- `Ingestion:StatsSource` chooses the port per environment: ESPN on the laptop, balldontlie
  in production.
- Coverage queries from the 2026-09-04 research (§2) pass against a balldontlie-only
  database: no finals without stats, and no Live games stuck.
- ADR amending 0011: the stats port has two implementations, and why production uses the
  licensed one.

**Exit:** production runs with ESPN and the MLB Stats API switched off, and the desk still
settles.

### Phase 3 — The public desk (a week)

- `AllowAnonymousVisitors=true`.
- A plain landing page (static, cacheable) that says what the desk is before a circuit
  opens. It is also what search engines index, because a Blazor Server page is empty to a
  crawler.
- Circuit limits tuned for anonymous traffic, and a connection-rate limit on `/_blazor`.
- The Odds API 20K tier, with automatic polling switched on in Ops, so public prices are
  current. This also closes the early-close finding (improvements §2.1, option C).

**Exit:** anyone can open the desk, and a burst of visitors cannot spend a credit or run a
query the operator did not schedule.

### Phase 4 — The platform (two to three weeks, in parallel with Phase 3 or after)

- Extract Deskworks (§7.2); move LineOps onto the packages; then TicketMiser.
- The vertical template.
- Decide open source versus private kit.

**Exit:** a desk fix lands in both products through a package version, with no copying.

### Phase 5 — The product (only if §8.1 is yes; four to six weeks)

- Accounts through an external identity provider, with `OwnerId` and query filters (§5.2).
- Stripe billing, entitlements, free board, paid analytics.
- Terms, privacy policy, responsible-gambling messaging, 21+ affirmation.
- Per-user alerts: line moves and CLV results delivered through the alert engine's
  notification channel (DESIGN §13).

**Exit:** a stranger can sign up, log a bet, pay, and see its fair-close CLV settle.

---

## 10. Risks

- **ESPN changes shape before Phase 2.** It is the only stats source until then. This is
  already a controlled risk (DESIGN §0), and it becomes a public one once the showcase is up.
  The reliability layer will notice; the runbook covers it.
- **Blazor Server under anonymous load.** Each visitor holds server memory for the life of
  the circuit. At one VM that bounds concurrent readers in the low hundreds, which is far
  above what a showcase sees. If the public desk takes off, the static landing page takes
  the load and the desk stays behind a click.
- **Credits spent by the public.** Phase 0 removes every control that spends from a visitor's
  desk. The worker's scheduler is the only automatic spender, and the budget guard caps it.
- **A forked design system.** Every week the kit is not extracted, LineOps and TicketMiser
  drift further apart and the extraction costs more.
- **Terms drift.** Each provider's terms were read on 2026-09-27. Re-read them before
  Stage 2 and before Stage 3.

---

## 10a. Reassessed 2026-10-24

Four weeks on, nothing in §8 has been decided and nothing has been deployed. Three things
changed underneath the plan.

**CI had never seen Phase 0, and would have failed it.** CI runs only on `main` and on pull
requests, and `LineX_Development` was 177 commits ahead of `main`. Run the way CI runs it, on
Linux from a fresh clone, the build and the pending-migration check passed, but `dotnet format`
reported 153 errors in eleven test files. On Windows those errors hide among line-ending noise.
Fixed in `d39e829`. Until then, merging to `main` would have failed CI, and the Images workflow
publishes only after CI passes, so deployment was blocked without anyone seeing it.

**The data has a four-week hole.** The local Postgres has been stopped since 27 September, so
the MLB postseason and NFL weeks 4–8 were never ingested. Most of it is recoverable for free:
History's *Gather history* walks ESPN back over the gap through `StatsIngestionService`, which
writes schedules, scores, box scores and ESPN's one-book reference close. What cannot come back
is the multi-book market close for those games, and so their fair close (ADR 0010: odds are not
backfillable). They will read "no fair close", which is the honest answer.

**The calendar moved the sequencing.** MLB is in its offseason until Opening Day, late March
2027, so a showcase now shows NFL alone, and the next five months are the cheapest time to
change MLB's stats port. Revised order:

1. **Now, free:** start Postgres and the desk, run *Gather history* over 27 Sep → today, and
   refresh the snapshot (`publish-data.ps1 -Commit`, which now leaves personal tables out).
2. **Decision 2 (repository private)**, before anything below publishes more of the data.
3. **Merge `LineX_Development` → `main`.** That is the first CI run since September and the
   first image build.
4. **Phase 1**, the showcase: decisions 3 and 4 (VM, domain), then `deploy/README.md`.
5. **Phase 2 in the offseason:** the balldontlie adapter, NFL first because it is the league
   in season and so the one a fixture can be checked against live; MLB before Opening Day.

Decision 1 (employment) still gates Phase 5 and nothing earlier.

---

## 11. Sources

- The Odds API pricing and terms: https://the-odds-api.com/ ·
  https://the-odds-api.com/terms-and-conditions.html
- odds-api.io pricing and free tier: https://odds-api.io/pricing ·
  https://odds-api.io/blog/free-odds-api
- MLBAM copyright notice: http://gdx.mlb.com/components/copyright.txt
- Disney terms of use (ESPN): https://disneytermsofuse.com/english/
- balldontlie pricing and terms: https://www.balldontlie.io/ ·
  https://www.balldontlie.io/terms.html
- MySportsFeeds pricing: https://www.mysportsfeeds.com/feed-pricing/
- Hetzner price adjustment (June 2026):
  https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/
- DigitalOcean droplets: https://www.digitalocean.com/pricing/droplets
- Azure Container Apps session affinity:
  https://learn.microsoft.com/en-us/azure/container-apps/sticky-sessions
- Neon pricing: https://neon.com/pricing · Supabase pricing: https://supabase.com/pricing
- AGA Responsible Marketing Code: https://americangaming.org/marketing-code/
- New Jersey DGE affiliate guidance (Ifrah Law):
  https://www.ifrahlaw.com/ifrah-on-igaming/new-jersey-dge-releases-guidance-for-affiliate-marketers/
- Pennsylvania gaming service providers:
  https://gamingcontrolboard.pa.gov/licensing/gaming-related-service-providers-information
- Pikkit Pro: https://pikkit.com/pro · competitor reviews at betsmart.co and xclsvmedia.com
- TicketMiser product roadmap and air-travel plan:
  `C:\Deploy\TicketMiser\docs\superpowers\specs\2026-09-12-*.md`
