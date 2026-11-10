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

## Authentication (optional)

| Key | Default | Meaning |
|---|---|---|
| `Authentication:Mode` | `None` | `None` = no login (V1 default). `Local` = login page, accounts in the database, roles Admin/Viewer. |
| `Authentication:BootstrapAdmin:Username` / `:Password` | `admin` / *(unset)* | Creates the first administrator **only when Local mode is on and no user exists**. The password must pass the policy (12+ characters, must not contain the username). Remove it from configuration after the first sign-in. |
| `Authentication:SessionMinutes` | `480` | Sliding session lifetime. |
| `Authentication:MaxFailedLogins` / `:LockoutSeconds` | `5` / `900` | Lock an account after this many wrong passwords. |
| `Authentication:LoginsPerMinute` | `10` | Per-IP rate limit on the login endpoint. |

Enable it over **HTTPS** whenever possible (the session cookie becomes `Secure` automatically). Manage users under *Settings → Admin → Users*.

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

## Running the tests on SQL Server

`dotnet test` uses SQLite and a scripted SNMP client by default. To run the integration suite (and the load test) on a real SQL Server through the real migrations:
`tools/sqlserver/run-sqlserver-tests.sh [--perf]` (starts SQL Server in Docker), or set `SPF_TEST_SQLSERVER` to a connection string *without* a `Database=` and run `dotnet test`.
Each test fixture creates and drops its own database.

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

## Traffic charts (port utilization and packet drops)

The dashboard's **Top 5 port utilization** gauges and **Top 5 packet-drop ports** chart, and the switch page's **Graphical** view, use the IF-MIB counters
(`ifHCInOctets`/`ifHCOutOctets`, falling back to 32-bit `ifInOctets`/`ifOutOctets`, plus `ifIn/OutDiscards` and `ifIn/OutErrors`). They are read in the same
read-only walk as the interface table, so discovery sends no extra SNMP requests.

- A rate is the **average between the last two scans**, so it needs two scans of a switch and its resolution is the scan interval (`scanIntervalMinutes`). Short bursts are averaged out.
- Utilization is the busier direction as a percentage of the link speed.
- Rates are discarded (and shown as "no data") after a switch reboot, a 64-bit counter reset, or if the scans are less than 10 seconds or more than 7 days apart.
- 32-bit counters wrap every ~34 s at 1 Gbit/s, so on switches without 64-bit counters rates are approximate and are rejected when they exceed the link speed. Use switches/firmware that expose `ifXTable` HC counters for accurate values.
- The first deployment of this feature applies database migration `Phase8PortCounters` (adds columns to `SwitchPorts`); run the installer with `-KeepConfig` to apply it.

## Manufacturer / "Product" column and search

The **Product / Manufacturer** column comes from the device's MAC address (first 3 bytes = OUI) looked up in the IEEE registry (~35,000 manufacturers) that is **embedded in the application**, so it works offline.

* It names the maker of the network adapter (Dell, Cisco, Apple, HP, Hikvision, ...), **not the exact product model** — a MAC address cannot tell that.
* **"Private (randomized MAC)"**: modern phones and laptops use a random MAC per Wi-Fi network for privacy (the second hex digit is 2, 6, A or E). Their manufacturer cannot be known from the MAC.
* **"Unknown"**: the prefix is not in the registry (for example very new or small-vendor ranges).
* Existing devices are filled automatically a few seconds after the upgraded service starts; new devices get it at their first scan. The Excel/CSV export has a **Manufacturer** column at the end.
* The registry is a snapshot. To refresh it, regenerate `src/SwitchPortFinder.Domain/Data/oui.tsv` from the IEEE `oui.txt` (format: `AABBCC<TAB>Vendor name`) and rebuild.

**Global search** (top bar) finds devices by hostname, IP, MAC (any format, partial), manufacturer, VLAN number (`50` or `vlan 50`), switch name/IP and port name; switches by name, IP, sysName, location, model, serial number and notes; ports by name, description and VLAN.

## Why a device shows "Unknown Device" and no switch/port

* **Hostname "Unknown Device"**: the name is looked up by reverse DNS (PTR record) of the device's IP using the server's DNS. If your DNS has no PTR record for that IP (common for phones, printers and DHCP clients), no name can be found. Check with `nslookup 10.10.8.49` on the server; add PTR records or enable DHCP-DNS registration to get names. Product/manufacturer is shown regardless.
* **Switch/Port "—"**: the device's MAC was learned only on switch **uplink** ports so far. The portal places a device only on an edge port of a switch it has scanned. Add the access switches that these devices connect to (and scan them) and the location appears.

## Switch uptime gauges

The dashboard's **Switch uptime** panel shows up to 5 switches: core (routing / ARP-source) switches first, then the most recently restarted. Uptime is the switch's SNMP `sysUpTime`, read during each scan, plus the time since the scan while the switch is reachable (an unreachable switch shows "n/a"). Red = restarted within a day, amber = within a week; the gauge is full at 365 days. `sysUpTime` is a 32-bit counter that wraps after about 497 days, so switches that have been up longer than that read low.
