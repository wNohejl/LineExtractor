# ADR 0015 — Standard telemetry beside a bespoke reliability layer

**Status:** Accepted

## Context

`LineOps.Reliability` already computes freshness, success rate, volume anomaly and budget burn,
raises alerts against SLOs, and opens incidents. Adding OpenTelemetry raises an obvious question:
if the platform already measures itself, what is the second system for — and if OTel can measure
it, why does the first system exist?

Answering "both, because both are good" would be the wrong answer. Two monitoring systems with
overlapping responsibilities is a real cost: two definitions of freshness that drift, two places
to look during an incident, and an interview question about which one is authoritative that has no
good answer.

There is also a constraint the design cannot break. The repository has to run for a reviewer with
no API keys, no collector, and no configuration, at $0 (§0). A telemetry stack that must be
running before the app works would cost more than it is worth.

## Decision

**The line is measurement versus judgment.**

OpenTelemetry carries signals to whatever the operator already runs. It is transport and
convention: spans for ingestion runs, counters for what those runs did, gauges sampling current
state, exported over OTLP to any backend.

The reliability layer holds the judgments — the decisions that are specific to this domain and
that no generic exporter could make:

- **Two kinds of zero** ([ADR 0003](0003-two-kinds-of-zero.md)). A run that writes no rows is a
  quiet market or a silent outage depending on what the *provider returned*. A generic exporter
  sees one number.
- **Unconfigured is not an outage.** A source that has never run is skipped rather than alerted on.
- **A median needs three days.** Volume ratio returns null rather than guessing, because a false
  alert on day one teaches you to ignore alerts.
- **Unmetered is not zero.** A provider with no ceiling has undefined utilisation, not comfortable
  utilisation.

Those are the interesting decisions, and they are the ones a dashboard cannot make for you.

**Instrumentation is BCL; only the host knows about OpenTelemetry.** `ActivitySource` and `Meter`
are framework types, so `LineOps.Core.Diagnostics.LineOpsTelemetry` declares the names and the
working libraries emit through them with no package reference. `LineOps.Observability` is the only
project that references OpenTelemetry at all. Changing exporter, or dropping OTel entirely, touches
one project and no instrumentation.

**The gauges publish the reliability layer's own numbers.** `lineops.source.freshness_minutes`,
`lineops.budget.utilisation`, `lineops.incidents.awaiting_rca` and the rest are computed by
`KpiCalculator` and `BudgetCalculator` — the same code the Ops Center and the alert engine use, not
a second implementation. This is the whole point: the bespoke layer becomes legible to standard
tooling without becoming a second source of truth.

**Gauges read a cached snapshot, never the database.** A metrics callback runs synchronously on the
collection path; querying Postgres from inside one blocks that thread and, under load, is how a
monitoring system becomes the outage it exists to report. `KpiMetricsPublisher` samples on a timer
and the callbacks read a reference. The snapshot's own age is published as
`lineops.kpi.snapshot_age_seconds`, so a publisher that has silently stopped is visible rather than
presenting its last reading as current for ever.

**Off by default, on via an overlay.** `Observability:Enabled` is false in `appsettings.json`; the
`compose.telemetry.yml` overlay turns it on and adds the Aspire dashboard. This matches
`compose.dev.yml` — the base stack stays something a reviewer runs without explanation.

**Liveness and readiness are separate questions.** `/health` depends on nothing, so a database
outage cannot get the container killed and restarted into the same outage. `/ready` reports whether
Postgres is reachable *and* the schema is at the version this build expects — a process pointed at
a database with pending migrations will start, accept traffic, and fail every request, and no
restart fixes that.

## Consequences

The Aspire dashboard runs with anonymous access enabled. That is only acceptable because both its
ports bind to `127.0.0.1` per [ADR 0006](0006-container-security-and-https.md). The dashboard
surfaces span attributes that can include connection strings and request contents, so the loopback
binding is load-bearing rather than ceremonial. Exposing it beyond the host means turning its token
auth back on first.

There is **no compose healthcheck on the `web` service**, and this is deliberate rather than an
omission. The runtime image (`mcr.microsoft.com/dotnet/aspnet:10.0`) ships no `curl` and no `wget`
— verified, not assumed — and a Docker healthcheck must run inside the container. Adding an HTTP
client to satisfy a local convenience would enlarge the attack surface of an image that is
otherwise read-only, non-root and capability-dropped. `/health` and `/ready` are shaped for an
orchestrator that probes over HTTP from outside the container, which is where they actually matter.

The KPI publisher runs even when the exporter is off. It costs one round of queries every 30
seconds and keeps the gauges meaningful the moment telemetry is switched on, rather than requiring
a restart to start collecting.
