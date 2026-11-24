<#
.SYNOPSIS
    Write the rows that are yours (journal entries, parlays, deletions, operator settings) to a
    file that stays off the repository, to carry to your other machine by hand.

.DESCRIPTION
    publish-data.ps1 leaves these tables out because its dump is committed and the repository
    is public. This is the other half: the Worker's journal:export job (ADR 0025) writes them to
    data/personal/journal.json, which .gitignore excludes. Send the file yourself (a USB stick,
    your own cloud drive, Tailscale's taildrop), then run restore-journal.ps1 there.

    The file names each bet's game the way ESPN, MLB and The Odds API do, not by this
    database's row id, so the other machine does not need the same snapshot: it finds the game
    in whatever it holds, and merges rather than replaces. Like any run of the Worker, the job
    migrates this database to the checkout's schema first.

.PARAMETER OutFile
    Where to write. data/personal/journal.json by default. Refused if it is inside a repository
    and git does not ignore it.

.PARAMETER ConnectionString
    The database to read, when it is not the Worker's configured one.

.EXAMPLE
    .\scripts\publish-journal.ps1
#>
param(
    [string]$Container = "xlines-postgres",
    [string]$OutFile,
    [string]$ConnectionString
)

$ErrorActionPreference = "Stop"
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path (Join-Path $root 'data') 'personal'
if (-not $OutFile) {
    New-Item -ItemType Directory -Force $dir | Out-Null
    $OutFile = Join-Path $dir 'journal.json'

    # Refuse to write where git would pick the file up: this is the one place a bet log could
    # leave the machine by accident. The job asks git again, for any path it is given.
    Push-Location $root
    try { Invoke-Native { git check-ignore -q -- data/personal/journal.json } ; $ignored = $LASTEXITCODE -eq 0 }
    finally { Pop-Location }
    if (-not $ignored) { throw "data/personal/ is not ignored by git. Add it to .gitignore before writing a journal there." }
}

if (-not $ConnectionString) {
    Invoke-Native { docker inspect $Container *> $null }
    if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Start it: $(Format-ComposeCommand $root)" }
}

$exit = Invoke-JournalJob -Root $root -Arguments @('--job', 'journal:export', '--out', $OutFile) -ConnectionString $ConnectionString
if ($exit -ne 0) { throw "journal:export failed (exit $exit); nothing was replaced." }
Write-Host "Not committed. Copy it to data/personal/ on the other machine, then run scripts/restore-journal.ps1 there."
