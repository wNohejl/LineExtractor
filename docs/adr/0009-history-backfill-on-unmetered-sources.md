# ADR 0009 — History comes from unmetered sources, and the code decides which

**Status:** Accepted

## Context

The scheduler is built for the present. A daily slate ingest, an intraday movement poll, and
yesterday's box scores keep the desk current, and that is the right shape for staying
current — but it means the platform can only ever learn one day per day. Every analytic that
needs a track record (CLV over a season, ROI by market, a team's form) starts empty and stays
empty for as long as it takes real time to pass.

The obvious fix is to walk backwards over past days. The obvious risk is that walking
backwards is, by construction, the one job whose shape is "issue a few thousand requests as
fast as you can" — against providers whose free tiers are the entire reason running costs are
zero. `CreditBudgetGuard` already refuses a *run* that would exceed a ceiling, but a backfill
that gets refused 1,400 times in a row has still burned the day's quota before it stops, and
the live schedule is what that quota is for.

## Decision

### Eligibility is a property of the source row, not a setting

A source may be backfilled only when it declares no rate limit, no daily limit and no monthly
credit budget. That is already this schema's definition of unmetered — `Source`'s limit fields
are documented as "null means unmetered" — so the rule adds no new concept, it just makes an
existing one load-bearing.

`Ingestion:Backfill:Sources` names which sources to walk, and it is **advisory**. A metered
provider listed there is refused, logged, and shown in the History window with the reason.
Configuration can narrow what the backfill does; it cannot widen it into spending.

This is deliberately stricter than "set it to `false` and don't change it". The cheapest place
to make an expensive mistake impossible is in the code that would make it, not in a JSON file
someone edits six months from now to fix something unrelated.

Today that admits exactly one real provider: **ESPN**, whose endpoints are unauthenticated and
unmetered. `balldontlie` (300/hour), `odds-api.io` (100/hour, 500/day) and The Odds API
(500 credits/month) are all refused, and the refusal is visible rather than silent.

### The walk is paced, resumable, and goes newest-first

- **Paced.** 600ms between day fetches by default. ESPN publishes no rate limit, which is a
  reason for more care rather than less: an unofficial endpoint used as a courtesy has no
  documented ceiling to stay under, so the only safe assumption is that we should be quiet.
- **Resumable.** Every completed `(source, sport, day)` is recorded in `BackfillCheckpoints`
  and skipped next time. **A day with no games still counts as completed** — without that,
  every offseason date would be re-fetched on every run forever, which is precisely the
  traffic the checkpoint exists to prevent. Failed days are recorded too, and re-walked only
  when `RetryFailedDays` is set, so one permanently-404 sport cannot stall the window.
- **Newest-first.** A long job that can be stopped at any moment should be ordered so that
  stopping early still leaves something useful. Recent history is what the analytics read;
  finishing "last month" beats getting a third of the way through a year that starts eleven
  months ago.
- **Gives up on a sick provider.** Eight consecutive failures against one source aborts that
  source. A provider that has started refusing us should be left alone, not retried 1,400
  times.

### Backfilled days are ordinary ingestion

The walk calls the same `StatsIngestionService` the scheduler does. It therefore writes the
same `IngestionRun` rows, passes the same budget guard, and uses the same upsert semantics —
so re-walking a day refreshes it rather than duplicating it, and a stalled backfill shows up
on the Ops dashboard as runs that stopped, with no new alerting to build.

### The coordinator outlives the window that starts it

A backfill takes minutes to hours; a Blazor circuit lasts until someone refreshes the tab.
`BackfillCoordinator` is a singleton hosted service, so the walk survives the window closing
and the History panel reattaches to a run already in progress. It is single-flight: starting
a second walk while one is active is refused, because two concurrent backfills would double
the request rate against exactly the provider this design is arranged to protect.

Auto-start (`Backfill:Enabled`) is **off** by default. A few thousand requests against a
courtesy endpoint should be a decision an operator makes, not something a fresh clone does on
its own. When it is on, it waits 30 seconds — the host has just migrated, seeded and possibly
run a startup ingest, and the backfill is the least urgent thing competing for that minute.

## Consequences

- History is stats-only: schedules, scores, box scores. **Historical odds are not
  backfillable for free.** Every provider that sells them meters them, and that is the point
  of the product's cost model rather than a gap in this feature — line movement accumulates
  going forward, from the intraday poll. CLV on past games stays unavailable until the games
  are ones the platform watched live.
- **A backfill is slow, and the cost is box scores rather than schedules.** A day-fetch is one
  scoreboard call *plus one call per finished game*, so a full MLB slate is sixteen requests,
  not one. Measured against live ESPN, 180 days x 4 sports is on the order of 3,000–5,000
  requests and runs for hours, not the ~7 minutes a naive "720 fetches" count suggests. This
  is why resumability and newest-first ordering are not niceties: the expected case is a walk
  that gets stopped and continued, possibly across restarts.
- That per-game fan-out is also why `EspnStatsAdapter` paces every call it makes rather than
  the backfill pacing only the gaps between days. Pacing at the day level would have left a
  slate day firing fifteen requests back to back, several hundred times over — the exact
  traffic pattern the delay exists to prevent.
- `BackfillCheckpoints` is small and never trimmed. At four sports and one source it grows by
  ~1,460 rows a year, which does not need a retention policy.
- The offseason reads as failure at a glance and is not: an NFL day in July legitimately
  returns zero games. The History window distinguishes *held* from *failed* for this reason,
  rather than showing a single "days processed" number that would hide a real outage behind a
  pile of legitimately empty days.
- Adding a second free stats provider is a config line plus an adapter — the eligibility check
  admits it automatically once its `Source` row declares no limits. Adding a *metered* one
  requires changing this ADR, which is the intended amount of friction.

## What the first real backfill found

Walking live ESPN surfaced three defects that the one-day-at-a-time scheduler had never
exercised. All three are fixed; the third is only contained.

1. **One DbContext for the whole walk.** The first implementation opened a single scope and
   held it for hundreds of days, so the change tracker accumulated every game, player and stat
   line it had ever seen and each day ran slower than the last. It looked exactly like a hang.
   The walk now takes a scope per day.

2. **`ux_player_game_stat` violations killed the entire run, not the day.** Two separate
   causes. ESPN reports a player once per stat *category*, so a batter who also fields arrived
   as two lines for one key — fixed by merging categories in the adapter under prefixed keys.
   And when the persist threw, the failure handler called `SaveChanges` again on a change
   tracker still holding the rows that had just been rejected, so it threw a second time, out
   past the run record and into the caller. One bad day therefore ended the backfill instead of
   being logged as a failed day. The handler now discards the pending work before recording the
   outcome.

3. **The unique key is the *resolved* `PlayerId`, not the provider's athlete id.**
   `UpsertPlayersAsync` falls back to matching on full name, so several ESPN athlete ids
   collapse onto one `Player` — 15 to 37 times on a typical MLB day. Deduplicating on source
   ids was therefore not enough; the batch is now keyed the way the constraint is, after
   resolution, and a repeat updates the row in flight. **This is contained rather than solved:
   colliding lines are merged, so the losing line's stats are discarded.** The real fix belongs
   in entity resolution — a name match should at minimum be scoped by team — and is tracked
   separately.

The general lesson is that backfill is a load test for ingestion. Every one of these was
reachable from the daily scheduler and none had been reached, because a defect that needs a
player with two stat categories, or a same-named teammate, or a few hundred sequential writes,
will not show up in a job that does four fetches a day.
