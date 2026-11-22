# ADR 0025 — The journal merges, and says what changed

**Status:** Accepted. Built 2026-11-15 and merged into `LineX_Development`; the journal has travelled through the private repository since 2026-11-17.

**Builds on:** [ADR 0018](0018-an-operator-and-visitors.md), whose operator is the one person the
journal belongs to, and [ADR 0023](0023-one-database-two-desks.md), whose "separate development"
option — a Postgres on each machine — is the case this ADR serves.

## Context

The developer works on a desktop and a laptop, each with its own Docker Postgres. Market data
travels as the committed snapshot (`publish-data.ps1`, `restore-data.ps1`). The journal does not,
because the repository is public: a bet log is not market data. On 2026-11-15 a second pair of
scripts carried it by hand, as a data-only `pg_dump` of `JournalEntries`, `Parlays` and
`AppSettings` written to the git-ignored `data/personal/` and loaded by truncate-and-replace.

That pair had three defects.

- **It replaced; it never merged.** A bet logged on the laptop and another on the desktop could
  not both survive. Whichever file was loaded last won.
- **It was keyed by this database's integers.** An entry's id is an identity column, and its
  `GameId` and `PlayerId` are this database's row ids. They line up only while both machines hold
  the same restored snapshot and neither has ingested since. Once the laptop pulls a day itself,
  its games are numbered differently, and a journal loaded there points at the wrong games or
  fails its foreign keys.
- **Nothing was recorded.** There was no trace of what an entry was before an edit, and no way to
  ask when this machine last published or took the data.

## Decision

**An entry has one identity on every machine.** `JournalEntry.SyncId` is a uuid made when the
entry is created, unique and never changed. A parlay already had one: its id is a uuid made by
the application, not the database. Every entry, parlay and operator setting carries
`UpdatedAtUtc`, which is what two copies of a row are ordered by.

Migration `JournalSync` gives existing entries a `SyncId` hashed from their row id and placement
time, not a random one. Both machines migrate the same journal separately (yesterday's dump
carried the row ids across), and a random uuid on each would make every existing bet two bets
to the merge. Their `UpdatedAtUtc` is set from when they settled or were placed, again so that
two copies migrated at different times agree. Settings take one fixed moment older than any real
write.

**A deletion is a row: `JournalTombstones(SyncId, Kind, DeletedAtUtc)`, not a soft-delete
column.** A merge must tell "deleted here" from "never seen here", or every import would bring a
deleted bet back. A soft-delete column would do that, but every reader of `JournalEntries` would
then have to learn to skip a deleted row: the Journal, Performance, settlement, the game window,
the duplicate check, the books picker, and every one written later. A missed reader would show a
deleted bet as live and count its stake. The database's own cascade from a parlay to its legs
would also stop applying. A separate table changes no reader. Its cost is that a deleted row's
content is gone, and the audit below keeps that.

**One interceptor stamps and records every journal write.** `JournalAuditInterceptor` runs on
every `SaveChanges`. It stamps `UpdatedAtUtc` on an added or changed entry, parlay or setting. It
appends a `JournalAudit` row for each entry and parlay — action (`Created`, `Updated`, `Deleted`,
`Imported`), time, `Environment.MachineName`, and the columns before and after as jsonb. It
writes a tombstone for each deletion. The context adds the interceptor to itself in
`OnConfiguring`, so the form, settlement, an edit, a delete, an import, a test's context and
`dotnet ef` all pass through it, and no caller can forget. An assignment that changes nothing is
not stamped. Nothing on the desk reads the audit yet; it is there for the day an entry is not
what the operator remembers. Settings are stamped but not audited.

**This machine logs what it moved: `DataTransfers(Kind, AtUtc, Machine, Detail)`.** The journal
jobs write `JournalExported` and `JournalImported`, with counts and the file's name.
`publish-data.ps1` and `restore-data.ps1` add `SnapshotPublished` and `SnapshotRestored` at the
end, with the manifest. A restore drops the schema and the table with it, so its row is written
after the restore. On a database from before the migration, the insert is skipped without a word.

**The journal travels as a file that merges.** `--job journal:export --out <file>` writes one JSON
document, `"format": 1`, holding entries, parlays, tombstones and settings. An entry names its
game the way providers do: the `ExternalIds` ESPN, MLB and The Odds API gave it. Beside those it
carries the league, the start in UTC, and both sides with their own provider ids and names. A
player is named the same way. No row id is in the file.

`--job journal:import --in <file> [--dry-run true]` resolves those names here, decides each row
with `JournalMerge`, and writes everything in one transaction. `JournalMerge` is a set of pure
functions with their own tests.

- Rows are matched by `SyncId`. Identical content is unchanged, whatever the timestamps say.
  Otherwise the newer `UpdatedAtUtc` wins the whole row. Two different copies from the same
  microsecond are a conflict: reported and left as they are here.
- A tombstone newer than the local row deletes it. A local row changed after the tombstone
  survives it. A row the file holds that was deleted here earlier comes back, and the local
  tombstone goes with it.
- A game is found by a provider id first. The fallback is the same league, both sides, and a
  start within 12 hours, and it is used only when exactly one game fits. A game that cannot be
  found is not guessed: a wrong game would grade the bet against someone else's score.
  `GameId` and `PlayerId` are nullable, as they always were for a bet on something the desk does
  not track, so the entry is imported without the reference and the report names the game.
  Imported again after that day is pulled, the same file links it: a copy that differs only by a
  link this one lacks is taken whatever the timestamps say. A parlay leg whose parlay is not here
  is skipped and named.
- Importing the same file twice writes no journal row the second time. The transfer log still
  records that the import ran.
- `--dry-run true` prints the plan — new, updated, deleted, unchanged, conflicts, unresolved, for
  entries, parlays and settings — and writes nothing.

The export is refused if its path is inside a repository that does not ignore it. The Worker
asks git, and fails closed if git cannot answer. The polling switch (`odds.line_polling`) does
not travel: it decides whether this machine's scheduler spends credits by itself, and a file from
the other machine must never switch that on. The close's row id (`ClosingSnapshotId`) is per
database too. The file carries the close's values, which are what CLV reads, and the import looks
up this database's own row for that close.

**The scripts become wrappers; the dump pair is retired, not kept as a fallback.**
`publish-journal.ps1` and `restore-journal.ps1` keep their names, `data/personal/` and the
git-ignore refusal, and now run the two jobs (`journal.json`; `-DryRun` on the restore). A
database that predates the migration needs no fallback: the Worker migrates the database before
any job, as the desk does when it starts. Keeping the dump would keep a second path that
replaces instead of merging. A leftover `journal.dump` is named and turned away, with the
instruction to export again from the machine that has the journal.

## Consequences

- A bet logged on each machine survives on both. An entry's game is found on a laptop that
  numbers its games differently, and a game the laptop has not pulled is named rather than
  guessed.
- The merge is last-writer-wins by whole row. If the laptop edits a note and the desktop settles
  the same entry later, the desktop's copy wins and the note is lost. Settlement is a write and
  is stamped. A field-level merge would fix this. It is not worth its complexity for a journal of
  hundreds of rows edited by one person, and the audit holds the lost note.
- Order is by each machine's clock. A laptop clock running minutes behind can lose an edit it
  made last. Both machines sync time from the network, and a tie is reported, not resolved.
- Settings carry no tombstones: a setting removed on one machine is not removed on the other.
  Settings are few, and their one removal (the polling switch going back to configuration) does
  not travel anyway.
- `restore-data.ps1` still drops the schema, journal included; its refusal now says to publish
  the journal first and restore it after. A machine's transfer log is lost with it, and the
  restore's own row is the first of the new log.
- The audit only grows. At a few rows per bet it stays small. Pruning it, and a window that reads
  it, are later work.
- Bulk `ExecuteUpdate` and `ExecuteDelete` bypass the interceptor. Nothing uses them on the
  journal today, and a change that does must write its own audit and tombstones.
