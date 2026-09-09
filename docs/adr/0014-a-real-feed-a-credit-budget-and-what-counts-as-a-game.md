# ADR 0014 — A real feed, a credit budget, and what counts as a game

**Status:** Accepted

**Amended by:** [ADR 0017](0017-the-demo-fixture-retires.md), which deletes the demo fixture
outright and removes the `Sources` rows this ADR deliberately kept. Everything below about the
credit budget, exhibitions, and what counts as a game is unchanged.

**Builds on:** [ADR 0012](0012-jobs-are-named-and-triggered-by-state.md), which derived the line
cadence from the provider's allowance, and [ADR 0011](0011-espn-is-the-stats-port-odds-come-from-a-book-market.md),
which made ESPN the stats port.

## Context

The demo fixture came out and The Odds API went in. That is a configuration change on paper, and
it exposed three things that were only correct while nothing real was connected.

**The pacing understood requests, not credits.** ADR 0012 derived the cadence from
`RateLimitPerDay` and `RateLimitPerHour`. The Odds API publishes neither — its free tier is 500
credits a month and nothing else — so `PlanForAsync` returned null, the source read as unmetered,
and the interval fell to the five-minute politeness floor. At two credits a scan that is the whole
month gone inside a day, and eleven months of nothing after it. The budget guard would have caught
the overrun, which is precisely the failure ADR 0012 said a guard should not be used for.

**Not everything ESPN returns is a game.** The scoreboard answers "what happened on this date"
including things that did not count. Two shapes of exhibition, and neither announces itself the
same way:

- *Spring training.* 24 March 2026 returns events marked season type 1; 25 March returns opening
  day. Same query, same league, and the payload root reports "Regular Season" on both — because it
  describes the season the query fell in, not the game.
- *The All-Star Game.* Season type 2, on an ordinary July date, so a season-type filter passes it
  through. Its competitors are not franchises, so resolving it *created* two: "American All-Stars"
  and "National All-Stars" sat in the team list beside the thirty real ones.

**The results pass and its trigger disagreed.** The scheduler asked `DatesAwaitingResultsAsync`
which days were owed; the job then fetched a hardcoded yesterday. A day whose results were missed
kept the trigger permanently true and kept re-fetching a day that already had them.

## Decision

### Allowances are paced over the window they are measured in

`LinePollPlanner` gained a monthly-credit branch: credits remaining, less a reserve, divided by the
credits a scan costs, spread over the hours the month has left. Where a source declares both, the
slower governs — a daily allowance says nothing about whether the month can afford to spend it
every day.

The reserve does more work here than in the daily case. Manual and automatic pulls draw on one
pool, so a manual pull lengthens the automatic cadence by itself: the scheduler makes room for the
operator rather than racing them for the last credits.

`PollPlan` now carries which window it is talking about, because "200 scans left" reads as generous
against a day and as thin against a month, and the operator decides whether to spend a manual pull
on that basis.

### The market list is a cost control

A credit-billed call costs `markets x regions`. Asking for totals alongside moneyline and spread is
a standing 50% surcharge on every scan for the rest of the month, so the markets are configured
rather than assumed — moneyline and spread, at two credits a scan.

Configuration accepts what someone would actually write. The canonical key is `h2h`, which is the
provider's word and nobody's first guess; bound strictly, "moneyline" would reach the adapter
unrecognised and be forwarded verbatim, producing a call that still bills a credit and returns
nothing. Unknown markets are dropped instead, so a typo costs the market rather than the money.

**Region stays `us` rather than a book list.** Ten books cost the same as one, so naming four would
buy nothing and forgo the rest. The pull returns nine.

> **Superseded by the amendment below.** This reasoning is right about the price and wrong about
> what is being bought: naming books is not confined to one region, and the book worth having —
> Pinnacle — is not in `us` at all.

### A pull is per sport, because that is how it is billed

"Fresh lines" scanned every configured league at once. On a credit-billed provider that spends the
allowance on leagues that are out of season and quoting nothing. The menu now lists one entry per
sport, quoting credits rather than requests — the unit that runs out.

### Exhibitions are excluded at the boundary, by the signal that identifies them

Season type 1 is rejected, read from the *event* rather than the payload root. Competition type
`ALLSTAR` is rejected separately, because the season cannot distinguish it.

Both reject named values rather than accepting known ones. The costs are asymmetric: letting an
exhibition through adds a row that can be found and removed, while requiring a recognised type
would silently drop every postseason game the first time ESPN labelled one differently.

### The results pass walks the days it is owed

`RunResultsAsync` reads the same `DatesAwaitingResultsAsync` the scheduler triggers on, and sweeps
each. Nothing outstanding still means yesterday, so pressing the button by hand on an up-to-date
desk does something sensible. This is what makes the daily poll self-healing rather than merely
repetitive.

### Keys live in a file the repository cannot see

`appsettings.Local.json`, gitignored and loaded last. The environment-named files are *not*
ignored, so a key put in the obvious place goes into the repository with it. A committed
`.example` alongside documents the shape.

## Consequences

- **Credits are bounded by construction, not by refusal.** 500 less a 100 reserve, at 2 a scan, is
  200 scans spread across the month. Verified in the running app: the cadence read 35.1 minutes
  where the unmetered floor would have been 5, and a live pull billed exactly 2 credits.
- **Coverage is the provider's, not the schedule's.** The Odds API quotes games inside its own
  horizon — 12 on the day this landed — so a board reading "12 of 12 priced" is complete with
  respect to what is purchasable, not with respect to the slate. The distinction matters when the
  season's shape changes; ADR 0013's gap reasons are what keep it legible.
- **Every priced game carries both external ids.** `espn` and `the-odds-api` on one row, zero
  odds-only orphans across the first pull. That is the entity-resolution guarantee working on a
  real feed for the first time — matching on team name and start time, which is what it was always
  going to have to do.
- **Store-on-change makes a second pull look like a failure and is not.** The first scan wrote 406
  prices; a manual pull ninety seconds later wrote 6, and a third wrote none. Only the moves are
  stored, so a repeated pull costs credits and stores little — worth knowing before pressing it.
- Adding a sport is one entry in `Ingestion:Sports`, and costs one more per-sport pull entry at the
  same per-sport price. Adding a market is a config line and a permanent rise in the cost of every
  scan.
- `bet365` does not appear. It is not carried in the provider's `us` region — a fact about the
  market rather than a configuration to fix. Nor is it reachable by naming it: the amendment below
  names four books, and bet365 is not among what the key can see.

## What the change found

**Roughly a fifth of the stored MLB season had never counted.** 314 spring training games carrying
14,523 player stat lines, ingested before the filter existed. They are indistinguishable from real
games downstream — a real ESPN event with a real box score — so their effect was quiet and
specific: team records counting exhibition losses, and player form averaging March at-bats taken
against minor-league pitching alongside ones that mattered. Removing them also removed 1,792
players whose only appearances were exhibitions, which is the same invariant
`StatsIngestionService` enforces on the way in, applied to what was already stored.

**The demo source left more than prices behind.** Standing it down stops it writing; it does not
remove 2,822 fabricated prices sitting in the same table as real ones under a different source id,
where every reader downstream treats them alike. Nor the 32 fixtures it invented before ADR 0013
taught it to price the real schedule — out-of-season NBA, NFL and NHL matchups on a July slate,
and eight NFL teams that existed only to hold them.

**Two of the three purges were for damage a filter had already stopped.** The adapter now rejects
exhibitions at the boundary, but nothing retroactively cleans what was ingested before it did.
Both are kept as scripts in `scripts/` rather than run once and forgotten, because the reasoning —
what counts as a game, and why a row that cannot be reached should not exist — is the part worth
keeping.

## Amendment — the spine, the sharp book, and when to spend

Three changes, from investigating whether to pull books directly.

### Pinnacle did not need scraping

The plan was to scrape Pinnacle, Caesars, BetMGM and DraftKings directly, Pinnacle first — it is
the sharpest price and the lowest vig, so it is the reference the others are judged against rather
than one more number to shop.

It is carried by the aggregator already configured, in the `eu` region. Requesting that region
alongside `us` would double the bill on every call, but **the `bookmakers` parameter crosses
regions and bills as one**: ten books count as a single region unit, so
`bookmakers=pinnacle,draftkings,fanduel,betmgm` returns all four for the same 2 credits
`regions=us` was already costing. Verified against the live endpoint before it was wired.

That collapses the top of the scraping list into a configuration line, and with it the ToS
exposure, the geo-fenced egress, and four adapters to maintain against four undocumented schemas
that change without notice.

The trade is real and worth stating: nine books became four, and the aggregator's per-book latency
is whatever it is. The four are the ones that matter, and Pinnacle is worth more as a reference
than five recreational books are as alternatives.

### MLB's own ids are the spine

`statsapi.mlb.com` is free, unauthenticated, and issues the identifiers baseball actually runs
on — `gamePk`, and MLBAM team and player ids. Every other source resolves into those once, which
turns an N-way fuzzy match into N exact lookups.

Name-and-date matching has worked so far and is a heuristic. It has two failure cases it cannot
reason about, and both are ordinary: a **doubleheader** is two games between the same teams on the
same day, which resolves to one game and silently merges two slates of prices; and MLB labels
exhibitions in a single `gameType` field where ESPN needs two independent signals — a season type
for spring training and a competition type for the All-Star Game — to say the same thing.

It also carries **probable pitchers**, hydrated with player ids, on the same free request. A
scratch invalidates the game's pricing and every pitcher prop attached to it, and it is knowable
here hours before a book takes the market down.

Registered as itself rather than as an `IStatsSource`: it issues identity, and box scores stay
ESPN's job (ADR 0011). Nothing competes for the stats slot, so it cannot displace what works.

### Timing is redistribution, not more spending

Prices now carry the book's own `last_update` rather than our fetch time. Stamping the fetch made
every price in a scan simultaneous and made the gap between scans look like the gap between
moves — so the movement chart recorded our polling, not the market.

The cadence tiers by proximity to first pitch: roughly four times slower than the even spread more
than a day out, and around a third of it inside three hours, when lineups are posted and the
market does most of its price discovery. This multiplies the budgeted interval rather than
replacing it, and because the budget recomputes from credits *actually spent* every tick, spending
sooner slows the rest by itself. The allowance cannot be exceeded by being spent impatiently, only
earlier. `PollPlan` reports both numbers, because "what can be afforded" and "when it is worth
spending" are different questions.

## What the amendment found

**The demo fixture's price balancer flattened the disagreement it existed to show.** There is no
such American price as -50: the scale runs -101, -100, +100, +101, so the gap between is not used.
`Balance` clamped anything inside it to the nearer edge, which maps every near-even price onto
exactly two numbers — four books quoting four different prices came out identical, more often the
closer a game was to a coin flip. Adding 200 steps across the gap instead, which is continuous and
order-preserving.

**Two "stable" hashes were seeded per process.** `string.GetHashCode` and `HashCode.Combine` are
randomized per run, so a book's fixed lean and a fixture's base price changed on every restart —
a movement chart recording deployments rather than markets. Both now use FNV-1a. This is what made
the failing test intermittent rather than simply failing.

**The urgency query had no sport filter**, so a hockey game an hour out would have spent baseball's
allowance faster. Caught because the tests could not control it: the planner had started reading
ambient slate state that the budget tests did not own. Splitting `BudgetInterval` from `Interval`
made both testable and separated two things that had quietly become one.

**Correcting the arithmetic this was planned against:** the key is on the free tier — 500 credits a
month, not the 20,000 of the paid one. The 220-sweeps-a-day figure needs that upgrade. At 500,
after a 100-credit reserve, it is 200 sweeps for the *month*, which is what makes tiering by first
pitch worth the code rather than a refinement.
