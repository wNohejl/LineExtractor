<#
.SYNOPSIS
    Snapshot the local LineOps database into the repository, so another machine can restore
    exactly the same data with one command.

.DESCRIPTION
    The desk's data lives in a Docker Postgres on this machine. To keep developing from a
    laptop against the same games, stats and closing lines, the database is published as a
    compressed pg_dump under data/snapshots/ and committed alongside the code. At ~2–3 MB it
    is cheap to version; each refresh replaces the file rather than accumulating.

    Run from the repository root or anywhere; the script finds the repo from its own path.

.PARAMETER Commit
    Also commit the snapshot (author: your git identity, no trailer) and push to origin.

.EXAMPLE
    .\scripts\publish-data.ps1 -Commit
#>
param(
    [string]$Container = "lineops-postgres",
    [string]$Database = "lineops",
    [string]$User = "lineops",
    [switch]$Commit
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$dir = Join-Path $root "data\snapshots"
$dump = Join-Path $dir "lineops.dump"
$manifest = Join-Path $dir "lineops.dump.json"

New-Item -ItemType Directory -Force $dir | Out-Null

docker inspect $Container *> $null
if ($LASTEXITCODE -ne 0) { throw "Container '$Container' is not running. Start it: docker compose -f docker-compose.yml -f compose.dev.yml up -d postgres" }

# The archive is streamed out of the container rather than written inside it and copied:
# the container's /tmp is a tmpfs mount (see docker-compose.yml's hardening), which
# `docker cp` cannot read. PowerShell would re-encode binary stdout as text, so the
# redirection is done by cmd, which passes bytes through untouched.
cmd /c "docker exec $Container pg_dump -U $User -Fc -Z 6 $Database > `"$dump`""
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $dump) -or (Get-Item $dump).Length -lt 1024) { throw "pg_dump failed" }

# A manifest beside the dump, so a reader knows what is in it without restoring it. The
# SQL goes over stdin: passed as an argument, PowerShell strips the double quotes that
# the PascalCase identifiers need.
$sql = @"
select json_build_object(
  'takenAtUtc', to_char(now() at time zone 'utc', 'YYYY-MM-DD"T"HH24:MI:SS"Z"'),
  'games', (select json_object_agg(k, n) from (select s."Key" k, count(*) n from "Games" g join "Sports" s on s."Id"=g."SportId" group by 1) t),
  'players', (select count(*) from "Players"),
  'statLines', (select count(*) from "PlayerGameStats"),
  'closingLines', (select count(*) from "ClosingLines"),
  'seasons', (select json_object_agg(k, ys) from (select s."Key" k, json_agg(distinct g."SeasonYear") ys from "Games" g join "Sports" s on s."Id"=g."SportId" group by 1) t)
)::text;
"@
$counts = ($sql | docker exec -i $Container psql -U $User -d $Database -X -q -A -t) -join ""
if (-not $counts) { throw "manifest query returned nothing" }
[System.IO.File]::WriteAllText($manifest, $counts.Trim() + "`n")

$size = "{0:N1} MB" -f ((Get-Item $dump).Length / 1MB)
Write-Host "Snapshot written: $dump ($size)"
Write-Host $counts

if ($Commit) {
    Push-Location $root
    try {
        git add -- $dump $manifest
        $date = Get-Date -Format "yyyy-MM-dd"
        git commit -m "data: snapshot $date" -- $dump $manifest
        git push
    }
    finally { Pop-Location }
}
