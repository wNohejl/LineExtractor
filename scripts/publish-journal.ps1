<#
.SYNOPSIS
    Write the rows that are yours (journal entries, parlays, operator settings) to a file
    that stays off the repository, to carry to your other machine by hand.

.DESCRIPTION
    publish-data.ps1 leaves these three tables out because its dump is committed and the
    repository is public. This is the other half: a data-only archive of just those tables,
    written to data/personal/, which .gitignore excludes. Send the file yourself (a USB
    stick, your own cloud drive, Tailscale's taildrop), then run restore-journal.ps1 there.

    A journal entry points at a game and sometimes a player, by id. The other machine must
    hold the same games: restore the latest snapshot there first.

.EXAMPLE
    .\scripts\publish-journal.ps1
#>
param(
    [string]$Container = "lineops-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops"
)

$ErrorActionPreference = "Stop"
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path (Join-Path $root 'data') 'personal'
New-Item -ItemType Directory -Force $dir | Out-Null
$dump = Join-Path $dir 'journal.dump'

# Refuse to write where git would pick the file up: this is the one place a bet log could
# leave the machine by accident.
Push-Location $root
try { Invoke-Native { git check-ignore -q -- data/personal/journal.dump } ; $ignored = $LASTEXITCODE -eq 0 }
finally { Pop-Location }
if (-not $ignored) { throw "data/personal/ is not ignored by git. Add it to .gitignore before writing a journal there." }

Invoke-Native { docker inspect $Container *> $null }
if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Start it: docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres" }
$pgPassword = Get-PgPassword $root

# Table names are double-quoted for pg_dump, which folds an unquoted pattern to lower case.
$exit = Invoke-PgSidecar -Container $Container -Password $pgPassword -Directory $dir -Script @"
pg_dump -U $User -Fc -Z 6 --data-only -t '"Parlays"' -t '"JournalEntries"' -t '"AppSettings"' -f /snapshots/journal.dump.partial $Database
mv /snapshots/journal.dump.partial /snapshots/journal.dump
"@
if ($exit -ne 0 -or -not (Test-Path $dump)) { throw "pg_dump failed (exit $exit)" }

$sql = @"
select 'journal entries: ' || (select count(*) from "JournalEntries")
    || ', parlays: ' || (select count(*) from "Parlays")
    || ', settings: ' || (select count(*) from "AppSettings");
"@
$counts = (Invoke-Native { $sql | docker exec -i -e "PGPASSWORD=$pgPassword" $Container psql -U $User -d $Database -X -q -A -t }) -join ""
Write-Host "Journal written: $dump ($([math]::Round((Get-Item $dump).Length / 1KB, 1)) KB)"
Write-Host $counts.Trim()
Write-Host "Not committed. Copy it to data/personal/ on the other machine, then run scripts/restore-journal.ps1 there."
