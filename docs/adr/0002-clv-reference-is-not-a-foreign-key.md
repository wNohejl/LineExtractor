# ADR 0002 — The closing-snapshot reference is not a foreign key

**Status:** Accepted

## Context

A settled journal entry points at the snapshot that represented the closing price. The natural
model is a foreign key from `JournalEntry` to `OddsSnapshot`.

PostgreSQL will not allow it. A foreign key needs a unique constraint on the target, and every
unique constraint on a partitioned table must include the partition key. `OddsSnapshot`'s key
is therefore `(CapturedAt, Id)`, and there is no unique index on `Id` alone to reference.

## Decision

`JournalEntry` stores three plain columns instead of a navigation property:

- `ClosingSnapshotId` — which observation was chosen;
- `ClosingCapturedAt` — the partition key, so a lookup prunes to a single partition;
- `ClosingPrice` — the price itself, denormalised at resolution time.

## Consequences

- Referential integrity for this link is enforced by the settlement service rather than the
  database. Acceptable: the reference is written once, by one code path, and never edited.
- Storing `ClosingPrice` means **CLV survives partition pruning**. When a 2023 partition is
  eventually dropped, entries from 2023 keep their computed CLV instead of silently losing it.
  This turned out to be the more important benefit, not a workaround.
- Reads carry the partition key, so a closing-price lookup touches one partition rather than
  scanning every month of history.
