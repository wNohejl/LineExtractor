# Incident-handling ops upgrade — design & implementation plan

**Date:** 2026-08-01
**Status:** approved, in progress
**Context:** LineOps is a portfolio project whose stated career purpose (DESIGN.md §, "Purpose (career)")
is to evidence on-call / KPI / RCA operational reliability for the Starbucks Application Developer and
Senior Application Developer roles. This change closes credibility gaps in that story and adds
industry-standard telemetry alongside the bespoke reliability layer.

**Non-negotiables inherited from DESIGN.md §0 and §11 — do not break these:**

- The whole stack runs at **$0**.
- A **cold clone with no API keys** must still run and demo every feature.
- Container posture from ADR 0006 (loopback-bound, no committed secrets) is preserved.

---

## The two findings that motivated this

1. **`budget_pressure` is documented but cannot fire.** `AlertRules.BudgetPressure`
   (`src/LineOps.Reliability/AlertEngine.cs`) and `ReliabilityOptions.BudgetWarnThreshold` both exist,
   and `docs/runbook.md` carries a full triage section for the rule — but `AlertEngine.EvaluateAsync`
   only ever emits `freshness`, `success_rate`, and `volume_anomaly`.

   The cause is architectural, not an oversight: the budget data lives in `CreditBudgetGuard`, which is in
   `LineOps.Ingestion`, and the reference direction is `Ingestion → Reliability`. `AlertEngine`
   structurally cannot see it.

2. **`IncidentService` has zero test coverage.** The RCA-enforcement discipline that DESIGN.md §5 calls
   "the reason this project exists" is the one part of the reliability layer no test touches.

---

## Part A — Close the credibility gaps

### A1. Split budget *measurement* from budget *enforcement*

Move budget reading into `LineOps.Reliability` as a new `BudgetCalculator`, and move the `BudgetUsage`
record with it. It is already a pure read over `ingestion_run` + `source` and touches only the Data
layer, so nothing blocks the move.

`CreditBudgetGuard` stays in `LineOps.Ingestion` and becomes a thin decision on top of the calculator.

**The boundary argument, which is the point:** measuring consumption is a reliability concern; refusing a
run is an ingestion concern. This is cleaner than letting the alert engine reach into the ingestion
library, and it strengthens the §5 shared-library framing rather than diluting it.

Consumers to update: `OpsPanel.razor`, `PartsPanel.razor`, `LinePollPlanner`, `IngestionServiceCollectionExtensions`,
and anything else surfaced by a `BudgetUsage` / `CreditBudgetGuard` grep.

### A2. Make `budget_pressure` fire, with a severity ladder

`AlertEngine` gains the fourth rule. Three refinements over what the runbook currently describes:

- **Info at ≥ `BudgetWarnThreshold` (0.8), Warn at ≥ 1.0.** At 100% the budget is spent and the guard is
  actively *refusing* runs — real degradation, not an FYI. A flat Info severity would under-report it.
- **The message names the pressured dimension** (hourly requests / daily requests / monthly credits).
  The runbook's triage step 1 asks exactly "which dimension is under pressure"; an alert that cannot
  answer its own first triage step is decoration.
- **Unmetered sources are skipped**, mirroring the existing `NeverRun` judgment. All-null limits yields
  `WorstUtilisation == 0`, which would otherwise read as a healthy 0% rather than "not applicable."

Reconciliation and auto-resolve come free from the existing `ReconcileAsync` machinery. Info/Warn never
auto-opens an incident — that path is Critical-only — which is correct and should carry a comment saying so.

### A3. A test that makes doc-to-code drift impossible to reintroduce

A test that reflects over the `AlertRules` constants, parses the `##` headings in `docs/runbook.md`, and
asserts a **bijection**: every rule has a runbook section, every runbook section has a rule.

This is the strongest interview beat in the change: *"I shipped a runbook entry for an alert that couldn't
fire. Rather than just fix it, I made the doc-to-code drift a test failure."*

Implementation note: the runbook must be resolvable from the test's working directory. Copy it to the test
output via `<Content Include=... CopyToOutputDirectory>` in `LineOps.Tests.csproj`, or walk up from
`AppContext.BaseDirectory` to the repo root. Prefer the csproj copy — it fails loudly if the file moves.

### A4. Runbook triage rendered inside the incident panel

The on-call working an incident should not have to go find a markdown file. Map rule → runbook section and
render the triage steps in `IncidentsPanel.razor` alongside the timeline. Cheap, because the mapping
already exists from A3.

### A5. Fill the `IncidentService` test hole

- `ResolveAsync` throws without a root cause
- `ResolveAsync` throws without a corrective action
- `PromoteAsync` is idempotent — a second promote returns the existing incident, does not open a second
- The timeline appends in order and survives a round-trip through jsonb
- `AutoOpenIncidentsAsync` respects the `AutoIncidentAfterCriticals` threshold and ignores Warn/Info

---

## Part B — OpenTelemetry + Aspire dashboard

### B1. New `LineOps.Observability` library

Exposes `AddLineOpsTelemetry(...)`, referenced by `LineOps.Web` and `LineOps.Worker` — deliberately the
same shape as the existing `AddLineOpsReliability` pattern so it reads as consistent rather than bolted on.

### B2. Instrumentation

An `ActivitySource` spanning each ingestion run (tagged source, job key, status, rows, requests) and each
reliability evaluation. A `Meter` whose instruments **mirror the existing KPIs** rather than inventing new
ones:

| Instrument | Kind |
|---|---|
| `lineops.ingestion.runs` / `.rows` / `.requests` | counters, tagged by source + status |
| `lineops.source.freshness_minutes` | observable gauge |
| `lineops.budget.utilisation` | observable gauge |
| `lineops.alerts.open` / `lineops.incidents.open` | observable gauges |

**The observable gauges are the whole argument.** They make the bespoke KPI layer legible to standard
tooling, converting "he built a custom monitoring thing" into "he built a KPI layer and exposed it over
OTel." Observable gauges need a scope per observation — resolve `IServiceScopeFactory`, never capture a
`DbContext` in the callback.

### B3. Aspire dashboard container

One container in `docker-compose.yml`, loopback-bound per ADR 0006. Its anonymous-access setting is a
deliberate, documented local-only decision — write it into the ADR rather than let it look accidental,
since §7's security posture is a stated selling point.

### B4. `/health` and `/ready`

Split honestly: `/health` = process alive; `/ready` = Postgres reachable and migrations applied. Wire
`/ready` into the compose healthcheck. Fold into whatever healthchecks compose already has rather than
duplicating.

### B5. New ADR — "Standard telemetry alongside a bespoke reliability layer"

Why both exist and where the line falls: OTel carries signals to whatever the operator already runs; the
reliability layer encodes the **domain judgments** (two kinds of zero, unconfigured-isn't-an-outage,
median-needs-three-days) that no generic exporter can make. This ADR is a better interview artifact than
the code it describes.

---

## Verification gates

- `dotnet build` clean, no new warnings
- `dotnet test` — all existing tests still green, new tests green (Testcontainers needs Docker running)
- Cold-clone check: `docker compose up` with no `.env` API keys still boots and demos
- Aspire dashboard reachable on loopback only; confirm it is not published on `0.0.0.0`

---

## Progress

Update this as work lands so a fresh session can resume without re-deriving anything.

- [ ] A1 — `BudgetCalculator` + `BudgetUsage` moved to Reliability; `CreditBudgetGuard` rewritten on top
- [ ] A2 — `budget_pressure` rule in `AlertEngine` with Info/Warn ladder and named dimension
- [ ] A3 — runbook ↔ `AlertRules` bijection test
- [ ] A4 — runbook triage rendered in `IncidentsPanel`
- [ ] A5 — `IncidentService` test coverage
- [ ] B1 — `LineOps.Observability` project + `AddLineOpsTelemetry`
- [ ] B2 — ActivitySource + Meter instrumentation
- [ ] B3 — Aspire dashboard in compose
- [ ] B4 — `/health` + `/ready` + compose healthcheck
- [ ] B5 — ADR 0015
- [ ] Docs — update `runbook.md` (severity ladder), `DESIGN.md` §5/§11/§12, `README.md`

---

## Deferred, deliberately

Considered and cut from this round, recorded so the reasoning is not lost:

- **Sev1–3 severity policy, MTTD/MTTA/MTTR as tracked KPIs, acknowledge step, error budgets,
  postmortem markdown export with action-item tracking.** The single strongest remaining upgrade to the
  on-call story. Cut only to keep this round focused.
- **Publicly hosted demo** with seeded incident history and a guided tour, so a recruiter clicks a link
  rather than running Docker. Highest-leverage change for *site viability* as opposed to code quality.
