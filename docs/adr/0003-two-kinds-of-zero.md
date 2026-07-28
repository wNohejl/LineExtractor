# ADR 0003 — Distinguishing "no data" from "no movement"

**Status:** Accepted

## Context

Because prices are only written when they change ([ADR 0001](0001-append-only-partitioned-odds.md)),
a healthy run against a quiet market writes zero rows. So does a completely broken provider
that returns `HTTP 200` with an empty or restructured payload.

Both produce `RowsIngested = 0`. Treating them the same means either paging someone every time
a market is quiet, or never noticing a silent outage. Both failures are unacceptable, and the
second is the one that actually bites: a provider that errors is obvious, a provider that
returns 200 and nothing is not.

## Decision

The run status is derived from what the *provider* returned, not from what was written:

| Provider returned | Rows written | Status | Meaning |
|---|---|---|---|
| Prices | > 0 | `Success` | Normal |
| Prices | 0 | `Success` | Quiet market — nothing moved |
| Nothing | 0 | `Partial` | Suspicious — possible schema drift |
| Threw | 0 | `Failed` | Hard failure |

`Partial` counts against the success-rate SLO, so repeated empty payloads breach it and alert.

Separately, the **volume-anomaly rule** compares today's rows against the trailing 7-day
median and warns below 50%. This catches the subtler case where a provider still returns
*some* data after a schema change — a partial break that never produces a single zero.

## Consequences

- A quiet Tuesday never pages anyone.
- A provider that starts returning empty payloads is caught within one evaluation cycle.
- A provider that starts returning *less* data is caught within a day by the volume rule.
- The volume rule needs at least three days of history before it will fire; with less it
  returns null rather than guessing, because a false alert on day one trains people to ignore
  the alerts.
