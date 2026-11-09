#Requires -Version 5.1
#Requires -RunAsAdministrator
<# Removes the service, firewall rule and certificate. Database, data folder and install folder are KEPT unless you ask. #>
[CmdletBinding()]
param(
    [string]$InstallPath = 'C:\SwitchPortFinder',
    [string]$DataPath = (Join-Path $env:ProgramData 'SwitchPortFinder'),
    [switch]$RemoveInstallFolder,
    # Deletes keys, certificate and logs. Without the keys, stored SNMP credentials in a kept database can no longer be decrypted.
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'
$svc = Get-Service -Name SwitchPortFinder -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service SwitchPortFinder -Force; $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60)) }
    & sc.exe delete SwitchPortFinder | Out-Host; Write-Host 'Service removed.'
}
Get-NetFirewallRule -DisplayName 'SwitchPort Finder*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Get-ChildItem Cert:\LocalMachine\My, Cert:\LocalMachine\Root | Where-Object { $_.FriendlyName -eq 'SwitchPort Finder' } | Remove-Item
Write-Host 'Firewall rule and certificate removed.'
if ($RemoveInstallFolder -and (Test-Path $InstallPath)) { Remove-Item $InstallPath -Recurse -Force; Write-Host "Removed $InstallPath" }
if ($RemoveData -and (Test-Path $DataPath)) { Remove-Item $DataPath -Recurse -Force; Write-Host "Removed $DataPath" }
Write-Host 'The SQL Server database was NOT touched. To delete it: DROP DATABASE [SwitchPortFinder]; (and DROP LOGIN [NT SERVICE\SwitchPortFinder];)'
