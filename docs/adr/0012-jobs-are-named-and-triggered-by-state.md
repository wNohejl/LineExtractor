# ADR 0012 — Jobs are named, and triggered by state rather than the clock

**Status:** Accepted

**Amends:** the schedule in §4 of the project spec — daily 09:00 plus intraday every three hours.

## Context

Three things were wrong at once, and they shared a cause: ingestion had no vocabulary. There
was work the scheduler did on a timer and work a button did, and neither could name what it
was fetching.

**The manual control was useless.** One button, "Ingest now", ran every odds source across
every sport and nothing else. It could not fetch today's games or last night's results — the
two things an operator actually reaches for — and gave no indication of what pressing it would
spend. It also read `Sports` directly, which had recently been defaulted to empty, so by the
time this was reviewed the button ingested *nothing at all* and said so nowhere.

**The line cadence ignored the budget.** Every three hours is not a plan; it is a number that
happens to be inside one provider's tier and outside another's. `CreditBudgetGuard` was left as
the only thing standing between the schedule and an overrun, and a guard refusing runs is a
failure state being used as a control loop.

**"After the game" was approximated by "tomorrow".** Box scores were fetched once, at 09:00,
for the previous day. A game finishing at 22:00 therefore sat ungraded for eleven hours, and
its journal entries with it.

## Decision

### A job catalog, shared by both paths

`IngestionJobs` names the pulls this platform can make — today's games, yesterday's results,
fresh lines, settle — and each declares its estimated request cost before it runs. The desk's
**Pull data** menu lists them with those costs; the scheduler runs the same entries.

That sharing is the point rather than a convenience. Manual and automatic ingestion were
previously two implementations of overlapping work, which is how the button ended up unable to
fetch stats at all. One catalog means a job fixed for one path is fixed for both, and the price
of an action is visible before it is taken rather than discovered in the run log.

A slate pass and a results pass differ only in date and in whether per-game summaries are
worth fetching, so `StatsIngestionService` grew an explicit schedules-only mode. Before the
games are played there is nothing to summarise, and a box-score pass costs one request per
finished game on top of the scoreboard — so the two are very different prices and only one is
worth paying twice a day.

### The line cadence is derived from what is left

`LinePollPlanner` computes the interval: the provider's daily ceiling, minus a reserve, minus
what has already been spent, divided by the cost of one scan, spread across the hours
remaining. Where several providers are live the tightest governs, because a scan is one action
across all of them.

Two bounds. A **reserve** is held back so a manual pull late in the day is never refused — the
guard should be a backstop, not a routine outcome. A **floor** stops a generous tier producing
a scan every few seconds, because books do not reprice that fast and past a point the requests
buy sampling noise rather than movement.

The cadence is shown in Ops, computed there from the same function rather than read off the
scheduler, so the number on screen cannot drift from the one in force.

### ESPN is fetched around games

The slate refreshes when the next start falls inside `PreGameLead`, rate-limited by
`SlateRefresh`. Results are swept once a started game is old enough to have finished, retried
until it is final here, and settlement runs in the same tick.

Both are driven by asking the database what state the games are in. A clock-based job either
runs when nothing has changed or misses the change; game state is the thing that actually makes
either pass due.

## Consequences

- The 09:00 daily job is gone, along with `DailySlateAt` and `IntradayInterval`. Nothing
  happens at a fixed hour any more.
- **Cost is now stated twice** — as an estimate in the menu, and as the real figure on the
  `IngestionRun` row. The estimate for a results pass is a floor, not a promise: it counts the
  scoreboard calls but not the per-game summaries, which depend on how many games finished.
- A quiet slate spends nothing. No game inside the movement window means no scan, whatever the
  cadence says.
- With no metered provider configured the planner has nothing to pace against and returns the
  floor, correctly labelled on screen as a politeness limit rather than a budget.
- `RequestsPerSportPerScan` is configuration rather than something the adapter reports. It is
  two for odds-api.io because a slate is `/events` plus one batched `/odds/multi`; a provider
  that batched differently would need this changed, and would be better served by adapters
  declaring their own scan cost.

## Amendment — a fourth pass, while the game is being played

State-triggered as designed, but state was checked at the wrong grain: "is a game about to
start" and "has a started game had long enough to finish" left the middle uncovered. A game an
hour into play was neither due for a pre-game refresh — it has no future start left to
protect — nor owed as a result — it is nowhere near `ResultsAfterStart` yet. Nothing polled it,
so a score sat at whatever the last pre-game scoreboard call happened to show until the results
sweep caught up, hours later.

`LivePollIsDueAsync` closes the gap: any game whose start time has passed but which is not yet
final or postponed is due on its own short interval (`LivePoll`, 90s), independent of the
pre-game and results cadences. It reuses the existing `EspnSlate` job rather than adding a new
one — the scoreboard call already carries live scores per event (`EntityResolver.ResolveGameAsync`
already refreshed them unconditionally; nothing had asked it to, mid-game). Keyed on start time
passing rather than on the `Live` status already stored, because a game that just started is not
marked live in our data yet — detecting that is exactly what the next poll is for.

## What the review found

Reviewing against the design is what surfaced the dead button. `Sports` had been defaulted to
empty in the previous session — correctly, because a non-empty array default cannot be narrowed
from configuration — and two call sites still read `Sports` rather than `EffectiveSports`. Both
were manual actions, so nothing failed and no test covered them: the button simply looped zero
times and reported success. A default changed for good reasons in one layer went silently wrong
in another, which is the argument for reviewing the whole path after a change of that kind
rather than only the code that changed.
