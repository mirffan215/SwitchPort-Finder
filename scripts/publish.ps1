# Builds the React frontend into the API's wwwroot and publishes a deployment folder.
# Usage: .\scripts\publish.ps1 [-Output .\publish] [-Runtime win-x64]
param([string]$Output = ".\publish", [string]$Runtime = "", [switch]$Zip)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Write-Host "==> Building frontend"
# Windows PowerShell 5.1 turns anything a native tool writes to stderr (e.g. npm notices) into a terminating error under
# $ErrorActionPreference = "Stop", so run the native tools with "Continue" and check their exit codes explicitly.
Push-Location "$root\src\SwitchPortFinder.Web"
$ErrorActionPreference = "Continue"
npm ci --no-audit --no-fund 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm ci failed (exit $LASTEXITCODE)" }
npm run build 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "npm run build failed (exit $LASTEXITCODE)" }
$ErrorActionPreference = "Stop"
Pop-Location
Write-Host "==> Publishing API (frontend is served from wwwroot by Kestrel)"
$args = @("-c", "Release", "-o", $Output, "$root\src\SwitchPortFinder.Api")
if ($Runtime) { $args += @("-r", $Runtime, "--self-contained", "false") }
$ErrorActionPreference = "Continue"
dotnet publish @args 2>&1 | ForEach-Object { "$_" }
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
$ErrorActionPreference = "Stop"
# ship the installer, helper scripts and the SQL script inside the package so it can be installed anywhere without the repository
foreach ($f in 'install.ps1', 'uninstall.ps1', 'backup.ps1', 'SpfInstall.Common.ps1') { Copy-Item (Join-Path $PSScriptRoot $f) $Output -Force }
New-Item -ItemType Directory -Force -Path (Join-Path $Output 'docs') | Out-Null
foreach ($f in 'schema.sql', 'INSTALLATION.md', 'CONFIGURATION.md', 'TROUBLESHOOTING.md') { Copy-Item (Join-Path $root "docs\$f") (Join-Path $Output 'docs') -Force }
if ($Zip) { $zipPath = "$($Output.TrimEnd('\','/')).zip"; if (Test-Path $zipPath) { Remove-Item $zipPath }; Compress-Archive -Path (Join-Path $Output '*') -DestinationPath $zipPath; Write-Host "==> Package: $zipPath" }
Write-Host "==> Done. Install on the server with:  .\install.ps1   (from the package folder, as Administrator)"
