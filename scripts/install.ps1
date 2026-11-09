#Requires -Version 5.1
#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Installs SwitchPort Finder on Windows Server (2019/2022): database, HTTPS, local login, Windows service, firewall.

.DESCRIPTION
  Run it from the deployment package (the folder that contains SwitchPortFinder.Api.exe) or from a repository checkout (it then builds first).
  What it does, in order:
    1. checks prerequisites (.NET 8 ASP.NET Core runtime, SQL Server service, free HTTPS port)
    2. copies the application to -InstallPath (keeps an existing appsettings.Production.json when -KeepConfig)
    3. creates a self-signed HTTPS certificate for this machine (stored in LocalMachine\My, trusted on this server) and exports a .cer for client PCs
    4. writes appsettings.Production.json (HTTPS only on the network, local login, file logging, least-privilege database settings)
    5. creates the Windows service running as a virtual account (NT SERVICE\SwitchPortFinder), delayed-auto start, restart on failure
    6. creates the database, applies the migrations and grants the service account db_datareader + db_datawriter ONLY
    7. opens the Windows Firewall port (Domain + Private profiles), starts the service and verifies HTTPS and the administrator login
    8. removes the bootstrap administrator password from the configuration file again

.EXAMPLE
  .\install.ps1
  Everything local: SQL Server on this machine (default instance), https://<this-computer>/ , administrator "admin" with a generated password.

.EXAMPLE
  .\install.ps1 -HostName spf.corp.local -SqlInstance .\SQLEXPRESS -HttpsPort 8443 -AdminPassword (Read-Host -AsSecureString)

.NOTES
  The person running the script needs rights on SQL Server to create a database and a login (sysadmin, or dbcreator + securityadmin).
  Re-running is safe (upgrade): files are replaced, the schema is migrated, data is kept.
#>
[CmdletBinding()]
param(
    # Name users type in the browser. Defaults to this computer's name.
    [string]$HostName = $env:COMPUTERNAME,
    [string]$InstallPath = 'C:\SwitchPortFinder',
    # Keys, certificate and logs. Back this folder up (see backup.ps1).
    [string]$DataPath = (Join-Path $env:ProgramData 'SwitchPortFinder'),
    # '.' = default instance on this machine; '.\SQLEXPRESS' = SQL Express; 'SQLHOST' = remote (then the service account needs a login there).
    [string]$SqlInstance = '.',
    [string]$Database = 'SwitchPortFinder',
    # Full connection string override (e.g. SQL authentication). The installer then skips creating the service login.
    [string]$ConnectionString,
    [int]$HttpsPort = 443,
    [string]$AdminUser = 'admin',
    # If omitted a strong password is generated and shown ONCE at the end.
    [securestring]$AdminPassword,
    # Folder with the published application. Default: the folder this script is in, else build from the repository.
    [string]$PackagePath,
    [switch]$KeepConfig,
    [switch]$RenewCertificate,
    [switch]$InstallPrerequisites,
    [switch]$NoFirewall
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpfInstall.Common.ps1')

$ServiceName = 'SwitchPortFinder'
$ServiceAccount = "NT SERVICE\$ServiceName"
$CertFriendlyName = 'SwitchPort Finder'
$SidAdmins = '*S-1-5-32-544'; $SidSystem = '*S-1-5-18'
$ConfigPath = Join-Path $InstallPath 'appsettings.Production.json'

function Step($m)  { Write-Host "`n==> $m" -ForegroundColor Cyan }
function Ok($m)    { Write-Host "    OK  $m" -ForegroundColor Green }
function Warn($m)  { Write-Host "    !!  $m" -ForegroundColor Yellow }
function Fail($m)  { Write-Host "`nERROR: $m" -ForegroundColor Red; exit 1 }

function Invoke-Native {
    param([string]$File, [string[]]$Arguments, [switch]$AllowFail)
    & $File @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0 -and -not $AllowFail) { throw "$File exited with code $LASTEXITCODE" }
}

function Set-Acl-Restricted {
    # Only SYSTEM + Administrators (full) and the service account (as given) may touch the path; inheritance removed.
    param([string]$Path, [string]$ServiceRights)
    Invoke-Native icacls @($Path, '/inheritance:r', '/grant:r', "${SidSystem}:(OI)(CI)F", "${SidAdmins}:(OI)(CI)F", "${ServiceAccount}:(OI)(CI)$ServiceRights") | Out-Null
}

try {
    # ------------------------------------------------------------------ 1. prerequisites
    Step 'Checking prerequisites'
    if ($HttpsPort -lt 1 -or $HttpsPort -gt 65535) { Fail 'HttpsPort is not valid.' }
    if ($Database -notmatch '^[A-Za-z0-9_\-]{1,100}$') { Fail 'Database may contain only letters, digits, _ and -.' }

    $runtimes = & dotnet --list-runtimes 2>$null
    if (-not ($runtimes | Where-Object { $_ -match '^Microsoft\.AspNetCore\.App 8\.' })) {
        if ($InstallPrerequisites -and (Get-Command winget -ErrorAction SilentlyContinue)) {
            Warn 'Installing the ASP.NET Core 8 runtime with winget...'
            Invoke-Native winget @('install', '--id', 'Microsoft.DotNet.AspNetCore.8', '-e', '--accept-package-agreements', '--accept-source-agreements')
        } else {
            Fail "The ASP.NET Core 8 runtime is not installed. Install it from https://dotnet.microsoft.com/download/dotnet/8.0 (choose 'ASP.NET Core Runtime 8.x - Windows x64'), or re-run with -InstallPrerequisites."
        }
    }
    Ok 'ASP.NET Core 8 runtime present'

    $sqlService = $null
    if (-not $ConnectionString) {
        $sqlService = Get-SqlServiceName -SqlInstance $SqlInstance -ComputerName $env:COMPUTERNAME
        if ($sqlService) {
            $svc = Get-Service -Name $sqlService -ErrorAction SilentlyContinue
            if (-not $svc) { Fail "SQL Server service '$sqlService' was not found on this machine. Install SQL Server, or pass -SqlInstance <server\instance> for a remote server." }
            if ($svc.Status -ne 'Running') { Fail "SQL Server service '$sqlService' is not running." }
            Ok "SQL Server service $sqlService is running"
        } else { Warn "SQL Server '$SqlInstance' is not on this machine; the service account '$ServiceAccount' only exists locally, so use -ConnectionString with a SQL login for remote servers." }
    }

    $inUse = Get-NetTCPConnection -State Listen -LocalPort $HttpsPort -ErrorAction SilentlyContinue | Where-Object { $_.OwningProcess -ne 0 }
    $existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($inUse -and -not $existingSvc) { Fail "TCP port $HttpsPort is already in use (IIS?). Choose another with -HttpsPort, e.g. 8443." }

    # ------------------------------------------------------------------ 2. application files
    Step 'Preparing the application files'
    $source = $null
    if ($PackagePath) { $source = $PackagePath }
    elseif (Test-Path (Join-Path $PSScriptRoot 'SwitchPortFinder.Api.exe')) { $source = $PSScriptRoot }
    else {
        $repo = Split-Path $PSScriptRoot -Parent
        if (-not (Test-Path (Join-Path $repo 'src\SwitchPortFinder.Api'))) { Fail 'No application found. Run this script from the deployment package, or pass -PackagePath.' }
        foreach ($tool in 'dotnet', 'npm') { if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) { Fail "Building from source needs '$tool' (.NET 8 SDK and Node.js 22). Use a prebuilt package instead." } }
        $source = Join-Path $env:TEMP "spf-build-$([guid]::NewGuid().ToString('N').Substring(0,8))"
        Write-Host "    Building from source into $source ..."
        & (Join-Path $PSScriptRoot 'publish.ps1') -Output $source -Runtime win-x64
        if (-not (Test-Path (Join-Path $source 'SwitchPortFinder.Api.exe'))) { Fail 'The build did not produce SwitchPortFinder.Api.exe.' }
    }
    if (-not (Test-Path (Join-Path $source 'SwitchPortFinder.Api.exe'))) { Fail "SwitchPortFinder.Api.exe not found in '$source'." }
    Ok "Source: $source"

    # stop a running service before replacing files
    if ($existingSvc -and $existingSvc.Status -ne 'Stopped') {
        Write-Host '    Stopping the existing service...'; Stop-Service -Name $ServiceName -Force; $existingSvc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }
    New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
    # /MIR keeps the install folder identical to the package, except the local configuration
    $rc = & robocopy $source $InstallPath /MIR /XF appsettings.Production.json /XD logs keys /NFL /NDL /NJH /NJS /NP
    if ($LASTEXITCODE -ge 8) { Fail "File copy failed (robocopy exit code $LASTEXITCODE)." }
    Ok "Application copied to $InstallPath"

    foreach ($d in 'keys', 'logs', 'cert') { New-Item -ItemType Directory -Force -Path (Join-Path $DataPath $d) | Out-Null }

    # ------------------------------------------------------------------ 3. HTTPS certificate
    Step 'HTTPS certificate'
    $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '169.254.*' } | ForEach-Object { $_.IPAddress })
    $fqdn = $null
    try { $fqdn = [System.Net.Dns]::GetHostEntry($env:COMPUTERNAME).HostName } catch { }
    $dnsNames = @($HostName, $env:COMPUTERNAME, $fqdn, 'localhost')
    $cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object { $_.FriendlyName -eq $CertFriendlyName -and $_.NotAfter -gt (Get-Date).AddDays(30) -and ($_.DnsNameList.Unicode -contains $HostName) } |
        Sort-Object NotAfter -Descending | Select-Object -First 1
    if ($RenewCertificate -or -not $cert) {
        $san = Get-SpfSanText -DnsNames $dnsNames -IpAddresses ($ips + '127.0.0.1')
        $cert = New-SelfSignedCertificate -Subject "CN=$HostName" -FriendlyName $CertFriendlyName -CertStoreLocation Cert:\LocalMachine\My `
            -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -NotAfter (Get-Date).AddYears(3) `
            -KeyUsage DigitalSignature, KeyEncipherment -TextExtension @("2.5.29.17={text}$san", '2.5.29.37={text}1.3.6.1.5.5.7.3.1')
        Ok "Created a self-signed certificate for $HostName (valid 3 years)"
    } else { Ok "Reusing the existing certificate (expires $($cert.NotAfter.ToString('yyyy-MM-dd')))" }

    $pfxPath = Join-Path $DataPath 'cert\SwitchPortFinder.pfx'
    $cerPath = Join-Path $DataPath 'cert\SwitchPortFinder.cer'
    $pfxPassword = New-SpfPassword -Length 30
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password (ConvertTo-SecureString $pfxPassword -AsPlainText -Force) | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
    # trust it on THIS server so browsers/scripts here accept it; other PCs need the .cer (see the summary)
    if (-not (Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })) {
        Import-Certificate -FilePath $cerPath -CertStoreLocation Cert:\LocalMachine\Root | Out-Null
    }
    Ok "Certificate exported; public certificate for client PCs: $cerPath"

    # ------------------------------------------------------------------ 4. configuration
    Step 'Writing the configuration'
    $newInstall = -not (Test-Path $ConfigPath)
    $adminPlain = $null
    if ($KeepConfig -and -not $newInstall) {
        $cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
        $cfg.Kestrel.Endpoints.Https.Certificate.Path = $pfxPath
        $cfg.Kestrel.Endpoints.Https.Certificate.Password = $pfxPassword
        $cfg | ConvertTo-Json -Depth 12 | Set-Content -Path $ConfigPath -Encoding UTF8
        Ok 'Kept the existing configuration (certificate settings refreshed)'
    } else {
        if ($AdminPassword) {
            $adminPlain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($AdminPassword))
            $problem = Test-SpfPasswordPolicy -Password $adminPlain -UserName $AdminUser
            if ($problem) { Fail "AdminPassword: $problem" }
        } else { $adminPlain = New-SpfPassword -Length 24 }
        $cs = if ($ConnectionString) { $ConnectionString } else { New-SpfConnectionString -SqlInstance $SqlInstance -Database $Database }
        $hosts = @($HostName, $env:COMPUTERNAME, $fqdn, 'localhost', '127.0.0.1') + $ips
        $cfgObj = New-SpfConfig -ConnectionString $cs -KeysPath (Join-Path $DataPath 'keys') -LogDirectory (Join-Path $DataPath 'logs') `
            -PfxPath $pfxPath -PfxPassword $pfxPassword -HttpsPort $HttpsPort -AllowedHosts $hosts -AdminUser $AdminUser -AdminPassword $adminPlain
        ConvertTo-SpfJson $cfgObj | Set-Content -Path $ConfigPath -Encoding UTF8
        Ok 'appsettings.Production.json written (HTTPS only on the network, local login on, file logging on)'
    }

    # ------------------------------------------------------------------ 5. Windows service
    Step 'Windows service'
    $bin = "`"$(Join-Path $InstallPath 'SwitchPortFinder.Api.exe')`""
    if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
        Invoke-Native sc.exe @('create', $ServiceName, "binPath=", $bin, 'start=', 'delayed-auto', 'obj=', $ServiceAccount, 'DisplayName=', 'SwitchPort Finder') | Out-Null
        Ok "Service created, running as the virtual account $ServiceAccount"
    } else {
        Invoke-Native sc.exe @('config', $ServiceName, "binPath=", $bin, 'start=', 'delayed-auto', 'obj=', $ServiceAccount) | Out-Null
        Ok 'Service updated'
    }
    Invoke-Native sc.exe @('description', $ServiceName, 'SwitchPort Finder: finds which switch port a device is connected to') | Out-Null
    Invoke-Native sc.exe @('sidtype', $ServiceName, 'unrestricted') | Out-Null
    Invoke-Native sc.exe @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/5000/restart/5000/restart/30000') | Out-Null
    if ($sqlService) { Invoke-Native sc.exe @('config', $ServiceName, 'depend=', $sqlService) | Out-Null; Ok "Starts after $sqlService" }
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    New-ItemProperty -Path $regPath -Name 'Environment' -PropertyType MultiString -Value @('ASPNETCORE_ENVIRONMENT=Production') -Force | Out-Null

    # file permissions (the virtual account exists now)
    Set-Acl-Restricted -Path (Join-Path $DataPath 'keys') -ServiceRights 'M'
    Set-Acl-Restricted -Path (Join-Path $DataPath 'logs') -ServiceRights 'M'
    Set-Acl-Restricted -Path (Join-Path $DataPath 'cert') -ServiceRights 'R'
    Invoke-Native icacls @($ConfigPath, '/inheritance:r', '/grant:r', "${SidSystem}:F", "${SidAdmins}:F", "${ServiceAccount}:R") | Out-Null
    Invoke-Native icacls @($InstallPath, '/grant', "${ServiceAccount}:(OI)(CI)RX") | Out-Null
    Ok 'Permissions set (keys/logs: service modify; certificate and configuration: service read-only; everything else: administrators)'

    # ------------------------------------------------------------------ 6. database
    Step 'Database'
    $dbArgs = @('--setup-database')
    if (-not $ConnectionString -and $sqlService) { $dbArgs += @('--grant', $ServiceAccount) }
    Push-Location $InstallPath
    try { & (Join-Path $InstallPath 'SwitchPortFinder.Api.exe') @dbArgs | Out-Host; $dbExit = $LASTEXITCODE } finally { Pop-Location }
    if ($dbExit -ne 0) { Fail "Database setup failed. The account running this script needs permission to create a database and a login on '$SqlInstance' (sysadmin, or dbcreator + securityadmin)." }
    Ok "Database [$Database] ready; service account has read/write access only (no schema-change rights)"

    # ------------------------------------------------------------------ 7. firewall, start, verify
    Step 'Firewall'
    if ($NoFirewall) { Warn 'Skipped (-NoFirewall)' } else {
        Get-NetFirewallRule -DisplayName 'SwitchPort Finder*' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
        New-NetFirewallRule -DisplayName 'SwitchPort Finder (HTTPS)' -Direction Inbound -Protocol TCP -LocalPort $HttpsPort -Action Allow -Profile Domain, Private | Out-Null
        Ok "Inbound TCP $HttpsPort allowed (Domain and Private networks). Outbound UDP 161 to your switches must be allowed by your network policy."
    }

    Step 'Starting the service'
    Start-Service -Name $ServiceName
    $healthy = $false
    for ($i = 0; $i -lt 60 -and -not $healthy; $i++) {
        Start-Sleep -Seconds 2
        try { $healthy = (Invoke-RestMethod -Uri 'http://127.0.0.1:5000/api/health' -TimeoutSec 5).status -eq 'ok' } catch { }
    }
    if (-not $healthy) {
        $log = Get-ChildItem (Join-Path $DataPath 'logs') -Filter 'spf-*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($log) { Write-Host '--- last log lines ---'; Get-Content $log.FullName -Tail 25 }
        Fail "The service did not become healthy. See $(Join-Path $DataPath 'logs') and Event Viewer (Application)."
    }
    Ok 'Service is running'

    $base = if ($HttpsPort -eq 443) { "https://$HostName" } else { "https://${HostName}:$HttpsPort" }
    try { $h = Invoke-RestMethod -Uri "$base/api/health" -TimeoutSec 15; Ok "HTTPS works: $base ($($h.status))" }
    catch { Warn "HTTPS check failed from this machine: $($_.Exception.Message). If '$HostName' does not resolve here, add it to the hosts file or use -HostName with a resolvable name." }

    if ($adminPlain -and -not ($KeepConfig -and -not $newInstall)) {
        $loggedIn = $false
        try {
            $body = @{ username = $AdminUser; password = $adminPlain } | ConvertTo-Json
            $r = Invoke-RestMethod -Uri "$base/api/auth/login" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 15
            $loggedIn = [bool]$r.authenticated
        } catch { }
        if ($loggedIn) {
            Ok "Administrator '$AdminUser' can sign in"
            # the bootstrap password is only needed once: remove it from the file and restart
            $cfg = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            $cfg.Authentication.PSObject.Properties.Remove('BootstrapAdmin')
            $cfg | ConvertTo-Json -Depth 12 | Set-Content -Path $ConfigPath -Encoding UTF8
            Restart-Service -Name $ServiceName
            Start-Sleep -Seconds 5
            Ok 'Bootstrap password removed from the configuration file'
        } else {
            $adminPlain = $null
            Warn "Could not sign in as '$AdminUser' with the generated password: accounts already exist in this database (upgrade), so they were kept unchanged."
        }
    }

    # ------------------------------------------------------------------ summary
    Write-Host ''
    Write-Host '=====================  SwitchPort Finder is installed  =====================' -ForegroundColor Green
    Write-Host "  Open:            $base"
    if ($adminPlain) {
        Write-Host "  Administrator:   $AdminUser"
        Write-Host "  Password:        $adminPlain      <-- shown ONCE, store it now; change it under Settings > My account" -ForegroundColor Yellow
    }
    Write-Host "  Service:         $ServiceName  (virtual account $ServiceAccount, starts automatically)"
    Write-Host "  Logs:            $(Join-Path $DataPath 'logs')   (daily files, 30 kept)"
    Write-Host "  Keys + cert:     $DataPath   <-- BACK THIS UP (scripts\backup.ps1); credentials cannot be decrypted without the keys"
    Write-Host ''
    Write-Host '  Browsers on OTHER PCs will warn about the self-signed certificate until they trust it:'
    Write-Host "    1. copy  $cerPath  to the PC"
    Write-Host '    2. install it into "Trusted Root Certification Authorities" (or distribute it with Group Policy)'
    Write-Host '  Next: add your switches (Switches > + Add Switch, or Settings > Import), tick "Use as ARP source" on the core switch, then Scan all.'
    Write-Host '==========================================================================='
}
catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray }
    exit 1
}
