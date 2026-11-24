# Infrastructure — as built

A definition of what actually runs, where it runs, and what it depends on, current as of
2026-11-22. The *why* behind each choice lives in the ADRs
([docs/adr](adr/)); this page is the *what*, for anyone who needs to operate, review, or
extend the stack without reading the whole design. [docs/README.md](README.md) says which
other documents are current.

The product is **XLines**; `XLines` is the platform it is built on and the name in every
project, image, volume and command below (ADR 0031).

## Solution layout

| Project | Role | Depends on |
|---|---|---|
| `XLines.Core` | Entities, contracts (`IOddsSource`, `IStatsSource`), grading/analytics rules | nothing |
| `XLines.Data` | EF Core 10 model, migrations, cross-reference read services (Board, Matchup, GameLog) | Core |
| `XLines.Ingestion` | Provider adapters, scheduler, budget guard, entity resolution, backfill, settlement — a library, hostable by web or worker (ADR 0005) | Core, Data |
| `XLines.Reliability` | KPI rollups, freshness/success/volume alerting, incident log with enforced RCAs, runbook pinned to code | Core, Data |
| `XLines.Observability` | OpenTelemetry wiring (ASP.NET Core, HttpClient, runtime, Npgsql) + KPI metrics publisher (ADR 0015) | Reliability |
| `XLines.Desk` | The desk as a reusable Razor class library: windows, tokens, themes, controls, the MudBlazor seam | MudBlazor |
| `XLines.Desk.Hosting` | The access gate (ADR 0018), sign-in page and edge headers for a Desk app | Desk |
| `XLines.Desk.Templates` | `dotnet new deskapp`, packing the Desk's own skills; content only, and the app it makes references the Desk packages | — |
| `XLines.Desk.Testing` | bUnit bench, fakes and source conventions for an app built on the Desk | Desk |
| `XLines.Web` | Blazor Web App, global Interactive Server; the XLines desk built on `XLines.Desk` | everything above |
| `XLines.Worker` | Optional Worker-SDK host composing the ingestion library as its own process; also runs named jobs (`--job`) | Ingestion |
| `XLines.Tests` | xUnit: unit + adapter fixtures + Testcontainers integration (real PostgreSQL) | all |
| `XLines.Web.Tests` | bUnit and service tests for the desk's windows | Web |
| `XLines.Desk.Tests` / `XLines.Desk.Hosting.Tests` | The Desk package, theme contrast, template; the access gate | Desk, Desk.Hosting |

`desk-packages.yml` packs and publishes the four Desk packages; `images.yml` builds the web and
worker container images.

Package versions are centrally pinned in `Directory.Packages.props`
(`ManagePackageVersionsCentrally` + transitive pinning). Solution file is `XLines.slnx`.

## Runtime topology (Docker Compose)

`docker-compose.yml` — the base stack a reviewer can run with no keys:

- **postgres** — PostgreSQL 17-alpine, SCRAM-only auth, data on the `lineops-pgdata`
  volume, healthchecked; *not* published to the host by default.
- **web** — Kestrel, HTTPS only on `127.0.0.1:9443`, PFX mounted read-only from the host,
  Data Protection keys on the `lineops-keys` volume (survives container replacement),
  migrates the database on boot. First boot works with no keys and no spend, and with an
  empty desk: ESPN is off until its operator turns it on, and the default source wants a
  key (ADR 0026). Ops reports a source with no key as unconfigured rather than as an
  outage, and asks for one.
- **worker** — opt-in via `--profile worker`; when used, set `Ingestion__HostScheduler=false`
  and `Reliability__HostEvaluator=false` on web so only one process polls and evaluates.

Container hardening applies to every service (ADR 0006): non-root, `read_only` root
filesystem, `cap_drop: ALL` (postgres adds back only its init caps), `no-new-privileges`,
tmpfs for scratch, memory limits, and **every published port bound to 127.0.0.1**.

Overlays, deliberately opt-in rather than default:

- `compose/dev.yml` — publishes Postgres on `127.0.0.1:5433` for psql / host-side `dotnet run`.
- `compose/telemetry.yml` — Aspire dashboard on `127.0.0.1:18888`, OTLP ingest on `:18889`;
  the OTLP exporter is off in appsettings and switched on here (ADR 0015).
- `compose/tailnet.yml` — a Tailscale sidecar serving the desk on your own tailnet; web
  drops to plain HTTP on the compose network, behind the proxy and gated (ADR 0022, and
  "From another device" below).
- `compose/tailnet-db.yml` — publishes Postgres on the desktop's tailnet address for a second
  desk on the laptop (ADR 0023, below).

`deploy/compose.yml` is a separate, hosted stack (Caddy, web, worker, Postgres, backups); see
`deploy/README.md`. It has been verified locally and never deployed: 1.0 is a desk each person
runs for themselves, and a hosted demo is optional (ADR 0031).

Secrets come from a gitignored `.env` (`POSTGRES_PASSWORD`, `CERT_PASSWORD`, `CERT_PATH`,
provider API keys). There are no fallback credentials: unconfigured means throw.

## Data stores

One PostgreSQL database, one EF Core model, migrations checked by CI.

- **Odds time-series**: `OddsSnapshots` is append-only and store-on-change (ADR 0001) —
  scans live until a game starts, then are pruned to one `ClosingLine` row per
  game/market/outcome/source (ADR 0010).
- **Stats**: `PlayerGameStats` holds per-sport `jsonb` stat lines; `StatSnapshots` the raw
  captures. Games/teams/players carry `jsonb` `external_ids` per provider — entity
  resolution unifies one fixture across sources without merging two fixtures from the same
  source.
- **Operations**: `IngestionRuns`, `BackfillCheckpoints` (held-day bookkeeping, clearable
  from the History window), `KpiDaily`, `Alerts`, `Incidents` (with written RCAs),
  `JournalEntries` (a ledger row, never an instruction).

## External sources

| Provider | Port | Style | Budget |
|---|---|---|---|
| The Odds API | `IOddsSource` — the book market (Pinnacle, DraftKings, FanDuel, BetMGM) | pulled on request (`LinePolling:Mode` `Manual`), or on a derived cadence when `Scheduled` | 500 credits/mo, billed markets × regions; enforced by `CreditBudgetGuard` — a run that would breach is refused and recorded `Partial` |
| The Odds API, schedule (`the-odds-api-schedule`) | `IStatsSource` and `IScoreSource` — **the default stats source**, registered only while ESPN is off (ADR 0026) | fixtures from `/events` on a state-driven cadence; final scores from `/scores?daysFrom=3` on request, or nightly per league when Scanning is Automatic | fixtures free; scores 2 credits a league, from the same account and month as the prices |
| odds-api.io | `IOddsSource` — alternative, off by default | as above | 100/h · 500/day; free tier is for evaluation (ADR 0024) |
| ESPN (undocumented JSON) | `IStatsSource` — **opt-in** (`Ingestion:Espn:Enabled`, false on a fresh clone); stats port only, never the odds port (ADR 0011); it also yields one reference closing line per finished game | scoreboard walk + box-score fetch, history backfill (ADR 0009) | free; requires a real HTTP-client `User-Agent` (403 otherwise — see `SourceOptions.UserAgent`) |
| MLB Stats API | entity spine — issues the `gamePk` and MLBAM ids every other source resolves into; follows ESPN's switch | schedule walk | free, unauthenticated |

Each source carries a licence, `Displayable` or `PersonalUse` (ADR 0024): The Odds API's two
rows are displayable, ESPN, MLB and odds-api.io are personal use. With `Sharing:Mode=Shared`
behind a gate, a visitor reads the displayable tier only; `Local`, the default, changes nothing.

Resilience per source: `AddStandardResilienceHandler` (retry with jitter, circuit breaker,
per-attempt timeout) on every HttpClient.

## Observability & health

- `/health` — liveness (no checks, process-up only); `/ready` — readiness (database).
- OpenTelemetry traces/metrics/logs over OTLP when the telemetry overlay runs; Npgsql's
  ActivitySource is enabled via `Npgsql.OpenTelemetry`.
- The bespoke reliability layer (KPIs, alerts, incidents, runbook) is the operational
  source of truth; OTel is the standard export beside it, not a replacement (ADR 0015).

## CI (GitHub Actions, `.github/workflows/ci.yml`)

1. `build-and-test`: restore → build (Release) → **`dotnet format --verify-no-changes`** →
   `dotnet test` (integration tests start their own PostgreSQL via Testcontainers on the
   runner's Docker daemon).
2. `migrations`: fails if an entity change lacks a matching migration.

The workflow runs on pushes and pull requests to `main` only; `LineX_Development` pushes get no
CI. Run `dotnet format --verify-no-changes` and the full test suite (Docker running) before
merging to `main`, where anything that fails either job fails the push. Run format in a Linux
SDK container: on Windows it reports line-ending noise.

## Local development

- `scripts/setup.ps1` bootstraps certificate + `.env`; the README's Quick start, and the
  `xlines-setup` agent skill, take a fresh clone to a running desk.
- `dotnet run` against `compose/dev.yml`'s published Postgres, or full stack via compose.
- Purge scripts in `scripts/` (fixtures the retired demo source invented, exhibitions/orphans,
  preseason) are the only
  sanctioned by-hand database interventions; day re-walks are done from the History window.

## From another device

The desk on the desktop can be opened from the laptop or a phone over your own Tailscale
tailnet, with Tailscale Serve: HTTPS at `https://<machine>.<tailnet>.ts.net`, reachable by your
signed-in devices only. It is never Funnel, which would put it on the public internet and
republish ESPN-derived data (ADR 0022; going-public plan §4, §10b).

Once, by hand: install Tailscale on each device and sign in to the same tailnet; in the admin
console's DNS page turn on MagicDNS and HTTPS certificates; make an operator code with
`scripts/new-access-code.ps1`.

- **From source** (`dotnet run --project src/XLines.Web --launch-profile http`): put
  `Hosting:BehindProxy = true` and `Access:Mode = Gated` with the operator hash in
  `src/XLines.Web/appsettings.Local.json` (`Get-Help scripts/serve-tailnet.ps1` shows the
  file), then `./scripts/serve-tailnet.ps1`. It prints the URL; `-Off` stops serving.
- **In containers**: add `TS_AUTHKEY` (admin console → Settings → Keys; not reusable, not
  ephemeral, tagged `tag:xlines` — an untagged node's key expires in about 180 days) and
  `ACCESS_OPERATOR_CODE_HASH` to `.env`, then
  `docker compose -f docker-compose.yml -f compose/tailnet.yml up -d`. The node is named
  `TS_HOSTNAME` (default `desk`); set `XLINES_TAILNET_HOST` to its full name to refuse
  any other `Host`.

Either way it is still the one desk and its one scheduler; nothing on the other device runs.

**Or against the desktop's database.** The laptop can run its desk against the desktop's database instead of a restored copy ([ADR 0023](adr/0023-one-database-two-desks.md)). On the desktop: install Tailscale, put its address (`tailscale ip -4`) in `.env` as `TAILNET_IP`, and bring Postgres up with `docker compose -f docker-compose.yml -f compose/dev.yml -f compose/tailnet-db.yml up -d postgres`, which publishes it on the tailnet address beside loopback and nowhere else. On the laptop: join the same tailnet, put the desktop's `POSTGRES_PASSWORD` in its own `.env` as `REMOTE_POSTGRES_PASSWORD`, run `.\scripts\connect-remote-db.ps1 -Host <desktop>`, and start the desk as usual. The script switches the laptop's scheduler and evaluator off; the desk refuses to start against a remote database with either on unless `Ingestion:AllowRemoteScheduling=true` says so (`SchedulerPlacement`). The connection uses `SSL Mode=Disable` because the tailnet already encrypts it. With the desktop off the laptop's desk cannot start: `connect-remote-db.ps1 -Local` and `restore-data.ps1` are the fallback. Only the desktop migrates its database: a desk or a Worker `--job` on the laptop never migrates it, and refuses to start when the laptop's code and the database's migrations differ, naming them: newer code is migrated from the desktop, older code pulls the newer commit (`Data:AllowRemoteMigration=true` is the deliberate override). `restore-data.ps1` on the desktop brings Postgres back with `compose/tailnet-db.yml` whenever `.env` sets `TAILNET_IP` (`XLINES_COMPOSE_FILES` overrides the list).
