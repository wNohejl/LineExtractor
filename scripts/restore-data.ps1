<#
.SYNOPSIS
    Restore the committed database snapshot into the local Docker Postgres, so this machine
    holds exactly the data the snapshot was taken from.

.DESCRIPTION
    Starts the dev Postgres if it is not running, waits for it to be healthy, then restores
    data/snapshots/lineops.dump over the database. The restore is --clean: existing tables are
    dropped and recreated from the archive, including the EF migration history, so the app
    starts against a database that matches the code the snapshot was committed with.

    Refuses to overwrite a database that already holds games unless -Force is given, because
    a restore is not a merge: whatever this machine ingested since the snapshot is replaced.

.EXAMPLE
    .\scripts\restore-data.ps1            # first time on a laptop
    .\scripts\restore-data.ps1 -Force     # replace local data with the latest snapshot
#>
param(
    [string]$Container = "lineops-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dump = Join-Path $root "data\snapshots\lineops.dump"

if (-not (Test-Path $dump)) { throw "No snapshot at $dump. Run scripts/publish-data.ps1 on the machine that has the data." }
if (-not (Test-Path (Join-Path $root ".env"))) { throw ".env is missing. Run .\setup.ps1 first — it generates the database password the container needs." }

Push-Location $root
try {
    docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres | Out-Null

    $deadline = (Get-Date).AddSeconds(90)
    do {
        $status = docker inspect --format '{{.State.Health.Status}}' $Container 2>$null
        if ($status -eq "healthy") { break }
        Start-Sleep 3
    } while ((Get-Date) -lt $deadline)
    if ($status -ne "healthy") { throw "Postgres did not become healthy in time (status: $status)" }

    $existing = (docker exec $Container psql -U $User -d $Database -X -q -A -t -c 'select count(*) from "Games"' 2>$null)
    if ($existing -and [int]$existing.Trim() -gt 0 -and -not $Force) {
        throw "This database already holds $($existing.Trim()) games. Re-run with -Force to replace them with the snapshot."
    }

    # The archive goes in over stdin rather than being copied into the container: its /tmp
    # is a tmpfs mount that `docker cp` cannot write, and cmd's redirection passes the bytes
    # through where PowerShell would re-encode them.
    cmd /c "docker exec -i $Container pg_restore -U $User -d $Database --clean --if-exists --no-owner --no-privileges < `"$dump`""
    if ($LASTEXITCODE -ne 0) { Write-Warning "pg_restore reported errors (exit $LASTEXITCODE); --clean on a fresh database is usually the cause — check the games count below." }

    $games = docker exec $Container psql -U $User -d $Database -X -q -A -t -c 'select s."Key" || '': '' || count(*) from "Games" g join "Sports" s on s."Id"=g."SportId" group by 1 order by 1'
    Write-Host "Restored. Games by sport:"; Write-Host $games
    Write-Host "Now: dotnet run --project src/LineOps.Web --launch-profile http"
}
finally { Pop-Location }
