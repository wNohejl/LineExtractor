# Infrastructure — as built

A definition of what actually runs, where it runs, and what it depends on, current as of
August 2026 (pre row-drilldown commit). The *why* behind each choice lives in the ADRs
([docs/adr](adr/)); this page is the *what*, for anyone who needs to operate, review, or
extend the stack without reading the whole design.

## Solution layout

| Project | Role | Depends on |
|---|---|---|
| `LineOps.Core` | Entities, contracts (`IOddsSource`, `IStatsSource`), grading/analytics rules | nothing |
| `LineOps.Data` | EF Core 10 model, migrations, cross-reference read services (Board, Matchup, GameLog) | Core |
| `LineOps.Ingestion` | Provider adapters, scheduler, budget guard, entity resolution, backfill, settlement — a library, hostable by web or worker (ADR 0005) | Core, Data |
| `LineOps.Reliability` | KPI rollups, freshness/success/volume alerting, incident log with enforced RCAs, runbook pinned to code | Core, Data |
| `LineOps.Observability` | OpenTelemetry wiring (ASP.NET Core, HttpClient, runtime, Npgsql) + KPI metrics publisher (ADR 0015) | Reliability |
| `LineOps.Web` | Blazor Web App, global Interactive Server; the desk (WindowManager/WindowCatalog) hosting ordinary panels | everything above |
| `LineOps.Worker` | Optional Worker-SDK host composing the ingestion library as its own process | Ingestion |
| `LineOps.Tests` | xUnit: unit + adapter fixtures + Testcontainers integration (real PostgreSQL) | all |

Package versions are centrally pinned in `Directory.Packages.props`
(`ManagePackageVersionsCentrally` + transitive pinning). Solution file is `LineOps.slnx`.

## Runtime topology (Docker Compose)

`docker-compose.yml` — the base stack a reviewer can run with no keys:

- **postgres** — PostgreSQL 17-alpine, SCRAM-only auth, data on the `lineops-pgdata`
  volume, healthchecked; *not* published to the host by default.
- **web** — Kestrel, HTTPS only on `127.0.0.1:9443`, PFX mounted read-only from the host,
  Data Protection keys on the `lineops-keys` volume (survives container replacement),
  migrates the database on boot, demo sources enabled so first boot works with no spend.
- **worker** — opt-in via `--profile worker`; when used, set `Ingestion__HostScheduler=false`
  on web so only one process polls.

Container hardening applies to every service (ADR 0006): non-root, `read_only` root
filesystem, `cap_drop: ALL` (postgres adds back only its init caps), `no-new-privileges`,
tmpfs for scratch, memory limits, and **every published port bound to 127.0.0.1**.

Overlays, deliberately opt-in rather than default:

- `compose.dev.yml` — publishes Postgres on `127.0.0.1:5433` for psql / host-side `dotnet run`.
- `compose.telemetry.yml` — Aspire dashboard on `127.0.0.1:18888`, OTLP ingest on `:18889`;
  the OTLP exporter is off in appsettings and switched on here (ADR 0015).

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
| odds-api.io | `IOddsSource` (primary) | scheduled slate + movement pulls | 100/h · 500/day, enforced by `CreditBudgetGuard` — a run that would breach is refused and recorded `Partial` |
| The Odds API | `IOddsSource` (reconciliation) | small credit budget | 500 credits/mo |
| balldontlie | `IStatsSource` | REST pulls | free tier |
| ESPN (undocumented JSON) | `IStatsSource` — stats port only, never the odds port (ADR 0011); it also yields one reference closing line per finished game | scoreboard walk + box-score fetch, history backfill (ADR 0009) | free; requires a real HTTP-client `User-Agent` (403 otherwise — see `SourceOptions.UserAgent`) |
| Demo sources | both ports | offline fixtures with drifting prices | none |

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

Anything that fails either job fails the push — run `dotnet format` and the full test
suite (Docker running) before committing.

## Local development

- `scripts/setup.ps1` bootstraps certificate + `.env`.
- `dotnet run` against `compose.dev.yml`'s published Postgres, or full stack via compose.
- Purge scripts in `scripts/` (demo data, exhibitions/orphans, preseason) are the only
  sanctioned by-hand database interventions; day re-walks are done from the History window.
