<#
.SYNOPSIS
    Point this machine's desk at the other machine's Postgres over the tailnet, or back at its own.

.DESCRIPTION
    Run on the laptop (ADR 0023). Writes into src/XLines.Web/appsettings.Local.json, the
    gitignored file the web host reads last, so it wins over appsettings.json and user-secrets:

      ConnectionStrings:LineOps   the remote database, SSL Mode=Disable (compose/tailnet-db.yml
                                  says why that is acceptable over the tailnet and nowhere else)
      Ingestion:HostScheduler     false: this machine never polls on a schedule
      Reliability:HostEvaluator   false: this machine never evaluates

    The two switches are what keeps the laptop from being a second spender of the odds credits.
    The web host refuses to start against a remote database without them (SchedulerPlacement).

    Every other key in the file is kept as it is. The file is read and written as JSON, so a file
    holding // comments (which the configuration provider tolerates but JSON does not) is refused
    rather than rewritten without them.

    The password is read from .env, as the other scripts read it (scripts/_pg.ps1):
    REMOTE_POSTGRES_PASSWORD when present, which is the desktop's POSTGRES_PASSWORD copied to the
    laptop, and otherwise POSTGRES_PASSWORD, for machines set up with the same one. -Local always
    uses POSTGRES_PASSWORD, this machine's own.

    -Local puts back the loopback connection string setup.ps1 writes and removes both switches,
    so the desk schedules and evaluates again against its own database.

    Runs under Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
    .\scripts\connect-remote-db.ps1 -Host desktop          # MagicDNS name of the desktop
    .\scripts\connect-remote-db.ps1 -Host 100.101.102.103 -Port 5433
    .\scripts\connect-remote-db.ps1 -Local                 # back to this machine's own database
#>
param(
    [Alias('Host')] [string]$RemoteHost,
    [ValidateRange(1, 65535)] [int]$Port = 5433,
    [switch]$Local,
    # The file to update. Defaults to the web host's; named so a scratch copy can be tried first.
    [string]$Path
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot '_pg.ps1')
$root = Split-Path $PSScriptRoot -Parent
if (-not $Path) { $Path = Join-Path (Join-Path (Join-Path $root 'src') 'XLines.Web') 'appsettings.Local.json' }

# Windows PowerShell 5.1 ships type data that makes ConvertTo-Json write an array that passed
# through ConvertFrom-Json as {"value": [...], "Count": n}. Removing it for this session restores
# plain arrays; PowerShell 7 has no such entry.
if ($PSVersionTable.PSVersion.Major -lt 6) { Remove-TypeData System.Array -ErrorAction SilentlyContinue }

if ($Local -and $RemoteHost) { throw "Give -Host or -Local, not both." }
if (-not $Local -and -not $RemoteHost) { throw "Give -Host <tailnet name or 100.x address> of the machine with the database, or -Local." }
if ($RemoteHost -and $RemoteHost -notmatch '^[A-Za-z0-9.\-]+$|^[0-9A-Fa-f:]+$') {
    throw "'$RemoteHost' is not a host name or address. Give the desktop's MagicDNS name or its 100.x address (``tailscale ip -4`` on the desktop)."
}

function Get-EnvValue([string]$Name) {
    $envFile = Join-Path $root '.env'
    if (-not (Test-Path $envFile)) { return $null }
    $line = Get-Content $envFile | Where-Object { $_ -match "^$Name=(.+)$" } | Select-Object -First 1
    if ($line) { ($line -replace "^$Name=", '').Trim() } else { $null }
}

function Set-Member($Object, [string]$Name, $Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

function Get-Section($Object, [string]$Name) {
    $section = $Object.PSObject.Properties[$Name]
    if ($section -and $section.Value -is [pscustomobject]) { return $section.Value }
    if ($section) { throw "$Path has '$Name' as something other than an object; fix it by hand first." }
    $new = New-Object pscustomobject
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $new
    return $new
}

function Remove-Member($Object, [string]$Section, [string]$Name) {
    $s = $Object.PSObject.Properties[$Section]
    if (-not $s -or $s.Value -isnot [pscustomobject]) { return }
    $s.Value.PSObject.Properties.Remove($Name)
    # A section this script created and has now emptied goes with it.
    if (@($s.Value.PSObject.Properties).Count -eq 0) { $Object.PSObject.Properties.Remove($Section) }
}

# Read what is there, keeping every key; refuse anything that is not plain JSON.
if (Test-Path $Path) {
    $text = [IO.File]::ReadAllText((Resolve-Path $Path).Path)
    if ($text.Trim().Length -eq 0) { $settings = New-Object pscustomobject }
    else {
        try { $settings = $text | ConvertFrom-Json }
        catch { throw "$Path is not plain JSON (a // comment?), so it is left untouched: $($_.Exception.Message)" }
        if ($settings -isnot [pscustomobject]) { throw "$Path does not hold a JSON object; left untouched." }
    }
    $existed = $true
}
else { $settings = New-Object pscustomobject; $existed = $false }

$connections = Get-Section $settings 'ConnectionStrings'

if ($Local) {
    $password = Get-PgPassword $root
    # The same string setup.ps1 puts in user-secrets: this machine's own compose/dev.yml port.
    Set-Member $connections 'LineOps' "Host=localhost;Port=5433;Database=lineops;Username=lineops;Password=$password"
    Remove-Member $settings 'Ingestion' 'HostScheduler'
    Remove-Member $settings 'Reliability' 'HostEvaluator'
    $target = 'localhost:5433 (this machine)'
}
else {
    $password = Get-EnvValue 'REMOTE_POSTGRES_PASSWORD'
    $passwordFrom = 'REMOTE_POSTGRES_PASSWORD'
    if (-not $password) { $password = Get-PgPassword $root; $passwordFrom = 'POSTGRES_PASSWORD' }

    # SSL Mode=Disable: the tailnet already encrypts device to device (compose/tailnet-db.yml).
    Set-Member $connections 'LineOps' "Host=$RemoteHost;Port=$Port;Database=lineops;Username=lineops;Password=$password;SSL Mode=Disable;Keepalive=30;Tcp Keepalive=true"
    Set-Member (Get-Section $settings 'Ingestion') 'HostScheduler' $false
    Set-Member (Get-Section $settings 'Reliability') 'HostEvaluator' $false
    $target = "${RemoteHost}:$Port (over the tailnet)"
}

$json = $settings | ConvertTo-Json -Depth 32
# UTF-8 without a byte-order mark, the same bytes from either PowerShell.
[IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding $false))

$shown = $connections.XLines -replace 'Password=[^;]*', 'Password=***'
Write-Host "$(if ($existed) { 'Updated' } else { 'Created' }) $Path"
Write-Host "  ConnectionStrings:LineOps = $shown"
if ($Local) {
    Write-Host "  Ingestion:HostScheduler and Reliability:HostEvaluator removed: this desk schedules and evaluates again."
}
else {
    Write-Host "  Password from .env's $passwordFrom."
    Write-Host "  Ingestion:HostScheduler = false, Reliability:HostEvaluator = false: this desk never schedules or evaluates."

    # A reachability check, not a sign-in: says early whether the tailnet and the port are up.
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $attempt = $client.BeginConnect($RemoteHost, $Port, $null, $null)
        if ($attempt.AsyncWaitHandle.WaitOne(3000) -and $client.Connected) { Write-Host "  $target answers." -ForegroundColor Green }
        else { Write-Host "  $target did not answer in 3 s. Is the desktop on, Tailscale up, and compose/tailnet-db.yml in its compose list?" -ForegroundColor Yellow }
    }
    catch { Write-Host "  $target could not be reached: $($_.Exception.Message)" -ForegroundColor Yellow }
    finally { $client.Close() }
}
Write-Host "Restart the desk for it to take effect: dotnet run --project src/XLines.Web --launch-profile http"
