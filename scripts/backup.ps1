#Requires -Version 5.1
<#
.SYNOPSIS  Backs up the database AND the keys/certificate/configuration (both are needed to restore).
.DESCRIPTION
  1. BACKUP DATABASE (compressed, checksummed) to SQL Server's default backup folder, then copies it.
  2. Zips keys, certificate and configuration. The zip contains secrets (encryption keys): store -Destination somewhere only administrators can read.
  Run as an account that can back up the database (sysadmin / db_backupoperator). To schedule: Task Scheduler -> run this script daily as that account.
#>
[CmdletBinding()]
param(
    [string]$SqlInstance = '.',
    [string]$Database = 'SwitchPortFinder',
    [string]$DataPath = (Join-Path $env:ProgramData 'SwitchPortFinder'),
    [string]$InstallPath = 'C:\SwitchPortFinder',
    [Parameter(Mandatory)][string]$Destination,
    [int]$KeepDays = 14
)
$ErrorActionPreference = 'Stop'
if ($Database -notmatch '^[A-Za-z0-9_\-]{1,100}$') { throw 'Invalid database name.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $Destination $stamp
New-Item -ItemType Directory -Force -Path $target | Out-Null

$conn = New-Object System.Data.SqlClient.SqlConnection "Server=$SqlInstance;Database=master;Trusted_Connection=True;TrustServerCertificate=True"
$conn.Open()
try {
    $cmd = $conn.CreateCommand(); $cmd.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(512))"
    $dir = [string]$cmd.ExecuteScalar()
    if (-not $dir) { throw 'Could not determine the SQL Server default backup folder.' }
    $bak = Join-Path $dir "$Database-$stamp.bak"
    $cmd = $conn.CreateCommand(); $cmd.CommandTimeout = 0
    $cmd.CommandText = "BACKUP DATABASE [$Database] TO DISK = @f WITH COMPRESSION, CHECKSUM, INIT, NAME = N'SwitchPortFinder-$stamp'"
    [void]$cmd.Parameters.AddWithValue('@f', $bak)
    [void]$cmd.ExecuteNonQuery()
    $cmd = $conn.CreateCommand(); $cmd.CommandTimeout = 0
    $cmd.CommandText = "RESTORE VERIFYONLY FROM DISK = @f WITH CHECKSUM"; [void]$cmd.Parameters.AddWithValue('@f', $bak); [void]$cmd.ExecuteNonQuery()
    Write-Host "Database backup verified: $bak"
} finally { $conn.Close() }
Move-Item -Path $bak -Destination $target -Force -ErrorAction SilentlyContinue
if (Test-Path $bak) { Copy-Item $bak $target -Force }   # SQL service account may own the original; copy if it could not be moved

$items = @((Join-Path $DataPath 'keys'), (Join-Path $DataPath 'cert'), (Join-Path $InstallPath 'appsettings.Production.json')) | Where-Object { Test-Path $_ }
Compress-Archive -Path $items -DestinationPath (Join-Path $target 'keys-cert-config.zip') -Force
Write-Host "Keys, certificate and configuration archived: $(Join-Path $target 'keys-cert-config.zip')  (CONTAINS SECRETS - protect this folder)"

Get-ChildItem $Destination -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' -and $_.LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) } | Remove-Item -Recurse -Force
Write-Host "Done. Backups older than $KeepDays days were removed."
