# LineOps runbook

What each alert means, how urgent it is, and what to do about it.

Every incident closed here needs a root cause and a corrective action — the app enforces it.
If the corrective action is a code change, reference the commit in the RCA so the loop from
incident to engineering change is visible later.

---

## `freshness` — Critical

**Means:** no successful ingestion run for this source in over 26 hours (SLO configurable via
`Reliability:FreshnessSlo`).

**Urgency:** real. Every downstream number — current lines, CLV resolution, settlement — is
working from stale data, and CLV in particular is *unrecoverable* if the close is missed: once
a game starts, the closing price that was never captured cannot be backfilled.

**Triage**
1. **Ingestion Runs** page, filter to the source. Is it failing, or not running at all?
2. Failing → read the error on the most recent run.
   - `HttpRequestException` / 5xx → provider outage. Check the provider's status page. Usually
     resolves itself; the circuit breaker will re-close automatically.
   - `401` / `403` → the API key expired or was revoked. Replace it in configuration.
   - `429` → rate limited. Check whether the budget guard should have caught this; if the
     provider's real limit is lower than the configured ceiling, correct the `Source` row.
3. Not running at all → is the scheduler alive? Look for "Ingestion scheduler started" in the
   logs. If the host restarted and the schedule has drifted, trigger a manual **Ingest now**
   from the dashboard to close the gap immediately.
4. If the provider is down for an extended period, the platform degrades rather than breaks —
   other sources continue, and this alert auto-resolves on the next success.

---

## `success_rate` — Warn

**Means:** fewer than 95% of this source's runs succeeded over the trailing 7 days, across at
least 3 runs. `Partial` runs count as failures (see
[ADR 0003](adr/0003-two-kinds-of-zero.md)).

**Urgency:** investigate the same day. Data is still arriving, but something is intermittently
wrong and it usually degrades further.

**Triage**
1. Look for a pattern in the failures on the Ingestion Runs page — every run, or one sport?
   One time of day? A single sport failing usually means a sport-key mapping is wrong after a
   provider change.
2. A mix of `Partial` runs means the provider is returning empty payloads intermittently — go
   to the `volume_anomaly` steps below.
3. Timeouts under load → consider raising the per-attempt timeout, but check first whether the
   provider is simply slow at a particular hour.

---

## `volume_anomaly` — Warn

**Means:** today's row count is below 50% of the trailing 7-day median while runs are still
succeeding.

**Urgency:** this is the alert that matters most. It is the only signal for a provider that
changed its schema and is now returning `HTTP 200` with data we no longer parse. Everything
looks green except this.

**Triage**
1. Call the provider endpoint by hand and compare the JSON against the adapter's expectations.
2. Compare against the recorded fixtures in `tests/LineOps.Tests/Fixtures/`. If the shape has
   changed, **record the new payload as a fixture first**, then fix the parser — that way the
   regression is pinned before the fix.
3. Check whether it is genuinely a light slate. Mid-week in the off-season is a real cause, and
   the honest response is to note it in the incident and resolve it as a false positive rather
   than to weaken the threshold.
4. If it is real schema drift, the corrective action usually belongs in the adapter's
   normalisation layer, not in the calling code.

---

## `budget_pressure` — Info, then Warn

**Means:** a provider is at or above 80% of its configured free-tier ceiling
(`Reliability:BudgetWarnThreshold`). The alert escalates in place rather than opening a second one:

| Utilisation | Severity | What it means |
|---|---|---|
| ≥ 80% | `Info` | Tight, but nothing has stopped. The lever is scheduling. |
| ≥ 100% | `Warn` | Spent. `CreditBudgetGuard` is refusing runs and data has stopped arriving. |

Unmetered providers never raise this. A source with no declared ceiling has *undefined*
utilisation, not comfortable utilisation, and reporting it as 0% would be a claim we cannot make.

**Urgency:** at `Info`, none immediately — but ignoring it means either dropped runs or a bill. At
`Warn` it has already become a freshness problem waiting to be noticed, because refused runs write
no rows.

**Triage**
1. The alert message names the dimension under pressure and the numbers behind it — hourly
   requests, daily requests, or monthly credits. Start there rather than guessing.
2. Credits, on The Odds API → each call costs `markets × regions`. Reduce the market list or
   the polling frequency rather than adding regions.
3. Consider whether the intraday movement window (`Ingestion:MovementWindow`, default 36h) is
   wider than it needs to be. Narrowing it is the cheapest lever.

---

## Running a drill

You do not have to wait for a real outage to practise this.

1. **Ops Center → On-call drill**, pick a source and a failure mode:
   - **Upstream 503** — hard failure, exercises retry and the breaker.
   - **Request timeout** — slow failure.
   - **Empty payload** — the silent one; returns `HTTP 200` and no rows.
2. **Run ingest & evaluate**.
3. Watch the alert appear, promote it to an incident, work the timeline, then write the RCA
   and resolve it.
4. Set the source back to **Healthy** and confirm the alert auto-resolves on the next
   evaluation.
