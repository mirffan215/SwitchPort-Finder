# Builds the React frontend into the API's wwwroot and publishes a deployment folder.
# Usage: .\scripts\publish.ps1 [-Output .\publish] [-Runtime win-x64]
param([string]$Output = ".\publish", [string]$Runtime = "", [switch]$Zip)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Write-Host "==> Building frontend"
Push-Location "$root\src\SwitchPortFinder.Web"; npm ci; npm run build; Pop-Location
Write-Host "==> Publishing API (frontend is served from wwwroot by Kestrel)"
$args = @("-c", "Release", "-o", $Output, "$root\src\SwitchPortFinder.Api")
if ($Runtime) { $args += @("-r", $Runtime, "--self-contained", "false") }
dotnet publish @args
# ship the installer, helper scripts and the SQL script inside the package so it can be installed anywhere without the repository
foreach ($f in 'install.ps1', 'uninstall.ps1', 'backup.ps1', 'SpfInstall.Common.ps1') { Copy-Item (Join-Path $PSScriptRoot $f) $Output -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'docs') | Out-Null
foreach ($f in 'schema.sql', 'INSTALLATION.md', 'CONFIGURATION.md', 'TROUBLESHOOTING.md') { Copy-Item (Join-Path $root "docs\$f") (Join-Path $Output 'docs') -Force }
if ($Zip) { $zipPath = "$($Output.TrimEnd('\','/')).zip"; if (Test-Path $zipPath) { Remove-Item $zipPath }; Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zipPath; Write-Host "==> Package: $zipPath" }
Write-Host "==> Done. Install on the server with:  .\install.ps1   (from the package folder, as Administrator)"
