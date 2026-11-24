<#
.SYNOPSIS
    Merge the journal written by publish-journal.ps1 on your other machine into this one.

.DESCRIPTION
    Reads data/personal/journal.json and merges it with this machine's journal through the
    Worker's journal:import job (ADR 0025), in one transaction: if a row cannot be merged,
    nothing changes. A bet is matched by its own id, not this database's row number; the newer
    copy of a bet wins; a bet changed on both machines at the same moment is reported and left
    as it is here; a deletion beats only a copy older than itself. Running it twice with the
    same file changes nothing the second time.

    A bet on a game this machine has not pulled is brought in without its game, and the report
    names the game. Pull that day (the Board's Date range key, or the Worker's espn:range job),
    then run this again: the same file links it.

.PARAMETER DryRun
    Say what would change (new, updated, deleted, unchanged, conflicts, unresolved) and write
    nothing.

.PARAMETER InFile
    The file to merge. data/personal/journal.json by default.

.PARAMETER ConnectionString
    The database to merge into, when it is not the Worker's configured one.

.EXAMPLE
    .\scripts\restore-journal.ps1 -DryRun
.EXAMPLE
    .\scripts\restore-journal.ps1
#>
param(
    [string]$Container = "xlines-postgres",
    [string]$InFile,
    [string]$ConnectionString,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path (Join-Path $root 'data') 'personal'
if (-not $InFile) { $InFile = Join-Path $dir 'journal.json' }

if (-not (Test-Path $InFile)) {
    $legacy = Join-Path $dir 'journal.dump'
    if (Test-Path $legacy) {
        throw "Found $legacy, the older format, which carries row ids and cannot be merged. On the machine that has the journal, update this checkout and run scripts/publish-journal.ps1 again: it now writes journal.json."
    }
    throw "No journal at $InFile. Run scripts/publish-journal.ps1 on the machine that has it and copy the file here."
}

if (-not $ConnectionString) {
    Invoke-Native { docker inspect $Container *> $null }
    if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Run scripts/restore-data.ps1 first, or start it: docker compose -f docker-compose.yml -f compose/dev.yml up -d postgres" }
}

$jobArgs = @('--job', 'journal:import', '--in', $InFile)
if ($DryRun) { $jobArgs += @('--dry-run', 'true') }

$exit = Invoke-JournalJob -Root $root -Arguments $jobArgs -ConnectionString $ConnectionString
if ($exit -ne 0) { throw "journal:import failed (exit $exit); nothing was changed." }
