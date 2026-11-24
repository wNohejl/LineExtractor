<#
.SYNOPSIS
    Restore the owner's database snapshot into the local Docker Postgres, so this machine
    holds exactly the data the snapshot was taken from.

.DESCRIPTION
    Starts the dev Postgres if it is not running, waits for it to be healthy, then restores
    data/personal/snapshots/lineops.dump over the database. That folder is the clone of the
    owner's private repository lineops-journal (CLAUDE.md, "The journal"): ESPN's and MLB's data
    may not be redistributed, so this public repository carries no snapshot. Anyone else fills
    their own database instead, with the espn:range job or the Board's Date range key.

    The restore replaces the schema: existing tables are dropped and recreated from the archive,
    including the EF migration history, so the app starts against a database that matches the
    code the snapshot was taken with.

    Refuses to overwrite a database that already holds games unless -Force is given, because
    a restore is not a merge: whatever this machine ingested since the snapshot is replaced.

.PARAMETER InFile
    A dump somewhere other than data/personal/snapshots/lineops.dump. Its manifest, if any, is
    read from beside it (<file>.json).

.EXAMPLE
    git -C data/personal pull
    .\scripts\restore-data.ps1            # first time on a laptop
    .\scripts\restore-data.ps1 -Force     # replace local data with the latest snapshot

.EXAMPLE
    .\scripts\restore-data.ps1 -InFile D:\lineops.dump
#>
param(
    [string]$Container = "xlines-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops",
    [string]$InFile,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
# docker and psql write progress and notices to stderr, which Windows PowerShell 5.1 turns
# into a terminating error while $ErrorActionPreference is Stop. They report failure by exit
# code, so native calls run with Continue and the callers check $LASTEXITCODE or the output.
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
$dump = if ($InFile) { $InFile } else { Join-Path (Join-Path (Join-Path (Join-Path $root 'data') 'personal') 'snapshots') 'lineops.dump' }

# No snapshot is the normal case for anyone but the owner: it travels only through the owner's
# private repository. A new desk fills itself from ESPN instead.
if (-not (Test-Path $dump -PathType Leaf)) {
    throw @"
No snapshot at $dump.
The snapshot is the owner's and lives in a private repository; this public one carries none. A new desk fills itself instead:
  the Board's Date range key on the desk, or
  dotnet run --project src/XLines.Worker -- --job espn:range --sport nfl --from <date> --to <date>
  (Docker: docker compose run --rm worker --job espn:range --sport nfl --from <date> --to <date>)
The owner: clone the private journal repository at data/personal and pull it, or name a dump with -InFile.
"@
}
$dump = (Resolve-Path $dump).Path
# The container enforces SCRAM for local connections too (docker-compose.yml's
# POSTGRES_INITDB_ARGS), so psql and pg_restore need the password. It comes from .env, the
# same place compose reads it, and reaches the tools through PGPASSWORD.
$pgPassword = Get-PgPassword $root
$pgEnv = "PGPASSWORD=$pgPassword"

Push-Location $root
try {
    # The same compose files this machine runs Postgres with (_pg.ps1), so recreating the
    # container keeps the tailnet port on the desktop.
    $composeArgs = Get-ComposeArgs $root
    Invoke-Native { docker compose @composeArgs up -d postgres 2>&1 | Out-Null }
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed to start postgres (exit $LASTEXITCODE): $(Format-ComposeCommand $root)" }

    $deadline = (Get-Date).AddSeconds(90)
    do {
        $status = Invoke-Native { docker inspect --format '{{.State.Health.Status}}' $Container 2>$null }
        if ($status -eq "healthy") { break }
        Start-Sleep 3
    } while ((Get-Date) -lt $deadline)
    if ($status -ne "healthy") { throw "Postgres did not become healthy in time (status: $status)" }

    # SQL goes over stdin: passed as an argument, PowerShell strips the double quotes that
    # the PascalCase identifiers need, and a fresh database has no "Games" to count yet.
    $existing = Invoke-Native { 'select count(*) from "Games"' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t 2>$null }
    if ($existing -and [int]$existing.Trim() -gt 0 -and -not $Force) {
        throw "This database already holds $($existing.Trim()) games. Re-run with -Force to replace them with the snapshot. The journal goes too: run scripts/publish-journal.ps1 first, and scripts/restore-journal.ps1 after, to keep it."
    }

    # The schema is dropped and recreated rather than restored with --clean: the odds tables
    # are partitioned, and --clean trips over their inherited constraints. Whatever this
    # machine held is gone here, which is what the -Force guard above stands in front of.
    Invoke-Native { 'set client_min_messages = warning; drop schema public cascade; create schema public;' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -v ON_ERROR_STOP=1 }
    if ($LASTEXITCODE -ne 0) { throw "could not reset the public schema (exit $LASTEXITCODE)" }

    # Read by a sidecar from the dump's own folder, mounted read-only (scripts/_pg.ps1 says why), so the
    # archive never crosses a shell pipe on any platform.
    $exit = Invoke-PgSidecar -Container $Container -Password $pgPassword -Directory (Split-Path $dump) -ReadOnly -Script @"
pg_restore -U $User -d $Database --no-owner --no-privileges /snapshots/$(Split-Path $dump -Leaf)
"@
    if ($exit -ne 0) { throw "pg_restore failed (exit $exit)" }

    # Logged after the restore, never before: the restore dropped the schema and every row this
    # machine had in "DataTransfers" with it, and a row written first would be gone too. A
    # snapshot from before migration JournalSync has no such table; the log is then skipped
    # quietly, and the desk's next start migrates it in.
    $manifestFile = "$dump.json"
    $manifestText = if (Test-Path $manifestFile) { Get-Content $manifestFile -Raw } else { $null }
    Add-DataTransfer -Container $Container -Password $pgPassword -User $User -Database $Database `
        -Kind SnapshotRestored -File (Split-Path $dump -Leaf) -Manifest $manifestText

    $games = Invoke-Native { 'select s."Key" || '': '' || count(*) from "Games" g join "Sports" s on s."Id"=g."SportId" group by s."Key" order by s."Key"' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t }
    Write-Host "Restored. Games by sport:"; Write-Host $games
    Write-Host "Now: dotnet run --project src/XLines.Web --launch-profile http"
}
finally { Pop-Location }
