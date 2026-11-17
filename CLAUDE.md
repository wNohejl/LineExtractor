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
manifest beside it saying what it holds. It leaves out the journal, parlays and operator
settings (`-IncludePersonal` to keep them), and the script refuses a dump that holds them. The
scripts run under Windows PowerShell 5.1 or PowerShell 7 on any OS: Postgres's tools run in a
sidecar container, so nothing binary crosses a shell pipe.

The project stays local (going-public plan §10b). Both machines may use the same Odds API key
with polling on: credit pacing takes the provider's reported remainder, not just this
database's own count, so the month is not spent twice. Refresh it whenever the data moves in a way the other
machine should see — after a backfill, after a season closes. `restore-data.ps1` refuses to
overwrite a database that already holds games unless told to with `-Force`.

A league's games over any range of days, with box scores and closing lines (≤200 days; complete days skipped unless `--refresh true`): `dotnet run --project src/LineOps.Worker -- --job espn:range --sport nfl --from 2024-09-01 --to 2024-09-30` — or the Board's Date range key on the desk.

**Setting up on anyone else's machine** (a first run, "get it running", an API key): use the
`lineops-setup` skill (`.claude/skills/lineops-setup/SKILL.md`), the README's Quick start for an
agent. Prices come from **The Odds API** (`the-odds-api.com`, free Starter plan of 500 credits a
month, sign-up at <https://the-odds-api.com/#get-access>), not odds-api.io. The private journal
below is the owner's alone; skip it.

The owner's laptop bootstrap, from nothing:

```powershell
git clone https://github.com/wNohejl/LineExtractor.git LineOps; cd LineOps
git checkout LineX_Development
.\scripts\setup.ps1                      # generates .env with the database password
.\scripts\restore-data.ps1               # brings Postgres up and loads the snapshot
git clone https://github.com/wNohejl/lineops-journal.git data/personal   # private
.\scripts\restore-journal.ps1
dotnet run --project src/LineOps.Web --launch-profile http   # or the Docker desk, below, if Smart App Control is on
```

### The journal: the private repo `lineops-journal`

The journal, parlays and operator settings travel through the **private** repository
`https://github.com/wNohejl/lineops-journal.git`, never through this public one (ADR 0025). It
holds one file, `journal.json` (games named by provider id, so it merges into any database
holding those games). Clone it at `data/personal`, where the journal scripts read and write by
default; `.gitignore` keeps that folder out of this repository, and nothing in it is ever
committed here. (A clone at `./lineops-journal`, also ignored, works with `-OutFile`/`-InFile`.)

```powershell
# On the machine whose journal is newest: export into the clone, then commit and push it there.
.\scripts\publish-journal.ps1
git -C data/personal commit -am "journal: export <date>, <n> entries"; git -C data/personal push

# On the other machine: data first, then the journal. In this order, always.
git -C data/personal pull
.\scripts\restore-data.ps1 -Force
.\scripts\restore-journal.ps1 -DryRun   # read the report
.\scripts\restore-journal.ps1
```

`restore-data.ps1` drops and recreates the whole schema, journal included: a data restore run
after the journal merge erases it, and the merge must be run again. The merge itself is
idempotent (a second run reports everything unchanged), so re-running it is always safe. The
journal scripts run the Worker on the host (`dotnet run`); where that is blocked (next section),
run the same job in the worker image with the clone mounted (`--build` if the image is older
than the code; drop `--dry-run true` to write):

```powershell
docker compose build worker
docker compose run --rm --no-deps -v "${PWD}\data\personal:/journal:ro" worker `
  --job journal:import --in /journal/journal.json --dry-run true
```

If a script "cannot be loaded because running scripts is disabled", run it with the policy
bypassed for that process only; do not change the machine's execution policy:
`powershell -ExecutionPolicy Bypass -File .\scripts\restore-data.ps1 -Force`.

### A machine with Smart App Control on: the desk runs in Docker

Windows 11's Smart App Control is on only on a fresh install, and it blocks freshly built,
unsigned DLLs by reputation: `dotnet run`, `dotnet test` and Visual Studio's F5 fail with
`FileLoadException … An Application Control policy has blocked this file (0x800711C7)` on
`LineOps.Core.dll` or another of ours. Nothing is wrong with the code; the desktop has it off
and runs on the host. Check it with
`(Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy').VerifiedAndReputablePolicyState`
(1 on, 0 off). Turning it off is the developer's decision, never an agent's: it cannot be turned
back on without reinstalling Windows. Until then, on that machine:

```powershell
# The desk, at https://localhost:9443 (built from this checkout; --build after a code change).
# Always both files: without compose.dev.yml Postgres is recreated without 127.0.0.1:5433,
# which the data and journal scripts need.
docker compose -f docker-compose.yml -f compose.dev.yml up -d --build web
docker compose logs -f web

# One Worker job, in the worker image instead of `dotnet run --project src/LineOps.Worker`.
docker compose run --rm worker --job espn:range --sport nfl --from 2024-09-01 --to 2024-09-30

# Tests: in the SDK container, from a copy (the checkout is mounted read-only, so Linux bin/obj
# never land in it and Visual Studio's locked .vs is skipped); the docker socket for Testcontainers.
docker run --rm -v "${PWD}:/repo:ro" -v /var/run/docker.sock:/var/run/docker.sock `
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal mcr.microsoft.com/dotnet/sdk:10.0 sh -c `
  'mkdir /src && cd /repo && tar --exclude=./.vs --exclude=./lineops-journal --exclude=./data/personal --exclude=bin --exclude=obj -cf - . | tar -xf - -C /src && cd /src && dotnet test LineOps.slnx'
```

The web container schedules ingestion itself; do not also run a host desk or a worker container
scheduling against the same database. Visual Studio's "An element with the same key but a
different value already exists … SelfHostWebServer" is unrelated stale IDE state: close it,
delete `.vs/` and `LineOps.slnLaunch.user`, reopen, and set LineOps.Web as the startup project.

## Scope

- Four leagues: **MLB, NBA, NFL and UFC**. NBA is this season only — its history starts at the
  2026–27 opening night (2026-10-20); nothing earlier is pulled. `Ingestion:Sports` and
  `Ingestion:Backfill:Sports` name them; `Sports.Enabled` hides the rest from the desk's pickers. Do not add a league without a
  season start in `Ingestion:Backfill:Seasons`.
- UFC is fought, not played (ADR 0021): a bout is a `Game` between two fighters (each a `Team`
  and a `Player`), its card and result live in `Bouts`, and a fighter's ESPN profile in
  `FighterProfiles`. Ask `FightSports.IsFight`, never compare the key. Books only attach to bouts
  ESPN holds; Contender Series and Road to UFC cards are not UFC and are not read. History reaches
  back to UFC 300 (2024-04-13).
- Odds come from a book market (ADR 0011); ESPN's closing line fills games no market covered.
  Where ESPN has no line — early-season history — there is no line, and nothing fabricates one.
- Every game carries `SeasonYear` and `SeasonType`, stamped by the provider. Filter by season,
  not by a rolling day count, wherever a reader is about a team's or player's record.

## Verification before "done"

- `dotnet build LineOps.slnx` clean (eleven projects and four test projects); `dotnet test
  LineOps.slnx` green, with Docker running for the Testcontainers suites.
- The Worker host starts (`dotnet run --project src/LineOps.Worker`); its `appsettings.json`
  comment keys must be unique (`"//1"`, `"//2"` …) or the configuration provider refuses it.
- For data changes: the coverage queries in
  `docs/superpowers/specs/2026-09-04-mlb-nfl-seasons-research.md` §2 — finals without stats
  and stuck Live games should both be zero for MLB.
