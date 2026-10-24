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
    127.0.0.1:5432 whether or not compose.dev.yml publishes it.
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

    $ErrorActionPreference = 'Continue'
    $Script | & docker @dockerArgs | Out-Host
    return $LASTEXITCODE
}

function Get-PgPassword([string]$Root) {
    $envFile = Join-Path $Root '.env'
    if (-not (Test-Path $envFile)) { throw ".env is missing. Run ./scripts/setup.ps1 first: it generates the database password the container needs." }
    $text = Get-Content $envFile -Raw
    if ($text -notmatch '(?m)^POSTGRES_PASSWORD=(.+)$') { throw ".env has no POSTGRES_PASSWORD. Run ./scripts/setup.ps1 first." }
    $Matches[1].Trim()
}
