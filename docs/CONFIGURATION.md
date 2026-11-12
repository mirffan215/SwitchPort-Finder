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

## Two-factor authentication (authenticator app)

With `Authentication:Mode = Local`, **administrators must enrol an authenticator app** (`Authentication:RequireMfaForAdmins`, default `true`; viewers may enrol voluntarily).

* The first time an administrator signs in after the upgrade, the portal shows *Set up two-factor authentication*: confirm your password, scan the QR code with Microsoft Authenticator, Google Authenticator, Authy, 1Password or similar, enter the 6-digit code, and **save the 10 one-time recovery codes** (shown once).
* Sign-in is then two steps: password, then the 6-digit code (or a recovery code). The password alone never creates a session.
* Codes are standard TOTP (RFC 6238: SHA-1, 6 digits, 30 s, ±1 step for clock drift) and each code works **once**. Wrong codes count toward the account lockout (`MaxFailedLogins`, `LockoutSeconds`), and the secret is stored encrypted (Data Protection), never returned by any API.
* **Lost phone?** Use a recovery code, or another administrator opens *Settings → Admin · Users → Reset 2FA* for that user; the user enrols again at next sign-in. If the *only* administrator loses both phone and recovery codes, stop the service and set `MfaEnabled = 0` for that user in the `Users` table (SQL), then sign in and enrol again.
* Turning MFA off is not possible for administrators while `RequireMfaForAdmins` is on. To switch the policy off set `Authentication:RequireMfaForAdmins` to `false` in `appsettings.Production.json`.
* Keep the server's clock correct (Windows Time service / NTP): TOTP depends on it.

## Switch export with credentials (encrypted)

*Settings → Admin · Import Switches → Export switches with credentials (encrypted)…* downloads a `.spfenc` file that contains every switch **with its SNMP communities / SNMPv3 credentials**. It is encrypted with AES-256-GCM using a passphrase you choose (PBKDF2-SHA256, 600,000 iterations); without the passphrase it is unreadable, and tampering is detected. You must re-enter your own password, the action is written to the application log, and only administrators can do it. Switches that use a credential profile are exported with that profile's credentials inline. To restore, choose the `.spfenc` file under *File* in the same tab and enter the passphrase. The plain CSV/Excel exports still never contain credentials.

## Health monitoring, reports and alerts

* **Health poll** (*Settings → General → Health poll interval*, default 5 min, 0 = off): a read-only SNMP poll of every switch for uptime, SNMP round-trip latency, CPU, memory and the interface counters. CPU/memory use the vendor MIB where known (Cisco CISCO-PROCESS-MIB/CISCO-MEMORY-POOL-MIB, Juniper JUNIPER-MIB) and the standard HOST-RESOURCES-MIB otherwise; a switch that does not expose a value simply shows nothing for it (never a made-up 0). A poll that fails is an "unreachable" data point: **downtime** and **availability** are computed from those points (each failed point counts until the next point, at most 1 hour per gap). History is kept for *Keep health and traffic history* days (default 90) and pruned automatically.
* **Reports** (sidebar *Reports*): pick a dataset (switches, ports, devices, switch health history, port traffic history, availability), choose columns or group-and-summarise (average / maximum / minimum / total / count), filters, period, and a chart (table, columns, bars, line, area, donut, gauges, numbers). Clicking a bar, slice or gauge filters the report to it. Start from the built-in templates (uplink utilization trend, CPU and memory trend, availability, downtime, latency, drops and errors, devices by manufacturer, monthly traffic trend…), save your own, and export to **Excel** (data + a *Definition* sheet), **CSV** or **PDF** (chart + table). Viewers can run and export reports; administrators can save them. Queries are checked against a whitelist and never become SQL.
* **Notifications** (*Settings → Admin · Notifications*): e-mail server settings, recipients, a test button, alert rules and the alert/e-mail history.
  * Mail: **STARTTLS (587) or SSL/TLS (465)**, TLS 1.2/1.3 only, certificate verified (tick *Trust the server certificate* only for an internal relay you control), SASL sign-in; the password is stored encrypted and never shown. "None" (plain SMTP) is allowed only without credentials, for an IP-restricted internal relay. The server address may not be loopback, link-local (cloud metadata) or multicast. **OAuth2 sign-in for Microsoft 365 / Google is not built in**: use an SMTP relay or Exchange connector that accepts this server's IP (or an account that is allowed SMTP AUTH).
  * Rules (editable; defaults are created on first start): uplink utilization ≥ 80 % for 10 min, switch unreachable 10 min, CPU ≥ 90 %, memory ≥ 90 %, packet drops ≥ 5/s, switch restarted. More types: any-port utilization, port errors, SNMP latency. Each rule has a limit, a duration, a scope (all switches or one), a reminder interval and "tell me when resolved". An alert opens when the reading stays at/above the limit for the whole duration and closes when it falls below **90 % of the limit** (no flapping). One e-mail is sent per check (every minute) for everything new, still open after the reminder interval, or resolved; a failed send is retried at the next check.
  * Active alerts also appear on the dashboard and in the sidebar for every signed-in user.

## Unknown devices: manual details and traceability

Open a device and choose **Edit details** (or *Devices → + Add device* for one the scans never saw). Enter a name, owner, notes and, to make it traceable, the **switch and port it is plugged into** (uplink ports are not offered). The name is never replaced by reverse DNS and the pinned port survives rescans; the device then appears on that port and *Trace Device* shows the path up to the core. Clearing the fields hands the device back to discovery. Devices added by hand can be deleted; discovered ones cannot (they would reappear).

## Ports page and navigation

*Ports* lists every port of every switch (filter by status, type, switch, VLAN, text). The dashboard's **Connected Ports** and **Switch Uplinks** cards and the port donut open it pre-filtered. Every page except the dashboard has a **Back** button.

## Device status: Online / Offline / Stale

A device's status always reflects the **most recent scan of the switch it sits behind**, never the clock:

* **Online** — the latest scan of its switch found the device's MAC address.
* **Offline** — the latest scan did not find it.
* **Stale** — its switch's scans have not found it for more than *Device stale after (days)* (default 7).

So with scheduled scans off (the default) nothing changes between scans: devices found by your last *Scan Now* stay Online until you scan again. A device that is located nowhere yet (seen only behind uplinks) is compared with the newest scan of any switch.

**Scheduled scans are optional** and set by an administrator under *Settings → General → Scheduled scans*: *Full discovery scan* (off by default; every 15/30/60 min, 2/6/12 h, daily) and *Health poll* (default every 5 min; needed for the reports, utilization gauges and e-mail alerts, off = no history and no alerts). The same page has **Scan all switches now**. Changes apply immediately.

## Access points and device types

Every device has a **type**: access point, IP phone, printer, camera, server, other, or none. Types come from editable rules (*Settings → Admin · Device types*) or are set by hand on a device (*Edit details → Type of device*; a manual type always wins).

* A rule matches when **all** of its filled-in conditions match: manufacturer (from the MAC), host name (starts with / contains), switch port description, what the device announces on its port by LLDP/CDP (name, model, or the "wireless access point"/"telephone" capability) and VLAN. The lowest priority number that matches wins.
* **Default rules** (edit them to fit your network): LLDP says access point or telephone; CDP/LLDP model contains `AIR-` (Cisco access points) or "Access Point" / "IP Phone"; Cisco host name `SEP…` = phone; host name starts with `AP-` or `WAP`; Grandstream = access point; Ruijie in **VLAN 50** = access point. There is deliberately **no rule that makes everything in a VLAN an access point**: Wi-Fi clients are in the same VLAN. Cisco access points and phones share the Cisco manufacturer name with all other Cisco gear, so the neighbor information and the host name decide for them.
* **Neighbor information belongs to one device.** An access point announces itself on its switch port, but several devices can sit on that port (the AP plus the Wi-Fi clients bridged through it). The announcement is attributed only to the device it describes: same chassis MAC (or the same first 5 bytes), same management IP, same host name, or the only device on the port.
* The **Access points** page lists only access points, with switch, port, VLAN and Online/Offline (from the latest scan). **Devices behind** is the number of other devices found on the AP's switch port; click it to list them on the Devices page. The Devices page and search (`access point`, `phone`) filter by type; Reports can group and filter by *Type*; the dashboard shows an access-point card.
* **Alert**: the *Access point offline* rule (Settings → Notifications) e-mails when an access point was not found by the latest scan of its switch. It depends on scans being run (by you or scheduled).
* **Wireless clients** that your Cisco controller tunnels never appear in the switch tables, so they are not listed. Showing them would need the controller to be read by SNMP (not built in yet).

## Reports: choosing switches

In *Reports*, the **Switches** box limits any report to one switch, several, or all (empty = all). It works for every dataset (current ports/devices/switches and the health, traffic and availability history), is saved with the report, is kept when you switch between templates, and is written on the Excel *Definition* sheet.

## Wireless controller (Cisco) and its access points

Access points that join a Cisco wireless controller are not seen by a switch scan as "access points", so the portal reads them from the controller instead.

1. **Switches → Add switch**: enter the controller's management IP and its SNMP read community (or SNMPv3 user), and tick **"This is a Cisco wireless controller"**. Save, then click **Scan**.
2. The scan reads the controller's access-point table (CISCO-LWAPP-AP-MIB, Catalyst 9800 / AireOS 8.x; older AireOS uses AIRESPACE-WIRELESS-MIB). Nothing is written to the controller (SNMP read only).
3. Every access point the controller lists appears on **Access points** automatically, with its name, model, uptime, Wi-Fi client count and **Online / Offline / Disabled** status. When the same MAC is also seen on a switch, the two are merged and the switch and port are shown.
4. An access point the controller stops listing becomes **Offline** after the next scan of the controller. The *Access point offline* alert rule (Admin → Notifications) covers these too.
5. If the scan reports "No access points were returned", the controller's SNMP view probably blocks the wireless tables: allow `1.3.6.1.4.1.9.9.513` (and `1.3.6.1.4.1.14179` on older AireOS).

The switch import/export CSV does not carry the wireless-controller flag; tick it after importing.

## Company name, logo and link (Admin · Company)

Settings → **Admin · Company**: set the company name, an optional website, and upload a logo (PNG, JPEG, GIF or WebP up to 512 KB; SVG is refused because it can carry script). They appear at the top of the menu and on the sign-in page; a click opens the website in a new tab. The logo is stored in the portal's database and served from the portal itself, so nothing is loaded from the internet.

## Optional integrations (Admin · Integrations) — all OFF by default

Each one is switched on by an administrator, is **read-only**, and runs only when you press **Run now** (no scheduled traffic). They fill in what is unknown; a name, type or location set by hand is never overwritten.

| Integration | What it reads | What you need |
|---|---|---|
| **Windows DHCP** | Leases and reservations (IP, MAC, host name, reservation name/description, scope, lease state and expiry) → names devices that show as "Unknown Device", fills a missing IP, and shows the DHCP details on the device page | Windows PowerShell + the DHCP management tools (RSAT) on the portal server; the service account needs read access to the DHCP server (member of *DHCP Users*) |
| **Local domain (Active Directory)** | Computer accounts over **LDAPS** → adds OS and OU to devices whose name matches | Domain controller name, LDAPS port (636), base DN (e.g. `DC=corp,DC=local`), a read-only account (`user@domain`) and its password. Install your domain CA on the server, or tick "trust certificate" |
| **Intune** | Managed devices via Microsoft Graph → matches by MAC; adds name, user, OS, compliance | Entra app registration with the application permission `DeviceManagementManagedDevices.Read.All` (admin consent), tenant ID, client ID and secret; the portal server must reach `login.microsoftonline.com` and `graph.microsoft.com` |

Passwords and secrets are stored encrypted and are never shown again. Intune matches only devices whose Wi-Fi/Ethernet MAC Intune reports; devices that use a randomised (private) Wi-Fi MAC cannot be matched. Run **DHCP first**, then **Active Directory**: the directory match uses the host name.

### Wireless controllers are recognised automatically

Add the controller like any switch (**Switches → + Add Wireless Controller**: IP and SNMP read community). When a scan finds that a device identifies itself as a Cisco wireless controller (AireOS, Catalyst 9800, virtual WLC), the portal flags it as one and reads its access points, even if the box was not ticked. Other vendors' controllers are not read: only the Cisco wireless MIBs are supported.
