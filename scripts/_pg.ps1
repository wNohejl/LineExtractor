<#
    Dot-sourced by publish-data.ps1 and restore-data.ps1: runs Postgres's own tools in a
    throwaway container beside the database, the same way on Windows, macOS and Linux.

    Why a sidecar rather than `docker exec` on the database container:

    * The archive is binary. Streamed over stdout it passes through the shell, and Windows
      PowerShell 5.1 re-encodes native output as text, which is why these scripts used to hand
      the redirection to cmd.exe, which exists only on Windows. Here the archive is written
      to, or read from, a directory bind-mounted into the sidecar, and never crosses a pipe.
    * The database container's /tmp is a tmpfs mount that `docker cp` cannot reach.
    * The commands go to the sidecar's shell over stdin, as text. Passed as arguments, Windows
      PowerShell 5.1 strips the double quotes that pg_dump needs around a PascalCase table
      name, and an unquoted name is folded to lower case, matches nothing, and is silently not
      excluded. Over stdin they arrive exactly as written in every PowerShell.

    The sidecar joins the database container's network namespace, so the server is at
    127.0.0.1:5432 whether or not compose/dev.yml publishes it.
#>

function Invoke-PgSidecar {
    param(
        [Parameter(Mandatory)] [string]$Container,
        [Parameter(Mandatory)] [string]$Password,
        [Parameter(Mandatory)] [string]$Directory,
        [Parameter(Mandatory)] [string]$Script,
        [switch]$ReadOnly
    )

    $mount = "$((Resolve-Path $Directory).Path):/snapshots$(if ($ReadOnly) { ':ro' })"
    $dockerArgs = @('run', '-i', '--rm', '--network', "container:$Container",
        '-e', "PGPASSWORD=$Password", '-e', 'PGHOST=127.0.0.1', '-v', $mount)

    # On Linux a root-owned dump would land in the working tree; write it as the caller.
    if ($IsLinux) { $dockerArgs += @('--user', "$(id -u):$(id -g)") }

    # The image's major version matches the server's (docker-compose.yml), so pg_dump's archive
    # format is one pg_restore on either machine reads.
    # tr strips the carriage returns Windows PowerShell adds to each line it pipes.
    $dockerArgs += @('postgres:17-alpine', 'sh', '-c', "tr -d '\r' | sh -e")

    # Windows PowerShell 5.1 writes a native command's stdin with the console's input encoding.
    # Where that is UTF-8 with a byte-order mark (a console on code page 65001), the mark lands in
    # front of the first command and sh reports "pg_restore: not found", after restore-data.ps1
    # has already reset the schema. Plain UTF-8, for this call only.
    $plain = New-Object System.Text.UTF8Encoding $false
    $savedOutput = $OutputEncoding
    $savedInput = try { [Console]::InputEncoding } catch { $null }
    $OutputEncoding = $plain
    if ($savedInput) { try { [Console]::InputEncoding = $plain } catch { } }
    try {
        $ErrorActionPreference = 'Continue'
        $Script | & docker @dockerArgs | Out-Host
        return $LASTEXITCODE
    }
    finally {
        $OutputEncoding = $savedOutput
        if ($savedInput) { try { [Console]::InputEncoding = $savedInput } catch { } }
    }
}

function Get-PgPassword([string]$Root) {
    $envFile = Join-Path $Root '.env'
    # A worktree has no .env of its own; PGPASSWORD set by the caller stands in for it.
    if (-not (Test-Path $envFile) -and $env:PGPASSWORD) { return $env:PGPASSWORD }
    if (-not (Test-Path $envFile)) { throw ".env is missing. Run ./scripts/setup.ps1 first: it generates the database password the container needs." }
    $text = Get-Content $envFile -Raw
    if ($text -notmatch '(?m)^POSTGRES_PASSWORD=(.+)$') { throw ".env has no POSTGRES_PASSWORD. Run ./scripts/setup.ps1 first." }
    $Matches[1].Trim()
}

<#
    The compose files this machine runs Postgres with, in order: docker-compose.yml and
    compose/dev.yml always, and compose/tailnet-db.yml when .env names a TAILNET_IP (ADR 0023).

    A script that recreates the container with a shorter list than the one it was started with
    drops whatever the missing overlay published: run on the desktop, a restore that named only
    the first two took the tailnet port away from the laptop until someone noticed. So every
    script that names the list asks for it here, and the list follows .env, which is where the
    machine already says it is the one the tailnet reaches.

    XLINES_COMPOSE_FILES, semicolon-separated, replaces the list outright, for a machine whose
    overlays are not the ones .env implies.
#>
function Get-ComposeFiles([string]$Root) {
    if ($env:XLINES_COMPOSE_FILES) {
        return @($env:XLINES_COMPOSE_FILES -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }

    $files = @('docker-compose.yml', 'compose/dev.yml')
    $envFile = Join-Path $Root '.env'
    if (Test-Path $envFile) {
        $text = [string](Get-Content $envFile -Raw)
        # An uncommented assignment, as compose reads it: an unquoted value ends at " #", and the
        # last assignment wins.
        $found = [regex]::Matches($text, '(?m)^[ \t]*(?:export[ \t]+)?TAILNET_IP[ \t]*=[ \t]*(?:"([^"]*)"|''([^'']*)''|([^#\r\n]*))')
        if ($found.Count -gt 0) {
            $last = $found[$found.Count - 1]
            $value = ($last.Groups[1].Value + $last.Groups[2].Value + $last.Groups[3].Value).Trim()
            if ($value) { $files += 'compose/tailnet-db.yml' }
        }
    }
    return $files
}

# The list as docker compose's arguments: -f a -f b ...
function Get-ComposeArgs([string]$Root) {
    $composeArgs = @()
    foreach ($file in @(Get-ComposeFiles $Root)) { $composeArgs += @('-f', $file) }
    return $composeArgs
}

# The command a person should run, for an error message: "docker compose -f a -f b up -d postgres".
function Format-ComposeCommand([string]$Root, [string]$Tail = 'up -d postgres') {
    "docker compose $((Get-ComposeArgs $Root) -join ' ') $Tail"
}

<#
    Records a snapshot published or restored in "DataTransfers" (ADR 0025), so a machine can say
    when it last sent or took the data. Best-effort by design: a database from before migration
    JournalSync has no such table, and the insert is skipped without a word; any other failure is
    reported in one line and never fails the script that called it. The SQL goes over stdin, for
    the reason Invoke-PgSidecar gives.
#>
function Add-DataTransfer {
    param(
        [Parameter(Mandatory)] [string]$Container,
        [Parameter(Mandatory)] [string]$Password,
        [Parameter(Mandatory)] [string]$User,
        [Parameter(Mandatory)] [string]$Database,
        [Parameter(Mandatory)] [ValidateSet('SnapshotPublished', 'SnapshotRestored')] [string]$Kind,
        [Parameter(Mandatory)] [string]$File,
        [string]$Manifest
    )

    function Quote([string]$Text) { "'" + $Text.Replace("'", "''") + "'" }

    $manifestSql = if ($Manifest -and $Manifest.Trim()) { "$(Quote $Manifest.Trim())::jsonb" } else { 'NULL' }
    $sql = @'
DO $$ BEGIN
  IF to_regclass('public."DataTransfers"') IS NOT NULL THEN
    INSERT INTO "DataTransfers" ("Kind", "AtUtc", "Machine", "Detail")
    VALUES (__KIND__, now(), __MACHINE__, jsonb_strip_nulls(jsonb_build_object('file', __FILE__::text, 'manifest', __MANIFEST__)));
  END IF;
END $$;
'@
    $sql = $sql.Replace('__KIND__', (Quote $Kind)).Replace('__MACHINE__', (Quote ([Environment]::MachineName))).Replace('__FILE__', (Quote $File)).Replace('__MANIFEST__', $manifestSql)

    $ErrorActionPreference = 'Continue'
    $out = $sql | & docker exec -i -e "PGPASSWORD=$Password" $Container psql -U $User -d $Database -X -q -v ON_ERROR_STOP=1 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Host "Transfer not logged (the data itself is fine): $(($out | Out-String).Trim())" }
}

<#
    Runs one of the Worker's journal jobs (journal:export, journal:import; ADR 0025) and returns
    its exit code. Used by publish-journal.ps1 and restore-journal.ps1. The Worker reads the
    database from its own configuration, with the password from .env passed as PGPASSWORD, which
    Npgsql reads; -ConnectionString points it somewhere else. Its logging is turned down to
    warnings, so what prints is the job's own report.
#>
function Invoke-JournalJob {
    param(
        [Parameter(Mandatory)] [string]$Root,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [string]$ConnectionString
    )

    $project = Join-Path (Join-Path $Root 'src') 'XLines.Worker'
    $dotnetArgs = @('run', '--project', $project, '--') + $Arguments + @('--Logging:LogLevel:Default=Warning')
    if ($ConnectionString) { $dotnetArgs += "--ConnectionStrings:LineOps=$ConnectionString" }

    $saved = $env:PGPASSWORD
    try {
        if (-not $ConnectionString) { $env:PGPASSWORD = Get-PgPassword $Root }
        $ErrorActionPreference = 'Continue'
        & dotnet @dotnetArgs | Out-Host
        return $LASTEXITCODE
    }
    finally { $env:PGPASSWORD = $saved }
}
