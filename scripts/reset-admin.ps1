<#
.SYNOPSIS
  Sets a new password for a SwitchPort Finder account (default: admin) when nobody can sign in, e.g. the generated password was not noted.
.DESCRIPTION
  Run as Administrator on the server. It asks for the new password (hidden), updates the account in the database, clears a lockout and,
  with -ResetTwoFactor, removes the account's authenticator apps so it can enrol again. Then it restarts the service.
  .\reset-admin.ps1
  .\reset-admin.ps1 -UserName admin -ResetTwoFactor
#>
param(
    [string]$UserName = 'admin',
    [string]$InstallPath = 'C:\SwitchPortFinder',
    [switch]$ResetTwoFactor
)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $InstallPath 'SwitchPortFinder.Api.exe'
if (-not (Test-Path $exe)) { Write-Host "ERROR: $exe not found. Use -InstallPath if the application is installed elsewhere." -ForegroundColor Red; exit 1 }
$p1 = Read-Host "New password for '$UserName' (at least 12 characters)" -AsSecureString
$p2 = Read-Host 'Repeat the new password' -AsSecureString
$a = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p1))
$b = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($p2))
if ($a -ne $b) { Write-Host 'ERROR: the two passwords are different.' -ForegroundColor Red; exit 1 }
$argv = @('--reset-password', $UserName); if ($ResetTwoFactor) { $argv += '--reset-mfa' }
$env:SPF_NEW_PASSWORD = $a
Push-Location $InstallPath
try { & $exe @argv; $code = $LASTEXITCODE } finally { Pop-Location; Remove-Item Env:\SPF_NEW_PASSWORD -ErrorAction SilentlyContinue }
if ($code -ne 0) { exit $code }
Restart-Service -Name SwitchPortFinder
Write-Host "Done. Sign in as '$UserName' with the new password." -ForegroundColor Green
