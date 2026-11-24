<#
.SYNOPSIS
    Snapshot the local XLines database into the owner's private journal repository, so another
    of the owner's machines can restore exactly the same data with one command.

.DESCRIPTION
    The desk's data lives in a Docker Postgres on this machine. To keep developing from a
    laptop against the same games, stats and closing lines, the database is published as a
    compressed pg_dump under data/personal/snapshots/: inside the clone of the private
    repository lineops-journal (CLAUDE.md, "The journal"), never in this public one. ESPN's and
    MLB's data may not be redistributed, so a snapshot of it is the owner's alone; anyone else
    fills their own database with the espn:range job. At a few MB it is cheap to version; each
    refresh replaces the file rather than accumulating.

    Run from the repository root or anywhere; the script finds the repo from its own path.

.PARAMETER Commit
    Also commit the snapshot inside the private clone at data/personal (author: your git
    identity, no trailer) and push it to that clone's origin. Refuses if data/personal is not a
    git work tree. Nothing is ever added or committed in this public repository.

.PARAMETER IncludePersonal
    Also snapshot the rows that are yours rather than the market's: journal entries, parlays,
    operator settings (the bankroll, the polling switch), and the journal's deletions and audit
    (ADR 0025). Left out by default: the journal travels as journal.json through
    publish-journal.ps1, and a bet log is not market data. Their tables are still created on
    restore, empty, so the schema matches the code either way. This machine's transfer log is
    never included.

.PARAMETER DisplayableOnly
    Snapshot only the displayable tier (ADR 0024): what a source whose terms allow display
    supplied. Box scores, raw stat payloads and fighter profiles (ESPN) keep their schema and
    lose their rows. A closing line or scan from a personal-use source cannot be told apart by
    pg_dump, so the dump is restored into a throwaway server inside the sidecar, those rows are
    deleted there, and that copy is what is dumped; the published file never holds them. ESPN's
    and MLB's prose about a game (last play, situation, linescore, probable pitchers) and the
    injury report are cleared too. Games, teams and players stay, with their final scores: the
    odds attach to them (see ADR 0024 on why that is still a gap). The manifest says
    "tier": "displayable". Not combined with -IncludePersonal.

.PARAMETER OutDir
    Where the dump and its manifest are written. data/personal/snapshots by default; -Commit
    only publishes from there.

.EXAMPLE
    .\scripts\publish-data.ps1 -Commit

.EXAMPLE
    .\scripts\publish-data.ps1 -DisplayableOnly -OutDir $env:TEMP\xlines-shared
#>
param(
    [string]$Container = "xlines-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops",
    [switch]$Commit,
    [switch]$IncludePersonal,
    [switch]$DisplayableOnly,
    [string]$OutDir
)

$ErrorActionPreference = "Stop"
# docker and psql write progress and notices to stderr, which Windows PowerShell 5.1 turns
# into a terminating error while $ErrorActionPreference is Stop. They report failure by exit
# code, so native calls run with Continue and the callers check $LASTEXITCODE or the output.
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
# The snapshot lives in the clone of the private repository lineops-journal, never in this public
# one: ESPN's and MLB's data may not be redistributed. .gitignore keeps data/personal out of here.
$private = Join-Path (Join-Path $root 'data') 'personal'
$snapshots = Join-Path $private 'snapshots'
$dir = if ($OutDir) { $OutDir } else { $snapshots }

if ($DisplayableOnly -and $IncludePersonal) { throw "-DisplayableOnly is for a snapshot others may read; it does not carry the journal (-IncludePersonal)." }

# -Commit commits inside the private clone, so it must be one, and a clone of its own: a plain
# folder inside this checkout would hand the commit to the public repository. Asked before the
# dump is taken, so a refusal costs nothing.
if ($Commit) {
    $top = if (Test-Path $private) { Invoke-Native { git -C $private rev-parse --show-toplevel 2>$null } } else { $null }
    $inside = if ($top) { Invoke-Native { git -C $private rev-parse --is-inside-work-tree 2>$null } } else { $null }
    if ($inside -ne 'true' -or -not $top -or (Resolve-Path $top).Path -ne (Resolve-Path $private).Path) {
        throw "data/personal is not a git clone of its own, so -Commit has nowhere private to publish. Clone the private journal repository there (CLAUDE.md, `"The journal`"): git clone https://github.com/wNohejl/lineops-journal.git data/personal. Or write the dump elsewhere with -OutDir and no -Commit."
    }
}

New-Item -ItemType Directory -Force $dir | Out-Null
$dir = (Resolve-Path $dir).Path
if ($Commit -and $dir -ne (Resolve-Path $snapshots).Path) { throw "-Commit publishes data/personal/snapshots only; -OutDir is for a dump that stays off every repository." }
$dump = Join-Path $dir "lineops.dump"
$manifest = Join-Path $dir "lineops.dump.json"

Invoke-Native { docker inspect $Container *> $null }
if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Start it: $(Format-ComposeCommand $root)" }
# The container enforces SCRAM for local connections too (docker-compose.yml's
# POSTGRES_INITDB_ARGS), so psql and pg_restore inside it need the password. It comes from
# .env, the same place compose reads it, and reaches the tools through PGPASSWORD.
$pgPassword = Get-PgPassword $root
$pgEnv = "PGPASSWORD=$pgPassword"

# The dump is written by a sidecar into the snapshot directory directly (scripts/_pg.ps1 says why),
# first under a temporary name, so a failed or refused dump never replaces the last good one.
#
# Personal tables keep their schema and lose their rows (see -IncludePersonal). The names are
# double-quoted for pg_dump, which folds an unquoted pattern to lower case. Then the archive is
# listed and refused if it holds a row from any of them anyway: the journal has its own way out
# (publish-journal.ps1), and this is the one place a bet log could leave the machine unasked. The journal's deletions and its audit
# (ADR 0025) are the journal's own and go where it goes.
$personal = @('JournalEntries', 'Parlays', 'AppSettings', 'JournalTombstones', 'JournalAudit')
$exclude = if ($IncludePersonal) { '' } else {
    ($personal | ForEach-Object { "--exclude-table-data='`"$_`"'" }) -join ' '
}

# This machine's log of what it published and restored (ADR 0025) never travels, -IncludePersonal
# or not: restored on the other machine it would claim transfers that machine never made. A
# database from before migration JournalSync has no such table; an exclusion that matches nothing
# is no error to pg_dump.
$machineLog = @('DataTransfers')
$exclude = (@($exclude) + ($machineLog | ForEach-Object { "--exclude-table-data='`"$_`"'" })) -join ' '

# Play-by-play keeps its schema and loses its rows too, always: a season of plays is several
# times the rest of the snapshot, every published version stays in git history, and the other
# machine refills it from ESPN with espn:gamecast:backport (unmetered).
$refillable = @('GamePlays', 'GameDrives', 'GamecastBackportMisses')
$exclude = (@($exclude) + ($refillable | ForEach-Object { "--exclude-table-data='`"$_`"'" })) -join ' '

# The displayable tier (ADR 0024) leaves out the tables that are wholly a personal-use source's:
# box scores and raw stat payloads, which only ESPN fills today, and ESPN's fighter profiles.
# A balldontlie box score would be displayable, but no adapter writes one yet, so there is
# nothing to keep; the scrub below deletes by licence in case that changes.
$statsPort = @('PlayerGameStats', 'StatSnapshots', 'FighterProfiles')
if ($DisplayableOnly) {
    $exclude = (@($exclude) + ($statsPort | ForEach-Object { "--exclude-table-data='`"$_`"'" })) -join ' '
}

$refused = @(if (-not $IncludePersonal) { $personal }) + $machineLog + @(if ($DisplayableOnly) { $statsPort })
$check = if ($refused.Count -eq 0) { '' } else { @"
if pg_restore -l /snapshots/lineops.dump.partial | grep -E 'TABLE DATA public "?($($refused -join '|'))"? '; then
  echo 'refused: the archive holds rows it must not; nothing was replaced' >&2
  rm -f /snapshots/lineops.dump.partial
  exit 3
fi
"@ }

# A manifest beside the dump, so a reader knows what is in it without restoring it. The
# SQL goes over stdin: passed as an argument, PowerShell strips the double quotes that
# the PascalCase identifiers need.
$sql = @"
select json_build_object(
  'takenAtUtc', to_char(now() at time zone 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
  'tier', '$(if ($DisplayableOnly) { 'displayable' } else { 'full' })',
  'games', (select json_object_agg(k, n) from (select s."Key" k, count(*) n from "Games" g join "Sports" s on s."Id"=g."SportId" group by 1) t),
  'players', (select count(*) from "Players"),
  'statLines', (select count(*) from "PlayerGameStats"),
  'closingLines', (select count(*) from "ClosingLines"),
  'bouts', (select count(*) from "Bouts"),
  'fighterProfiles', (select count(*) from "FighterProfiles"),
  'personal', '$(if ($IncludePersonal) { 'included' } else { 'excluded' })',
  'seasons', (select json_object_agg(k, ys) from (select s."Key" k, json_agg(distinct g."SeasonYear") ys from "Games" g join "Sports" s on s."Id"=g."SportId" group by 1) t)
)::text;
"@

if (-not $DisplayableOnly) {
    $exit = Invoke-PgSidecar -Container $Container -Password $pgPassword -Directory $dir -Script @"
pg_dump -U $User -Fc -Z 6 $exclude -f /snapshots/lineops.dump.partial $Database
$check
mv /snapshots/lineops.dump.partial /snapshots/lineops.dump
"@
    if ($exit -ne 0 -or -not (Test-Path $dump) -or (Get-Item $dump).Length -lt 1024) { throw "pg_dump failed (exit $exit)" }

    $counts = (Invoke-Native { $sql | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t }) -join ""
}
else {
    # The scrub. pg_dump filters whole tables, not rows, and a closing line from ESPN sits in the
    # same table as one from the book market. So the dump is restored into a throwaway server
    # started inside the sidecar itself (the image carries one), the personal-use rows are
    # deleted there, and that copy is dumped. The live database is only read; nothing a
    # personal-use source supplied reaches the published file.
    #
    # Which sources are displayable is read from "Sources"."Licence"; a database from before
    # that column (migration SourceLicence) is classified by key, from the same table as
    # SourceLicences.Known in XLines.Core.
    $scrub = @'
-- Which sources may be shown.
CREATE TEMP TABLE shown AS SELECT "Id" FROM "Sources" WHERE "Key" IN ('the-odds-api', 'balldontlie');
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'Sources' AND column_name = 'Licence') THEN
    DELETE FROM shown;
    INSERT INTO shown SELECT "Id" FROM "Sources" WHERE "Licence" = 'Displayable';
  END IF;
END $$;

-- Rows a personal-use source supplied: ESPN's reference closes, an evaluation feed's scans.
DELETE FROM "ClosingLines"      WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
DELETE FROM "ClosingLineMisses" WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
DELETE FROM "OddsSnapshots"     WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
DELETE FROM "PlayerGameStats"   WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
DELETE FROM "StatSnapshots"     WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
-- ESPN's and MLB's account of a game's circumstances: venue, weather, officials, lineups. A
-- database from before migration GameContextAndOpeningLine has neither table.
DO $$ BEGIN
  IF to_regclass('public."GameOfficials"') IS NOT NULL THEN
    DELETE FROM "GameOfficials" WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
    DELETE FROM "GameContexts"  WHERE "SourceId" NOT IN (SELECT "Id" FROM shown);
  END IF;
END $$;

-- ESPN's and MLB's own words about a game and a player. The schedule and the final score stay:
-- every odds row hangs off a game.
UPDATE "Games" SET "LastPlay" = NULL, "Situation" = NULL, "Linescore" = NULL, "StatusDetail" = NULL,
  "DownDistanceText" = NULL, "PossessionText" = NULL,
  "HomeProbablePitcher" = NULL, "AwayProbablePitcher" = NULL;
UPDATE "Players" SET "InjuryStatus" = NULL, "InjuryNote" = NULL, "InjuryUpdatedAt" = NULL;

-- Refuse rather than publish if anything personal-use is left. By licence, as the deletes above
-- are: a displayable stats source's box scores may stay (the exclusions still leave the table's
-- data out of this dump), so the check must not fail the day such rows exist.
DO $$ BEGIN
  IF EXISTS (SELECT 1 FROM "ClosingLines"    WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))
     OR EXISTS (SELECT 1 FROM "OddsSnapshots"   WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))
     OR EXISTS (SELECT 1 FROM "PlayerGameStats" WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))
     OR EXISTS (SELECT 1 FROM "StatSnapshots"   WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))
     OR EXISTS (SELECT 1 FROM "FighterProfiles") THEN
    RAISE EXCEPTION 'refused: personal-use rows survived the scrub';
  END IF;
  IF to_regclass('public."GameOfficials"') IS NOT NULL
     AND (EXISTS (SELECT 1 FROM "GameOfficials" WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))
          OR EXISTS (SELECT 1 FROM "GameContexts" WHERE "SourceId" NOT IN (SELECT "Id" FROM shown))) THEN
    RAISE EXCEPTION 'refused: personal-use rows survived the scrub';
  END IF;
END $$;
'@

    $exit = Invoke-PgSidecar -Container $Container -Password $pgPassword -Directory $dir -Script (@'
pg_dump -U __USER__ -Fc -Z 6 __EXCLUDE__ -f /tmp/full.dump __DB__
# The throwaway server: owned by the image's postgres user, or by the caller where the sidecar
# already runs as one (Linux, scripts/_pg.ps1).
if [ "$(id -u)" = 0 ]; then AS="$(command -v gosu || command -v su-exec) postgres"; else AS=''; fi
mkdir -p /tmp/scrub && chmod 700 /tmp/scrub
if [ -n "$AS" ]; then chown postgres /tmp/scrub; fi
$AS initdb -D /tmp/scrub -U __USER__ --auth=trust < /dev/null > /dev/null
$AS pg_ctl -D /tmp/scrub -o "-c listen_addresses='' -k /tmp" -l /tmp/scrub.log -w start < /dev/null > /dev/null
export PGHOST=/tmp
createdb -U __USER__ __DB__ < /dev/null
pg_restore -U __USER__ -d __DB__ --no-owner --no-privileges /tmp/full.dump < /dev/null
psql -U __USER__ -d __DB__ -X -q -v ON_ERROR_STOP=1 <<'SCRUB'
__SCRUB__
SCRUB
# The same exclusions again, so the archive has no data entry for those tables for the check to find.
pg_dump -U __USER__ -Fc -Z 6 __EXCLUDE__ -f /snapshots/lineops.dump.partial __DB__ < /dev/null
__CHECK__
psql -U __USER__ -d __DB__ -X -q -A -t > /snapshots/lineops.dump.json.partial <<'MANIFEST'
__MANIFEST__
MANIFEST
$AS pg_ctl -D /tmp/scrub -m fast stop < /dev/null > /dev/null
mv /snapshots/lineops.dump.partial /snapshots/lineops.dump
'@).Replace('__USER__', $User).Replace('__DB__', $Database).Replace('__EXCLUDE__', $exclude).Replace('__SCRUB__', $scrub).Replace('__MANIFEST__', $sql).Replace('__CHECK__', $check)
    if ($exit -ne 0 -or -not (Test-Path $dump) -or (Get-Item $dump).Length -lt 1024) { throw "displayable snapshot failed (exit $exit); nothing was replaced" }

    $partial = Join-Path $dir 'lineops.dump.json.partial'
    $counts = (Get-Content $partial -Raw)
    Remove-Item $partial
}

if (-not $counts) { throw "manifest query returned nothing" }
[System.IO.File]::WriteAllText($manifest, $counts.Trim() + "`n")

$size = "{0:N1} MB" -f ((Get-Item $dump).Length / 1MB)
Write-Host "Snapshot written: $dump ($size)"
Write-Host $counts

# Logged on this machine, after the dump, so the dump never holds its own record (and the table
# is left out of it anyway). Skipped quietly on a database from before migration JournalSync.
Add-DataTransfer -Container $Container -Password $pgPassword -User $User -Database $Database `
    -Kind SnapshotPublished -File (Split-Path $dump -Leaf) -Manifest $counts

# Committed and pushed inside the private clone only, every git call pinned to it with -C, and
# by name relative to it: the dump and its manifest, nothing else that clone holds. Never in
# this public repository.
if ($Commit) {
    $paths = @('snapshots/lineops.dump', 'snapshots/lineops.dump.json')
    $date = Get-Date -Format "yyyy-MM-dd"
    $what = if ($DisplayableOnly) { "data: displayable snapshot $date" } else { "data: snapshot $date" }
    Invoke-Native { git -C $private add -- @paths }
    if ($LASTEXITCODE -ne 0) { throw "git add failed in data/personal (exit $LASTEXITCODE)" }
    Invoke-Native { git -C $private commit -m $what -- @paths }
    if ($LASTEXITCODE -ne 0) { throw "git commit failed in data/personal (exit $LASTEXITCODE); is the snapshot unchanged since the last one?" }
    Invoke-Native { git -C $private push }
    if ($LASTEXITCODE -ne 0) { throw "git push failed in data/personal (exit $LASTEXITCODE); the commit is there, push it with: git -C data/personal push" }
}
