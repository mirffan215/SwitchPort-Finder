# Builds the React frontend into the API's wwwroot and publishes a deployment folder.
# Usage: .\scripts\publish.ps1 [-Output .\publish] [-Runtime win-x64]
param([string]$Output = ".\publish", [string]$Runtime = "")
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Write-Host "==> Building frontend"
Push-Location "$root\src\SwitchPortFinder.Web"; npm ci; npm run build; Pop-Location
Write-Host "==> Publishing API (frontend is served from wwwroot by Kestrel)"
$args = @("-c", "Release", "-o", $Output, "$root\src\SwitchPortFinder.Api")
if ($Runtime) { $args += @("-r", $Runtime, "--self-contained", "false") }
dotnet publish @args
Write-Host "==> Done. Run:  cd $Output; dotnet SwitchPortFinder.Api.dll"
