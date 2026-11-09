# Troubleshooting

Start with **Discovery → click the job**: every step shows ✓ / ⚠ / ✗ with the reason, and the *Errors and warnings* list gives the details. Nothing in these messages contains credentials.

## SNMP / connectivity

| Symptom | Likely cause and fix |
|---|---|
| **"No response from the switch (timeout)"** | Wrong IP; SNMP not enabled; ACL/firewall blocks **UDP 161** from the portal server; or — for SNMPv2c — a **wrong community string** (a bad community is *silently dropped* by switches, so it looks like a timeout). Test from the server: `snmpwalk -v2c -c <community> <ip> 1.3.6.1.2.1.1`. |
| **"SNMPv3 authentication failed"** | Wrong username/auth protocol/password. Protocol must match the switch (SHA ≠ SHA256). |
| **"SNMPv3 privacy (decryption) failed"** | Wrong privacy protocol/password (AES-128 vs AES-256, DES). |
| **"does not recognise this SNMPv3 username"** | The user is missing on the switch, or the engine ID changed (re-create the user). |
| **"security level"** | The group requires auth+priv but the credential is noAuth/auth-only (or vice-versa). |
| *Test Connection* works but scans are slow/time out | Large tables on slow CPUs: raise **SNMP timeout** and lower **GETBULK max-repetitions** (Settings → General). Lower parallelism if many switches share a slow path. |
| "Loopback addresses cannot be switches" | Use the switch's real management IP (loopback is allowed only in Development). |
| 429 "Too Many Requests" on Test Connection | The test endpoints are rate-limited (`Security:SnmpTestsPerMinute`, default 30/min). Wait a minute. |

## Missing data

| Symptom | Cause / fix |
|---|---|
| **MAC table empty on a Cisco switch** | The FDB is per VLAN. v2c: nothing to configure (portal uses `community@vlan`); check the community works with `@<vlan>`. v3: allow contexts `vlan-<id>` (see SNMP-SETUP.md). Check the job shows the VLAN step succeeded. |
| MAC table empty on another vendor | The switch may expose only BRIDGE-MIB per VLAN, or restrict the view. Make sure the view includes `1.3.6.1.2.1.17`. |
| **IPs show "Unknown"** | Access switches have no ARP for user VLANs. Tick **Use as ARP source** on the core/Layer 3 switch and **scan it**. The hint on the switch page says the same. |
| IP is stale/wrong | The portal uses the **newest ARP observation** from any source switch. Rescan the ARP source; check ARP aging. |
| **"Unknown Device"** | Reverse DNS has no name (or DNS is slow/unreachable). The portal never invents hostnames. Check the PTR record; a DNS failure never fails a scan. |
| "LLDP unavailable. Continuing with MAC and interface discovery…" | LLDP is off or the LLDP-MIB is not in the SNMP view. Enable LLDP (SNMP-SETUP.md). Uplinks are then inferred from the MAC table ("Likely"). |
| "CDP unavailable" (Cisco) | CDP disabled or its MIB not in the view; harmless if LLDP works. |
| "N interface(s) were returned without an operational status" | The switch's reply was incomplete (overloaded device, UDP loss). Previous status is kept; **rescan**. |
| Device has **no switch/port** ("not determined yet") | It was only ever learned behind a **switch uplink**. Scan (or add) the switch it is really plugged into; the location appears after that scan. |
| Switch page shows no devices on an uplink | By design: devices are shown only on the **edge port** where they connect. |
| A port is wrongly shown as an **uplink** (Likely) | Only the MAC-table heuristic applies when LLDP/CDP are off. Enable LLDP (preferred), or raise *Uplink heuristic: overlap ratio* / *minimum MACs* in Settings. |
| A real uplink is shown as Endpoint | Scan **both** switches (the heuristic needs both MAC tables) or enable LLDP. |
| Trace says "Path partially determined" | A hop between the root and the device is not proven (an LLDP/CDP link is missing, or a switch is not registered). The message names the missing link. |

## Install / run

| Symptom | Fix |
|---|---|
| App won't start: cannot connect to SQL Server | Check `ConnectionStrings:Default`, that SQL Server accepts TCP, the service account's rights, and `TrustServerCertificate=True` for self-signed certs. |
| "Invalid object name" / missing tables | Migrations not applied (INSTALLATION.md §5). |
| **"The key was not found in the key ring" / switches show credential errors after moving the server** | Credentials are encrypted with keys in `Security:KeysPath` (and machine-scope DPAPI on Windows). A new machine, a deleted `keys` folder or a different service identity cannot read them. Restore the original keys on the same machine, or **re-enter the credentials** (re-import with the credential columns, or edit the switches/profiles). |
| Port 5000 already in use | Change `Kestrel:Endpoints:Http:Url`. |
| 403 "Cross-origin request blocked" behind a reverse proxy | The proxy changes `Host`. Send `X-Forwarded-Host` or add the public origin to `Security:TrustedOrigins`. |
| UI loads but API calls fail in dev | Start the API on :5000 (the Vite dev server proxies `/api`). |
| Jobs stuck "Queued" after a crash | On start the worker fails jobs that were Running and re-queues Queued ones. |
| Excel/CSV import: "Missing required column(s)" | Use the template header row; `Name` and `ManagementIP` are required. |

## Sign-in (only when `Authentication:Mode=Local`)

| Symptom | Fix |
|---|---|
| Login page but no account exists | Set `Authentication:BootstrapAdmin:Password` (12+ characters, not containing the username) and restart; check the log for "bootstrap administrator". If the password fails the policy the account is not created. |
| "Too many failed sign-in attempts" | The account is locked for `Authentication:LockoutSeconds` (15 min default). Wait, or an administrator can reset the password under *Settings → Users*. |
| Signed out unexpectedly | Your account was disabled/deleted/demoted, the session expired (`SessionMinutes`), or the server was restarted with a different `Security:KeysPath` (cookies are protected with the same keys). |
| "Your account is read-only" (403) | You are a Viewer. Ask an administrator for the Admin role. |
| Forgot the only administrator password | See INSTALLATION.md §11 (`DELETE FROM Users;` and restart with `BootstrapAdmin`). |
| Login works on HTTP but the cookie is dropped behind a proxy | Make sure the proxy forwards cookies and `X-Forwarded-Host`/`Host` consistently, and add your public origin to `Security:TrustedOrigins`. |

## Logs

Look for the structured events: `DiscoveryStarted`, `DiscoveryCompleted`, `SwitchUnreachable`, `SnmpAuthenticationFailed`, `OidUnavailable`, `NeighborDetected`, `DeviceDiscovered`. Set `Serilog:MinimumLevel:Default` to `Debug` for per-VLAN detail. Logs never contain SNMP credentials.
