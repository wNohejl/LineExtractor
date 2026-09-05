# LineOps — working rules

These rules exist because the repository spent a month with twenty-three agent worktrees,
thirty-three branches, Claude's name in sixty commit messages, and a Worker host that could
not start. The desk was good; the git management was not. Keep it this way instead.

## Branches: two, and only two

- `main` — the default branch. Stable.
- `LineX_Development` — where all development lands. Work here directly, or on a short-lived
  branch that is merged into `LineX_Development` **and deleted** in the same sitting.
- Nothing else persists. No agent worktrees left behind (`git worktree list` should show one
  entry), no `worktree-agent-*` branches, no stashes parked "for later", no tags bookmarking
  old tips. If a piece of work is worth keeping it is worth a commit on `LineX_Development`.
- Push `LineX_Development` after every commit. The laptop and the desktop both clone from
  origin; an unpushed branch is a branch that exists on one machine.

## Authorship

- Commits are authored and committed by the developer's own git identity. **Never** add a
  `Co-Authored-By: Claude …` trailer, and never commit with a Claude committer identity. The
  history was rewritten once to remove sixty of these; it will not be rewritten again.
- Commit messages follow the repository's voice: a `type(scope): what changed, as a sentence`
  subject and a body that says why. Read `git log` for the register.

## Data: the desk targets one dataset from any machine

The database is a local Docker Postgres. To develop from another machine against the same
games, stats and closing lines, the data travels with the code as a committed snapshot:

```powershell
.\scripts\publish-data.ps1 -Commit    # on the machine that has the data: dump, commit, push
.\scripts\restore-data.ps1            # on the other machine: start Postgres, restore
```

The snapshot lives at `data/snapshots/lineops.dump` (custom-format pg_dump, ~2–3 MB) with a
manifest beside it saying what it holds. Refresh it whenever the data moves in a way the other
machine should see — after a backfill, after a season closes. `restore-data.ps1` refuses to
overwrite a database that already holds games unless told to with `-Force`.

Laptop bootstrap, from nothing:

```powershell
git clone https://github.com/wNohejl/LineExtractor.git LineOps; cd LineOps
git checkout LineX_Development
.\setup.ps1                              # generates .env with the database password
.\scripts\restore-data.ps1               # brings Postgres up and loads the snapshot
dotnet run --project src/LineOps.Web --launch-profile http
```

## Scope

- Two leagues: **MLB and NFL**. `Ingestion:Sports` and `Ingestion:Backfill:Sports` name them;
  `Sports.Enabled` hides the rest from the desk's pickers. Do not add a league without a
  season start in `Ingestion:Backfill:Seasons`.
- Odds come from a book market (ADR 0011); ESPN's closing line fills games no market covered.
  Where ESPN has no line — early-season history — there is no line, and nothing fabricates one.
- Every game carries `SeasonYear` and `SeasonType`, stamped by the provider. Filter by season,
  not by a rolling day count, wherever a reader is about a team's or player's record.

## Verification before "done"

- `dotnet build` clean on `src/LineOps.Web` and both test projects; `dotnet test` green.
- The Worker host starts (`dotnet run --project src/LineOps.Worker`); its `appsettings.json`
  comment keys must be unique (`"//1"`, `"//2"` …) or the configuration provider refuses it.
- For data changes: the coverage queries in
  `docs/superpowers/specs/2026-09-04-mlb-nfl-seasons-research.md` §2 — finals without stats
  and stuck Live games should both be zero for MLB.
