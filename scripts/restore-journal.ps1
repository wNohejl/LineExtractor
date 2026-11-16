<#
.SYNOPSIS
    Load the journal, parlays and operator settings written by publish-journal.ps1.

.DESCRIPTION
    Reads data/personal/journal.dump and replaces this machine's journal entries, parlays
    and operator settings with it, in one transaction: if a row cannot be loaded, nothing
    changes. Refuses to replace a journal that already holds entries unless -Force is given.

    A journal entry points at a game and sometimes a player, by id. If this machine's games
    are older than the journal, the load fails on a foreign key: restore the latest snapshot
    first (git pull; .\scripts\restore-data.ps1 -Force), then run this again.

.EXAMPLE
    .\scripts\restore-journal.ps1
.EXAMPLE
    .\scripts\restore-journal.ps1 -Force     # replace the journal already here
#>
param(
    [string]$Container = "lineops-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path (Join-Path $root 'data') 'personal'
$dump = Join-Path $dir 'journal.dump'
if (-not (Test-Path $dump)) { throw "No journal at $dump. Run scripts/publish-journal.ps1 on the machine that has it and copy the file here." }

Invoke-Native { docker inspect $Container *> $null }
if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Run scripts/restore-data.ps1 first: it starts Postgres and loads the games the journal points at." }
$pgPassword = Get-PgPassword $root
$pgEnv = "PGPASSWORD=$pgPassword"

$existing = (Invoke-Native { 'select count(*) from "JournalEntries"' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t }) -join ""
if ($existing -and [int]$existing.Trim() -gt 0 -and -not $Force) {
    throw "This database already holds $($existing.Trim()) journal entries. Re-run with -Force to replace them."
}

# Emptied and loaded in one transaction, so a failed load leaves the journal as it was.
# The archive is turned into SQL by pg_restore and piped to psql inside the sidecar, where
# the pipe is a Linux one and nothing binary crosses a PowerShell pipe.
$exit = Invoke-PgSidecar -Container $Container -Password $pgPassword -Directory $dir -ReadOnly -Script @"
{
  echo 'begin;'
  echo 'truncate "JournalEntries", "Parlays", "AppSettings";'
  pg_restore --data-only -f - /snapshots/journal.dump
  echo "select setval(pg_get_serial_sequence('public.\"JournalEntries\"', 'Id'), coalesce((select max(\"Id\") from public.\"JournalEntries\"), 1));"
  echo 'commit;'
} | psql -U $User -d $Database -X -q -v ON_ERROR_STOP=1 -o /dev/null
"@
if ($exit -ne 0) { throw "The journal was not loaded (exit $exit) and nothing was changed. If the error names a foreign key, this machine's games are older than the journal: git pull, .\scripts\restore-data.ps1 -Force, then run this again." }

$sql = @"
select 'journal entries: ' || (select count(*) from "JournalEntries")
    || ', parlays: ' || (select count(*) from "Parlays")
    || ', settings: ' || (select count(*) from "AppSettings");
"@
$counts = (Invoke-Native { $sql | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t }) -join ""
Write-Host "Journal restored. $($counts.Trim())"
