<#
.SYNOPSIS
    Makes an access code for a gated desk, and the hash that configuration holds instead of it.

.DESCRIPTION
    A gated desk (Access:Mode = Gated, ADR 0018) opens to a code: the operator's opens
    everything, the guest code opens a desk that reads and changes nothing. Configuration never
    holds a code, only its PBKDF2 hash, so this prints the code once - keep it somewhere safe,
    because nothing can recover it - and the line to put in the server's .env.

    The hash is the format LineOps.Web.Access.AccessCode checks:
    pbkdf2-sha256.<iterations>.<salt>.<hash>, salt and hash in base64.

.EXAMPLE
    ./scripts/new-access-code.ps1                    # a new operator code
    ./scripts/new-access-code.ps1 -Role Guest        # a new guest code
    ./scripts/new-access-code.ps1 -Code 'chosen'     # hash a code you chose yourself
#>
[CmdletBinding()]
param(
    [ValidateSet('Operator', 'Guest')]
    [string]$Role = 'Operator',

    # A code of your own. Left out, a random one is made: 20 characters from an alphabet
    # without look-alikes, which is about 100 bits.
    [string]$Code
)

$ErrorActionPreference = 'Stop'

$iterations = 600000

if (-not $Code) {
    $alphabet = 'abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789'
    $bytes = New-Object byte[] 20
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $Code = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
    # Grouped for reading aloud or typing; the dashes are part of the code.
    $Code = ($Code -split '(.{5})' | Where-Object { $_ }) -join '-'
}

$salt = New-Object byte[] 16
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($salt)

# The constructor rather than the static Pbkdf2, so Windows PowerShell 5.1 runs this too.
$kdf = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
    [System.Text.Encoding]::UTF8.GetBytes($Code), $salt, $iterations,
    [System.Security.Cryptography.HashAlgorithmName]::SHA256)
try {
    $hash = $kdf.GetBytes(32)
}
finally {
    $kdf.Dispose()
}

$stored = "pbkdf2-sha256.$iterations.$([Convert]::ToBase64String($salt)).$([Convert]::ToBase64String($hash))"
$variable = if ($Role -eq 'Operator') { 'ACCESS_OPERATOR_CODE_HASH' } else { 'ACCESS_GUEST_CODE_HASH' }
$setting = if ($Role -eq 'Operator') { 'Access:OperatorCodeHash' } else { 'Access:GuestCodeHash' }

Write-Host ""
Write-Host "$Role code (shown once - store it in a password manager):" -ForegroundColor Cyan
Write-Host "  $Code"
Write-Host ""
Write-Host "For the server's .env (deploy/.env.example):" -ForegroundColor Cyan
Write-Host "  $variable=$stored"
Write-Host ""
Write-Host "Or for a host run from source (user-secrets, never appsettings):" -ForegroundColor Cyan
Write-Host "  dotnet user-secrets --project src/LineOps.Web set $setting '$stored'"
Write-Host ""
