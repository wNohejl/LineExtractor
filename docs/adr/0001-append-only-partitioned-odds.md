# ADR 0001 — Odds are append-only and partitioned by month

**Status:** Accepted, amended by
[ADR 0010](0010-odds-are-scans-until-first-pitch-then-one-closing-line.md)

> Append-only and write-on-change still describe how the odds table is written. What has
> changed is how long it is kept: snapshots are now a scan tier that lives until a game starts,
> after which a single promoted `ClosingLine` is the permanent record. The consequences below
> about dropping old partitions still hold — they are simply exercised far sooner.

## Context

Odds change continuously. The obvious model is a row per (game, book, market, outcome) that
gets updated as prices move. That model cannot answer the question the platform exists to
answer: *did the price I took beat the price at kick-off?* Closing-line value needs the market
as it stood at a past instant, which an updated row has destroyed.

Line history is also the only table that grows without bound — every poll can append a row per
book, market and outcome, across four sports.

## Decision

`OddsSnapshot` is **append-only**: rows are inserted, never updated. "Current line" is a query
(`newest row per game/book/market/outcome`), not a column.

The table is a **native range-partitioned table**, partitioned monthly on `CapturedAt`, with a
`DEFAULT` partition so a row can never be rejected for falling outside every declared range.
A `lineops_ensure_odds_partition(timestamptz)` function creates partitions on demand, and the
worker calls it before each run so a month boundary is never a failed insert.

Prices are written **on change only**. If the newest stored observation for a key has the same
line and price, nothing is written.

## Consequences

- CLV, movement charts and "biggest movers" all become straightforward queries.
- Re-running an ingestion window is idempotent by construction, with no unique-constraint
  collisions to handle.
- Storage tracks market activity rather than poll frequency — roughly an order of magnitude
  less than storing every poll — and every stored row is a real line move, so charts show
  signal instead of sampling noise.
- Old months can be detached or dropped as a metadata operation instead of a mass `DELETE`.
- **Cost:** the primary key must include the partition key, so it is `(CapturedAt, Id)`. That
  means `OddsSnapshot` cannot be the target of a foreign key, which forced the decision in
  [ADR 0002](0002-clv-reference-is-not-a-foreign-key.md).
