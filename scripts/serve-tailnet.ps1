<#
.SYNOPSIS
    Puts the desk you are running with `dotnet run` on your own tailnet, over HTTPS, and takes
    it off again with -Off.

.DESCRIPTION
    For the desktop that runs the desk from source:

        dotnet run --project src/XLines.Web --launch-profile http

    Tailscale Serve on this machine then terminates HTTPS at https://<machine>.<tailnet>.ts.net
    and proxies to the http profile's port on 127.0.0.1. Only devices signed in to your tailnet
    (and allowed by its ACLs) can reach it. Nothing is published to the internet: that would be
    Tailscale Funnel, which this script never runs and refuses to leave on. See ADR 0022.

    Two settings must be on in the desk this proxies to, in src/XLines.Web/appsettings.Local.json
    (gitignored, read on every start; make the hash with scripts/new-access-code.ps1):

        {
          "Hosting": { "BehindProxy": true },
          "Access": {
            "Mode": "Gated",
            "OperatorCodeHash": "pbkdf2-sha256.600000.<salt>.<hash>"
          },
          "AllowedHosts": "localhost;<machine>.<tailnet>.ts.net"
        }

    Hosting:BehindProxy makes the desk believe Serve's X-Forwarded-For and -Proto, so the
    sign-in limit counts your phone's address rather than 127.0.0.1 and the request is known to
    be HTTPS. It is safe here because the http profile listens on localhost only: nothing but a
    process on this machine, tailscaled included, can reach the port. Never combine it with a
    listener on 0.0.0.0.

    Access:Mode Gated puts a code in front of the desk. The tailnet is yours, but a node you
    share, or a second person you invite, is on it too; and Open mode lets whoever reaches the
    desk spend odds credits and read the journal (ADR 0018).

    AllowedHosts is optional tightening: with it, a request naming any other host gets a 400.
    Keep localhost in the list or the desk refuses you at http://localhost:<port>. This script
    prints the exact value once Serve is on.

    One-time, by hand: install Tailscale on this machine and on each device you want to use,
    sign in to the same tailnet, and in the admin console's DNS page enable MagicDNS and HTTPS
    certificates. The first HTTPS request may take a few seconds while the certificate is issued.

.PARAMETER Port
    The port to proxy to. Left out, the http profile's port is read from
    src/XLines.Web/Properties/launchSettings.json.

.PARAMETER Database
    Forwards this machine's Postgres to the tailnet instead of the desk: raw TCP on
    -DatabasePort, to the same port on 127.0.0.1 where compose/dev.yml publishes it. This is
    how the laptop's desk reaches the desktop's database (ADR 0023). Postgres stays bound to
    loopback, so there is no Docker port on the tailnet address to fail at boot and no
    firewall rule to keep. Limit who may reach the port in the tailnet policy: Postgres sees
    every tailnet client as 127.0.0.1 and cannot tell them apart itself.

.PARAMETER DatabasePort
    The Postgres port, on both sides of the forward. 5433 unless you changed compose/dev.yml.

.PARAMETER Off
    Stops serving HTTPS on 443 from this machine, or with -Database stops the Postgres
    forward. Other Serve handlers are left alone; `tailscale serve reset` clears all of them,
    the desk and the database alike.

.EXAMPLE
    ./scripts/serve-tailnet.ps1                  # serve the http profile's port on the tailnet
    ./scripts/serve-tailnet.ps1 -Off             # stop serving it
    ./scripts/serve-tailnet.ps1 -Database        # forward Postgres to the tailnet
    ./scripts/serve-tailnet.ps1 -Database -Off   # stop forwarding it
#>
[CmdletBinding()]
param(
    [int]$Port,
    [switch]$Database,
    [int]$DatabasePort = 5433,
    [switch]$Off
)

$ErrorActionPreference = 'Stop'

$tailscale = Get-Command tailscale -ErrorAction SilentlyContinue
if (-not $tailscale) {
    throw "The tailscale CLI is not on PATH. Install Tailscale (https://tailscale.com/download) and sign in first."
}

function Invoke-Tailscale {
    param([string[]]$Arguments)
    $output = & $tailscale.Source @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "tailscale $($Arguments -join ' ') failed ($LASTEXITCODE):`n$($output -join "`n")"
    }
    return ($output -join "`n")
}

# Serve and Funnel share one configuration. If Funnel is on for this host, what is served is on
# the public internet, which the data's terms do not allow. Returns the offending entries.
function Get-FunnelOn([string]$HostName) {
    $serve = Invoke-Tailscale @('serve', 'status', '--json') | ConvertFrom-Json
    if (-not $serve -or -not $serve.AllowFunnel) { return @() }
    return @($serve.AllowFunnel.PSObject.Properties | Where-Object { $_.Name -like "$HostName*" -and $_.Value })
}

if ($Off -and $Database) {
    Invoke-Tailscale @('serve', "--tcp=$DatabasePort", 'off') | Out-Null
    Write-Host "No longer forwarding Postgres to the tailnet." -ForegroundColor Cyan
    return
}

if ($Off) {
    Invoke-Tailscale @('serve', '--https=443', 'off') | Out-Null
    Write-Host "No longer serving the desk on the tailnet." -ForegroundColor Cyan
    return
}

$status = Invoke-Tailscale @('status', '--json') | ConvertFrom-Json
if ($status.BackendState -ne 'Running') {
    throw "Tailscale is '$($status.BackendState)', not Running. Sign in with 'tailscale up' (or the tray app) first."
}
$hostName = $status.Self.DNSName.TrimEnd('.')

# Before anything is served, not only after: a handler added while Funnel is on is public from
# its first second.
if ((Get-FunnelOn $hostName).Count -gt 0) {
    throw "Funnel is on for $hostName, which would publish whatever is served to the internet. Turn it off ('tailscale funnel reset') before running this again."
}

# Without unattended mode Tailscale disconnects when you sign out of Windows and stays down
# after a reboot until someone signs in, taking the desk and the database forward with it.
$prefs = $null
try { $prefs = Invoke-Tailscale @('debug', 'prefs') | ConvertFrom-Json } catch { }
if ($prefs -and -not $prefs.ForceDaemon) {
    Write-Warning "Unattended mode is off: this stops at sign-out and after a reboot until you sign in. Turn it on with: tailscale set --unattended=true"
}

if ($Database) {
    Invoke-Tailscale @('serve', '--bg', "--tcp=$DatabasePort", "tcp://127.0.0.1:$DatabasePort") | Out-Null
    if ((Get-FunnelOn $hostName).Count -gt 0) {
        Invoke-Tailscale @('serve', "--tcp=$DatabasePort", 'off') | Out-Null
        throw "Funnel is on for $hostName. The Postgres forward was taken down again."
    }
    Write-Host ""
    Write-Host "Postgres on your tailnet (tailnet devices only, never the internet):" -ForegroundColor Cyan
    Write-Host "  ${hostName}:$DatabasePort -> 127.0.0.1:$DatabasePort"
    Write-Host ""
    Write-Host "On the laptop: ./scripts/connect-remote-db.ps1 -Host $hostName" -ForegroundColor Cyan
    Write-Host "In the tailnet policy, allow tcp:$DatabasePort to this machine from the laptop only." -ForegroundColor Cyan
    Write-Host "Stop with: ./scripts/serve-tailnet.ps1 -Database -Off" -ForegroundColor DarkGray
    return
}

if (-not $status.CurrentTailnet.MagicDNSEnabled) {
    throw "MagicDNS is off for this tailnet. Turn it on in the admin console (DNS page); Serve's HTTPS name depends on it."
}
if (-not $status.CertDomains) {
    throw "HTTPS certificates are off for this tailnet. Turn them on in the admin console (DNS page, HTTPS Certificates)."
}

if (-not $Port) {
    $launch = Join-Path $PSScriptRoot '..\src\XLines.Web\Properties\launchSettings.json'
    $Port = 5000
    if (Test-Path $launch) {
        $url = (Get-Content $launch -Raw | ConvertFrom-Json).profiles.http.applicationUrl
        if ($url -match ':(\d+)') { $Port = [int]$Matches[1] }
    }
}

Invoke-Tailscale @('serve', '--bg', '--https=443', "http://127.0.0.1:$Port") | Out-Null

# Checked again now that the handler exists: take it down rather than warn.
if ((Get-FunnelOn $hostName).Count -gt 0) {
    Invoke-Tailscale @('serve', '--https=443', 'off') | Out-Null
    throw "Funnel is on for $hostName, which would publish the desk to the internet. Serving stopped; turn Funnel off ('tailscale funnel --https=443 off') before running this again."
}

Write-Host ""
Write-Host "The desk on your tailnet (tailnet devices only, never the internet):" -ForegroundColor Cyan
Write-Host "  https://$hostName"
Write-Host "  -> http://127.0.0.1:$Port"
Write-Host ""
Write-Host "In src/XLines.Web/appsettings.Local.json, if not already:" -ForegroundColor Cyan
Write-Host "  Hosting:BehindProxy = true, Access:Mode = Gated (see Get-Help $PSCommandPath)"
Write-Host "  `"AllowedHosts`": `"localhost;$hostName`""
Write-Host ""
Write-Host "Stop with: ./scripts/serve-tailnet.ps1 -Off" -ForegroundColor DarkGray
