# Shared, side-effect-free helpers for install.ps1 (kept separate so they can be unit-tested without Windows).
# Compatible with Windows PowerShell 5.1 and PowerShell 7.

function New-SpfPassword {
    <# Random password from a crypto RNG. Satisfies the portal policy (12+ chars, >= 4 distinct, no username inside). #>
    param([int]$Length = 24)
    $chars = 'abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'.ToCharArray()   # no look-alikes (0/O, 1/l/I)
    $bytes = New-Object byte[] ($Length * 4)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $sb = New-Object System.Text.StringBuilder
    for ($i = 0; $i -lt $Length; $i++) {
        $n = [BitConverter]::ToUInt32($bytes, $i * 4)
        [void]$sb.Append($chars[[int]($n % [uint32]$chars.Length)])
        if (($i + 1) % 6 -eq 0 -and ($i + 1) -lt $Length) { [void]$sb.Append('-') }
    }
    $sb.ToString()
}

function Test-SpfPasswordPolicy {
    param([string]$Password, [string]$UserName)
    if ([string]::IsNullOrEmpty($Password) -or $Password.Length -lt 12) { return 'Password must be at least 12 characters.' }
    if (($Password.ToCharArray() | Select-Object -Unique).Count -lt 4) { return 'Password is too simple.' }
    if ($UserName -and $Password.IndexOf($UserName, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return 'Password must not contain the username.' }
    return $null
}

function Get-SpfSanText {
    <# Value for -TextExtension 2.5.29.17 (subject alternative names): "DNS=a&DNS=b&IPAddress=1.2.3.4". #>
    param([string[]]$DnsNames, [string[]]$IpAddresses)
    $parts = @()
    foreach ($d in ($DnsNames | Where-Object { $_ } | Select-Object -Unique)) { $parts += "DNS=$d" }
    foreach ($i in ($IpAddresses | Where-Object { $_ } | Select-Object -Unique)) { $parts += "IPAddress=$i" }
    $parts -join '&'
}

function New-SpfConfig {
    <# The appsettings.Production.json content. Plain HTTP is bound to loopback ONLY (health checks); the network sees HTTPS only. #>
    param(
        [Parameter(Mandatory)][string]$ConnectionString,
        [Parameter(Mandatory)][string]$KeysPath,
        [Parameter(Mandatory)][string]$LogDirectory,
        [Parameter(Mandatory)][string]$PfxPath,
        [Parameter(Mandatory)][string]$PfxPassword,
        [Parameter(Mandatory)][int]$HttpsPort,
        [Parameter(Mandatory)][string[]]$AllowedHosts,
        [string]$AdminUser = 'admin',
        [string]$AdminPassword,
        [int]$LoopbackHttpPort = 5000
    )
    $auth = [ordered]@{ Mode = 'Local' }
    if ($AdminPassword) { $auth['BootstrapAdmin'] = [ordered]@{ Username = $AdminUser; Password = $AdminPassword } }
    [ordered]@{
        ConnectionStrings = [ordered]@{ Default = $ConnectionString }
        Database          = [ordered]@{ Provider = 'SqlServer'; AutoMigrate = $false }
        Security          = [ordered]@{ KeysPath = $KeysPath; AllowLoopbackTargets = $false; TrustedOrigins = @(); Hsts = $false }
        Authentication    = $auth
        AllowedHosts      = (($AllowedHosts | Where-Object { $_ } | Select-Object -Unique) -join ';')
        Kestrel           = [ordered]@{ Endpoints = [ordered]@{
            Http  = [ordered]@{ Url = "http://127.0.0.1:$LoopbackHttpPort" }
            Https = [ordered]@{ Url = "https://0.0.0.0:$HttpsPort"; Certificate = [ordered]@{ Path = $PfxPath; Password = $PfxPassword } }
        } }
        Serilog           = [ordered]@{ WriteTo = @(
            [ordered]@{ Name = 'File'; Args = [ordered]@{ path = ($LogDirectory.TrimEnd('\') + '\spf-.log'); rollingInterval = 'Day'; retainedFileCountLimit = 30 } }
        ) }
    }
}

function ConvertTo-SpfJson { param($Object) $Object | ConvertTo-Json -Depth 12 }

function Get-SqlServiceName {
    <# Windows service name of a LOCAL SQL Server instance, or $null for a remote server. #>
    param([string]$SqlInstance, [string]$ComputerName)
    $inst = $SqlInstance.Trim()
    $local = @('.', '(local)', 'localhost', $ComputerName)
    $parts = $inst -split '\\', 2
    $server = $parts[0] -replace ',\d+$', ''
    if ($local -notcontains $server -and $server -ne '127.0.0.1') { return $null }
    if ($parts.Count -eq 1 -or $parts[1] -eq 'MSSQLSERVER') { return 'MSSQLSERVER' }
    return 'MSSQL$' + $parts[1]
}

function New-SpfConnectionString {
    param([string]$SqlInstance, [string]$Database)
    "Server=$SqlInstance;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True"
}
