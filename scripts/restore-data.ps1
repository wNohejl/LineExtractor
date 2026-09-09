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
# docker and psql write progress and notices to stderr, which Windows PowerShell 5.1 turns
# into a terminating error while $ErrorActionPreference is Stop. They report failure by exit
# code, so native calls run with Continue and the callers check $LASTEXITCODE or the output.
function Invoke-Native([scriptblock]$Block) { $ErrorActionPreference = "Continue"; & $Block }
$root = Split-Path $PSScriptRoot -Parent
$dump = Join-Path $root "data\snapshots\lineops.dump"

if (-not (Test-Path $dump)) { throw "No snapshot at $dump. Run scripts/publish-data.ps1 on the machine that has the data." }
if (-not (Test-Path (Join-Path $root ".env"))) { throw ".env is missing. Run .\scripts\setup.ps1 first — it generates the database password the container needs." }
# The container enforces SCRAM for local connections too (docker-compose.yml's
# POSTGRES_INITDB_ARGS), so psql and pg_restore inside it need the password. It comes from
# .env, the same place compose reads it, and reaches the tools through PGPASSWORD.
$envText = Get-Content (Join-Path $root ".env") -Raw
if ($envText -notmatch '(?m)^POSTGRES_PASSWORD=(.+)$') { throw ".env has no POSTGRES_PASSWORD. Run .\scripts\setup.ps1 first." }
$pgEnv = "PGPASSWORD=$($Matches[1].Trim())"

Push-Location $root
try {
    Invoke-Native { docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres 2>&1 | Out-Null }
    if ($LASTEXITCODE -ne 0) { throw "docker compose failed to start postgres (exit $LASTEXITCODE)" }

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
        throw "This database already holds $($existing.Trim()) games. Re-run with -Force to replace them with the snapshot."
    }

    # The schema is dropped and recreated rather than restored with --clean: the odds tables
    # are partitioned, and --clean trips over their inherited constraints. Whatever this
    # machine held is gone here, which is what the -Force guard above stands in front of.
    Invoke-Native { 'set client_min_messages = warning; drop schema public cascade; create schema public;' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -v ON_ERROR_STOP=1 }
    if ($LASTEXITCODE -ne 0) { throw "could not reset the public schema (exit $LASTEXITCODE)" }

    # The archive goes in over stdin rather than being copied into the container: its /tmp
    # is a tmpfs mount that `docker cp` cannot write, and cmd's redirection passes the bytes
    # through where PowerShell would re-encode them.
    Invoke-Native { cmd /c "docker exec -i -e $pgEnv $Container pg_restore -U $User -d $Database --no-owner --no-privileges < `"$dump`"" }
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed (exit $LASTEXITCODE)" }

    $games = Invoke-Native { 'select s."Key" || '': '' || count(*) from "Games" g join "Sports" s on s."Id"=g."SportId" group by s."Key" order by s."Key"' | docker exec -i -e $pgEnv $Container psql -U $User -d $Database -X -q -A -t }
    Write-Host "Restored. Games by sport:"; Write-Host $games
    Write-Host "Now: dotnet run --project src/LineOps.Web --launch-profile http"
}
finally { Pop-Location }
