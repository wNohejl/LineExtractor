# ADR 0017 — The demo fixture retires, and a keyless clone says so

**Status:** Accepted

**Amends:** [ADR 0014](0014-a-real-feed-a-credit-budget-and-what-counts-as-a-game.md), which took
the demo fixture out of service when The Odds API went in and purged what it had written. The
fixture is now removed from the codebase, and the source rows it seeded are removed with it.

**Builds on:** [ADR 0003](0003-two-kinds-of-zero.md), whose reading of an unconfigured source is
what makes an empty odds port legible rather than alarming, and
[ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md), which made ESPN the
keyless stats port this now leans on entirely.

## Context

The demo fixture existed so a cold clone ran with no keys and no spend. It priced the real
schedule with plausible numbers that drifted between polls, and invented rosters and box scores
beside them, so movement charts, CLV, settlement and the on-call drills all worked for a reviewer
who had signed up for nothing. It yielded automatically, per kind — a keyed odds provider meant no
demo odds, ESPN being on meant no demo stats — so there was nothing to remember to switch off.

That was a good arrangement while nothing real was connected, and it stopped being one the moment
something was. Three things had gone wrong with it, in increasing order of seriousness.

**Standing down is not the same as being gone.** ADR 0014 recorded this once already: the fixture
stopped writing and left 2,822 fabricated prices and 32 invented fixtures behind, in the same
tables as real rows under a different source id, where every reader downstream treats them alike.
The purge script cleaned the data. It deliberately left the `Sources` rows, on the reasoning that
historical `IngestionRun` rows referenced them and deleting the source would rewrite the past to
say those runs never happened.

**The source rows outlived their usefulness and kept talking.** The reliability layer reads its
subjects from `Sources`, so a row that is disabled but present is a row still being evaluated the
next time it is enabled — and a row nobody can explain. On the desk this showed as a "Demo stats
fixture" source sitting STALE at 35.1 days and holding an open CRITICAL freshness alert. The
platform has one critical severity, and it was spending it on a feed that had been fabricating
its own data and had then stopped. An alert that cannot be acted on is worse than no alert: it
teaches the operator that criticals are furniture.

**A fixture is a permanent invitation to compare fake numbers with real ones.** Every guard around
it — the per-kind yield, the config flag, the purge script — was a guard against mixing. The
guards worked. But a system whose correctness depends on a fixture continuing to stand aside is
carrying a hazard it does not need once it has a real feed.

## Decision

### The fixture comes out

`DemoOddsSource` and `DemoStatsSource` are deleted, along with `IngestionOptions.Demo`, both DI
registrations, the `realOdds` / `realStats` yield logic they were gated on, and the `Demo` blocks
in every `appsettings`. Deleted, not set to `Enabled = false`: a disabled fixture is a fixture
one config line away from writing fabricated prices into a database that now holds real ones.

Two things followed it out because it was the only reason they existed.

`IScheduleReader` and `ScheduleReader` were introduced by [ADR 0013](0013-the-board-and-the-floating-layer.md)
so the fixture could quote the real slate instead of inventing a parallel one. Real providers
publish their own event lists and need nothing from us, so with the fixture gone the interface had
no caller — an abstraction whose entire justification had been deleted.

`OddsProviderState.IsReal` existed to let the Ops panel mark one row in the feed table as not a
provider. Every registered feed is real by construction now, so `IsLive` is the whole question,
and the panel's State column is a two-way branch again.

The `appsettings` guidance is rewritten rather than removed. What a cold clone needs is still the
question a fresh reader asks; only the answer has changed, and a config file that says nothing
about it makes them read source to find out.

### Retired sources leave the database they seeded

`DatabaseInitializer.RemoveRetiredSourcesAsync` deletes the `demo` and `demo-stats` source rows
and everything recorded against them — closing lines, odds snapshots, player stats, stat
snapshots, backfill checkpoints, ingestion runs, KPI dailies, alerts — in foreign-key order, on
every start.

It is a startup path rather than a migration on purpose. A migration runs once per database and is
then a file nobody reads; this is idempotent by construction — it resolves the ids first and
returns immediately when there are none — so it costs a lookup on every boot and self-heals any
clone, at any point in its history, without anyone remembering that it needs to. That is the
pattern for retiring any source, and `RetiredSourceKeys` is where the next one goes.

ADR 0014's reasoning for keeping the row is answered rather than ignored. Deleting a source does
lose the runs that referenced it — and those runs are the record of a fixture ingesting fabricated
data. There is no operational question the reliability layer can be asked about them whose honest
answer is not "that source was never real." Keeping the row to preserve that history bought a
permanently unactionable KPI subject.

Two things are deliberately left behind, and the distinction between them and everything above is
the same distinction each time: **rows the platform wrote about itself go; rows a person wrote
stay.**

- **Fixtures the demo source invented**, from before ADR 0013 taught it to price the real
  schedule — a game carrying a `demo` external id and no `espn` one. Removing a game takes its
  journal entries with it, and deleting what someone recorded about their own wagering is their
  decision, not a startup path's. `scripts/purge-demo-data.sql` still does that, by hand, and is
  now the only thing it does.
- **Incidents.** An incident exists to carry a written root-cause analysis; the analysis is the
  point of it, and ADR 0003's discipline is that every real failure gets one. Its alerts go, so
  nothing is still *open* about a source that no longer exists, but the write-up stays where its
  author left it.

### A keyless clone reads as not-configured, not as breached

With the fixture gone, a clone with no odds key has **zero** odds sources. That has to read as a
thing not set up rather than a thing broken, or the first experience of the platform is a false
alarm — which is the failure ADR 0003 exists to prevent, arriving from the other direction.

Three seams already say it, and none needed changing:

1. `AlertEngine` skips any source where `NeverRun` — "a source that has never run is not stale, it
   is unconfigured." That rule was written for a fresh clone and it is now doing exactly the job
   it was written for.
2. `AlertEngine` iterates only enabled `Sources`. The demo rows are gone, so there is no subject
   left to raise the alert that prompted this.
3. **Ops → Odds feeds** lists every real provider with `Off` and a reason — "Enabled, but no API
   key. Supply one out of band" — because an odds provider's failure mode is silence, and a
   provider that was never registered writes no runs and appears nowhere in source health.
   Distinguishing "no prices" from "no feed" is the whole reason that section exists separately
   from the health table.

That the answer was already three seams rather than a new rule is the point worth recording. The
doctrine was in place; removing the fixture is what made it load-bearing.

## Consequences

- **A cold clone still runs, and now says what it is.** ESPN is keyless (ADR 0011) and the MLB
  Stats API is unauthenticated, so real fixtures, real results, real box scores and season history
  all flow with no signup. Prices need a key, and the desk says so in three places rather than
  filling the gap with something invented.

- **The reviewer experience is genuinely worse in one respect, and this is the accepted trade.**
  A reader cloning the repo with no keys sees empty price cells where they used to see a working
  board. Movement charts, CLV and the settlement path have real code and real tests behind them,
  but nothing to display until a key goes in. The alternative was a desk that demonstrates its
  features with data it made up, which is a worse thing to show than an honest gap.

- **Failure-injection drills now cost a real request.** `IFailureInjectable` and the drill UI are
  unchanged, but the fixture was their most convenient subject — it could be told to throw, to
  time out, or to return HTTP 200 with nothing in it, for free. Driving the empty-payload drill
  that ADR 0003's rules are written against now means doing it against a metered provider.

- **`RemoveRetiredSourcesAsync` runs on a partitioned table.** The `OddsSnapshots` delete crosses
  every monthly partition. On a database carrying a long fixture history this is the one step that
  could take a noticeable moment at startup — once, because the second boot finds no ids and
  returns before touching anything.

- **The demo source stays in ADRs 0010, 0011, 0013 and 0014, unamended.** Those record decisions
  as they were taken, and each mentions the fixture in passing while deciding something else —
  retention windows, which port owns stats, what the board compares, how a real feed was brought
  in. Adding an amendment line to all four would put a pointer to this ADR on four documents whose
  reasoning this one does not change, which is noise in a log whose value is that a cross-reference
  means something. 0014 is amended because the fixture's disposal is a decision it actually made.

- **The next reader's question is answered in `DESIGN.md` §11, not by silence.** The register row
  reads "Demo fixture sources on by default — then removed again", with both halves of the
  reasoning, because a design document that quietly drops a feature reads as though it was never
  considered.

## What the change found

**The dead abstraction was invisible until its consumer went.** `IScheduleReader` had a
registration, an implementation, a documented rationale and a test stub, and nothing called it
once `DemoOddsSource` was deleted. Nothing failed — a registered service with no consumer is
perfectly legal, builds clean, and would have sat there indefinitely. Finding it took grepping for
the interface after the deletion rather than before it, which is a step worth doing on any removal
of this size: the thing being removed is rarely the only thing that was only there for it.

**Six tests were deleted rather than rewritten, and that needed arguing for rather than assuming.**
`DemoOddsSourceTests` asserted that the fixture quoted the real schedule, invented no games of its
own, priced every fixture with every book, made books disagree, kept a fixture's identity stable
across scans, and yielded nothing when a fault was injected. Every one is a fact about a class
that no longer exists; there is no real-source equivalent to point them at, because a real
provider publishes its own events and none of those questions apply to it. The four
`IngestionOptionsBindingTests` facts about source selection were **rewritten**, not deleted,
because the question they ask — what registers with what configured — is still live and its answer
changed. Deleting a test to make a suite green and deleting one whose subject has gone look
identical in a diff; the difference is whether a real-source version of the assertion exists.

**Prose outlives the thing it describes, in more places than a grep for the type name reaches.**
The word "demo" was load-bearing in a `PriceCell` book monogram, a bUnit `InlineData` case, an
empty-state paragraph on the dashboard, a comment in `StatsIngestionService` explaining why players
are resolved to games first, an infrastructure table row, and a compose-file bullet — none of them
compiled against anything that was deleted, and all of them would have gone on describing a source
that no longer exists. This is the same lesson ADR 0016 recorded about retired design tokens
hiding in `style=` attributes, arriving from a different direction: a removal is finished when the
documentation stops describing the removed thing, not when the build goes green.
