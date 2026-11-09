# Configuration

Configuration comes from `appsettings.json`, then `appsettings.{Environment}.json`, then **environment variables** (`Section__Key`, e.g. `Snmp__TimeoutMs`). Environment variables win. Restart after changing files.

Thirteen discovery settings can also be changed **at runtime** under **Settings → General** (stored in the database, applied immediately, no restart); they are marked *(runtime)* below. A value saved in the UI overrides the file.

## Hosting

| Key | Default | Meaning |
|---|---|---|
| `Kestrel:Endpoints:Http:Url` | `http://0.0.0.0:5000` | Listen address. Plain HTTP in V1. |
| `AllowedHosts` | `*` | Restrict to your portal host names if you like (e.g. `portal.corp.example`). |

### Enabling HTTPS later
No code changes are needed; add an `Https` endpoint (and optionally keep or remove `Http`):
```json
"Kestrel": { "Endpoints": {
  "Https": { "Url": "https://0.0.0.0:5001", "Certificate": { "Path": "C:\\certs\\portal.pfx", "Password": "..." } } } }
```
Then call `app.UseHttpsRedirection()` / `UseHsts()` in `Program.cs` if you want redirects. HTTPS is recommended when the portal is moved beyond a trusted internal network. (SNMPv2c itself is clear-text on the wire; prefer SNMPv3 where possible.)

## Database

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Default` | LocalHost, Windows auth | SQL Server connection string. |
| `Database:Provider` | `SqlServer` | `SqlServer` for production. `Sqlite` exists **only** for tests and sandboxes. |
| `Database:AutoMigrate` | `false` (`true` in Development) | Apply EF migrations at startup. |

## Credential protection and security

| Key | Default | Meaning |
|---|---|---|
| `Security:KeysPath` | `keys` (beside the app) | Folder for Data Protection keys. On Windows the keys are additionally wrapped with **machine-scope DPAPI**. Back this folder up; see SECURITY.md. |
| `Security:AllowLoopbackTargets` | `false` (`true` in Development) | Loopback addresses are never real switches; blocked unless enabled. `0.0.0.0`, broadcast and multicast are always blocked. |
| `Security:TrustedOrigins` | `[]` | Extra origins allowed to make state-changing requests (reverse proxies that rewrite `Host`). |
| `Security:SnmpTestsPerMinute` | `30` | Rate limit per client IP for the *Test Connection* endpoints. |

## SNMP

| Key | Default | Meaning |
|---|---|---|
| `Snmp:TimeoutMs` *(runtime)* | `5000` | Per-request timeout (500–60000). |
| `Snmp:Retries` *(runtime)* | `1` | Retries after a timeout (0–5). |
| `Snmp:MaxRepetitions` *(runtime)* | `10` | GETBULK max-repetitions (1–50). GETNEXT is the automatic fallback. |
| `Snmp:Port` | `161` | UDP port on the switches. |

## Discovery

| Key | Default | Meaning |
|---|---|---|
| `Discovery:MaxParallelScans` *(runtime)* | `5` | Switches scanned at the same time. Changes apply to the next job. |
| `Discovery:ScanIntervalMinutes` *(runtime)* | `0` (off) | Scheduled "scan all"; `0` or ≥ 5. Changes apply without a restart. |
| `Discovery:OfflineAfterMinutes` *(runtime)* | `60` | Not seen for longer → **Offline**. |
| `Discovery:StaleAfterDays` *(runtime)* | `7` | Not seen for longer → **Stale**. Devices never vanish after one missed scan. |
| `Discovery:PerVlanTimeoutMs` *(runtime)* | `1500` | Cisco per-VLAN MAC queries use short timeouts. |
| `Discovery:PerVlanParallelism` *(runtime)* | `4` | Simultaneous VLAN queries per switch. |
| `Discovery:DnsTimeoutMs` *(runtime)* | `2000` | Reverse-DNS timeout. DNS never fails a scan. |
| `Discovery:HostnameRefreshHours` *(runtime)* | `24` | Retry hostnames that could not be resolved. |
| `Discovery:UplinkHeuristicMinMacs` *(runtime)* | `3` | FDB heuristic (used only when LLDP/CDP are unavailable): minimum MACs on a port. |
| `Discovery:UplinkHeuristicRatio` *(runtime)* | `0.6` | FDB heuristic: share of a port's MACs that must also be edge MACs elsewhere. Raise it if ports are wrongly shown as uplinks. |
| `Discovery:SchedulePollSeconds` | `30` | How often the scheduler checks whether a scan is due. |

## Import / export

| Key | Default | Meaning |
|---|---|---|
| `Import:MaxRows` | `10000` | Rows per import file. |
| `Import:MaxFileBytes` | `10485760` | Upload size limit (10 MB). Excel files that unpack to more than 200 MB are rejected. |
| `Import:PreviewTtlMinutes` | `30` | How long a previewed import can be committed. |
| `Export:MaxDeviceRows` | `250000` | Largest device export in one request. |

## Logging

Serilog, configured under `Serilog` (levels) and writing to the console by default. Structured events: `DiscoveryStarted`, `DiscoveryCompleted`, `SwitchUnreachable`, `SnmpAuthenticationFailed`, `OidUnavailable`, `NeighborDetected`, `DeviceDiscovered`. **Credentials are never logged**; a test fails the build if one appears in logs. Add a file sink by referencing `Serilog.Sinks.File` and adding `"WriteTo"` entries.

## Example `appsettings.Production.json`

```json
{
  "ConnectionStrings": { "Default": "Server=SQLHOST;Database=SwitchPortFinder;Trusted_Connection=True;TrustServerCertificate=True" },
  "Database": { "AutoMigrate": false },
  "Security": { "KeysPath": "D:\\SwitchPortFinderKeys", "TrustedOrigins": [ "https://portal.corp.example" ] },
  "Discovery": { "MaxParallelScans": 8 },
  "AllowedHosts": "portal.corp.example;localhost"
}
```
