# Test report — production readiness (security, penetration test and QA)

Scope: the application at the commit that contains this file, tested as it is deployed in production — Release build, `ASPNETCORE_ENVIRONMENT=Production`,
**HTTPS with a PFX certificate**, **Local authentication**, and **Microsoft SQL Server 2022** (real server in Docker, real EF migrations).

This is a thorough internal test with repeatable tooling. It is **not a substitute for an independent third-party penetration test** — see "What was NOT tested".

## Result summary

| Area | Tool | Result |
|---|---|---|
| Unit tests | `dotnet test` | **345 passed**, 0 failed |
| Integration tests (SQLite) | `dotnet test` | **105 passed**, 0 failed |
| Integration tests on **real SQL Server 2022** | `tools/sqlserver/run-sqlserver-tests.sh` | **101 passed**, 0 failed (performance tests excluded; migrations Phase1-Phase11 applied) |
| Black-box penetration test | `tools/security/pentest.py` (HTTPS + SQL Server) | **94 checks passed**, 0 failed |
| Browser QA + accessibility (WCAG 2.1 AA, dark and light) | `tools/qa/ui-qa.js` (HTTPS + SQL Server) | **47 checks passed**, 0 failed |
| Load | `tools/security/loadtest.py`, 30 concurrent clients | 2,700 requests, ~800 req/s, **0 server errors**, p95 < 70 ms |
| Dependency audit | `dotnet list package --vulnerable --include-transitive`, `npm audit` | **0 vulnerable packages** |
| Secrets scan of tracked files | pattern search | none found |
| Application log after all of the above | grep for `[ERR]`/`[FTL]`, passwords, SNMP communities | no errors, **no secrets logged** |

## One combined update

Everything in this report (session revocation, search fix, CSP/fonts, traffic gauges, manufacturer column, switch-uptime gauges) is in the same release. **One `publish.ps1` + `install.ps1 -KeepConfig` applies all of it**; the installer runs all pending database migrations (`Phase8PortCounters`, `Phase9SessionVersion`, `Phase10DeviceVendor`, `Phase11SwitchUptime`) in order. The final penetration test (94 checks) and browser QA (47 checks) were re-run on this exact build.

## Update: authenticator MFA, reports, health history, notifications, manual devices, encrypted export

These features were added and tested in the same way (unit tests, integration tests including a **real in-process SMTP server**, the browser QA, and the penetration suite):

* **MFA:** TOTP verified against the RFC 6238 test vectors; tests cover forced enrolment for administrators, no session before the second factor, tampered/expired challenge, code replay, recovery codes (single use, stored hashed), lockout on wrong codes, admin reset, and that the secret is never returned.
* **Encrypted credential export:** round trip, wrong passphrase / flipped bit / truncated file all refused identically, secret never in clear in the file, password re-entry, admin only.
* **Reports:** availability/downtime maths on a scripted outage, grouping/time buckets/filters/limits, hostile queries rejected, Excel formula neutralisation, viewers may run but not save.
* **Alerts:** sustained-threshold and hysteresis logic, alert opened once, mailed once, resolved and mailed, failed mail retried, mail credentials never over an unencrypted connection, mail server address policy.
* **Found and fixed while testing:** the target policy did not refuse link-local addresses (`169.254.169.254`, cloud metadata). It was not exploitable for SNMP (UDP) but would have mattered for the new mail connection; it is now refused for both.

## Defects found by this testing — all fixed and covered by regression tests

| # | Severity | Finding | Fix | Regression test |
|---|---|---|---|---|
| 1 | **High** | **A logged-out session cookie could still be replayed.** Logout only deleted the cookie in the browser; a copied cookie stayed valid until it expired. The same applied after a password change or reset. | Each user has a `SessionVersion` stored in the cookie and checked on every request. Logout, password change/reset and role change bump it, so older cookies are refused. Changing your own password keeps the current browser signed in. Migration `Phase9SessionVersion`. | `AuthTests`: logout replay, password-change revocation, admin reset |
| 2 | Medium | **HTTP 500 on SQL Server for a very long search text** (`String or binary data would be truncated`). SQLite hid it. | Search text is capped at 100 characters (no host, IP, MAC or port name is longer). | `HostileInputTests` (fails without the fix, passes with it, verified on SQL Server) |
| 3 | Medium | **Fonts blocked by the production Content-Security-Policy.** The build inlined small font files as `data:` URIs, which `default-src 'self'` forbids, so part of the font failed to load on every page. | Vite `assetsInlineLimit: 0` — fonts are served as files. | `ui-qa.js` (console must be free of errors on every route) |
| 4 | Medium | **Blank switch page** while a switch's ports were still loading (found by the user). | Loading state + a global error boundary, so a crash shows a message instead of a blank page. | `ui-qa.js` route checks |
| 5 | Low | `Server: Kestrel` response header disclosed the web server. | `AddServerHeader = false`. | `pentest.py` |
| 6 | Low | Phone layout scrolled sideways (page 367 px too wide); Settings tabs overflowed. | Grid `minmax(0, 1fr)`, scrollable tab row. | `ui-qa.js` (375 px and 768 px widths) |
| 7 | Low | Links inside text were distinguishable by colour only (WCAG 1.4.1). | Underlined. | axe-core on 5 pages × 2 themes |
| 8 | Low | `tsconfig.tsbuildinfo` was tracked in git and blocked `git pull` after every build. | Untracked and ignored. | — |

Earlier in the project (already fixed): fuzzing found 500s in change-password/logout with auth off and in malformed multipart field names; one vendor mapping (`4413`) was wrong; the
Windows service ignored `appsettings.Production.json` (working-directory bug).

## What the penetration test covered (`tools/security/pentest.py`)

Run it yourself against your own server — it creates and deletes its own `pt-` users and switches:

```
python tools/security/pentest.py --base https://<server>:8450 --admin admin --password "<admin password>"
```

* **Authentication required:** 35 API endpoint/method combinations return 401 anonymously; only `/api/health` is public and returns no sensitive data.
* **Login hardening:** identical response for unknown user vs wrong password (no user enumeration); account lockout (also with the *correct* password while locked); per-IP rate limiter returns 429 under password spraying; no hash or password in any response.
* **Session:** cookie is `HttpOnly`, `SameSite=Strict`, `Secure` over HTTPS; logout/password change/reset/disable all kill existing sessions; forged cookies rejected.
* **Authorization (RBAC):** a Viewer gets **403** on every write/admin endpoint (15 calls, with a verified-valid Viewer session so the 403s are real), can still read; cannot promote itself; cannot read users, settings, credential profiles or exports; the last administrator cannot be deleted or demoted.
* **CSRF:** state-changing requests with a foreign `Origin` (`evil.example`, `null`, look-alike hosts) are rejected; cross-site form POSTs rejected; `text/plain` bodies rejected; no CORS headers are ever sent.
* **Injection:** SQL, XSS, template, NUL, path-traversal and Unicode payloads in search/filter/sort/paging never cause a 5xx or leak SQL/stack text; hostile JSON, oversized (35 MB) and malformed bodies are refused with clean 4xx.
* **SSRF / target policy:** loopback, IPv6 loopback, `0.0.0.0`, cloud-metadata `169.254.169.254`, broadcast, multicast, `localhost`, IPv4-mapped, decimal/hex/octal/short-form loopback addresses are all blocked for SNMP tests and cannot be saved as a management IP.
* **Secret handling:** an SNMP community is never returned by any endpoint (create, list, get, ports, export, dashboard, topology, search, jobs, profiles, users); user list exposes no hash/salt; nothing sensitive in the application log.
* **Headers and disclosure:** strict CSP (self only, no `eval`, no inline scripts, no framing), `nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Cache-Control: no-store` on the API, HSTS (when enabled), no `Server`/`X-Powered-By`; JSON errors without stack traces; `appsettings*.json`, keys, DLLs, `.git`, `.env`, Swagger and traversal attempts do not expose anything; `TRACE`/`TRACK`/`CONNECT`/`PATCH` are not served.
* **Export:** CSV export neutralises spreadsheet formulas (`=`, `+`, `-`, `@`).

## What the QA run covered (`tools/qa/ui-qa.js`)

Login (wrong and right password), nothing rendered before login; **13 routes** including unknown IDs and unknown URLs render without a blank page or JavaScript error;
theme default (dark), persistence across reload and **Auto** following the operating system; add a switch through the UI; stored-XSS payload in a switch name does not execute on the list or dashboard;
the SNMP community never appears in page HTML; search box survives SQL and script text; no horizontal scrolling at phone (375 px) and tablet (768 px) widths on 4 pages;
**axe-core WCAG 2.1 A/AA** on 5 pages in dark and light themes (no serious/critical issues); keyboard focus; sign-out returns to the login form and the Back button does not reveal protected pages.

## What was NOT tested — do these before relying on it

1. **The Windows installer's Windows-only steps** (certificate creation and trust, `sc.exe` service and virtual account, `icacls`, firewall rule). They were reviewed and the PowerShell was parsed, but they were not executed in this Linux environment. You ran the installer on your server and the application came up, which is the best evidence so far; re-run `tools/security/pentest.py` against that server (it is the same code path you will use).
2. **Real switches.** SNMP behaviour is verified against scripted and `snmpd`-emulated responses and vendor MIB definitions, not against physical Cisco/Aruba/Juniper/etc. hardware. Scan one switch of each model and compare with the switch's own CLI before trusting the port/uplink classification and the new utilization/drop figures (which are *averages between two scans*, and approximate on switches that only have 32-bit counters).
3. **SNMPv3 and ARP/FDB quirks on production firmware** beyond what the lab covered.
4. **An independent penetration test**, volumetric/distributed denial-of-service, slow-connection (slowloris) attacks and TLS configuration scanning of your final deployment — put the server behind your normal network controls.
5. **Active Directory / multi-server / high-availability setups.** The application is designed as a single instance (in-memory session cache and rate limiter).

## Residual risks and design limits (accepted, know them)

* **SNMP v1/v2c are unencrypted on the network** (protocol limitation) — use SNMPv3 where the switches support it and restrict the SNMP source address on each switch to this server.
* **Self-signed certificate:** browsers warn until the `.cer` is installed on clients; HSTS is off by default for that reason. Use a certificate from your internal CA when you can, then set `Security:Hsts` to `true`.
* **Local accounts only, no MFA.** Use long passphrases (12+ characters enforced); give Viewer accounts to most people. Anyone who can sign in can see the whole network inventory.
* **The login rate limiter is per client IP.** Behind a reverse proxy all users share one IP unless forwarded headers are configured.
* **`style-src 'unsafe-inline'`** is allowed because the UI sets some inline styles; scripts are still `'self'` only.
* **Credential encryption keys** (`Security:KeysPath`) are machine-scoped. Back them up with `backup.ps1`; without them stored SNMP credentials cannot be decrypted after a restore.
* **Logout ends all of that user's sessions** (every browser), by design of the session-version scheme.
* The application's own logging is the audit trail (logins, user changes, scans). It is not tamper-proof; forward the log folder to your log system if you need that.

## Go-live checklist

- [ ] Installed with `-KeepConfig`; **`Authentication:Mode` is `Local`** and the bootstrap password was removed from `appsettings.Production.json` (the installer does this).
- [ ] The first administrator's password was changed and stored in your password manager; create named accounts, make most of them Viewers.
- [ ] Browse to `https://<machine-name>:<port>` from a client; install the `.cer` (or use your CA's certificate) so there is no warning.
- [ ] Windows Firewall: the port is open only to the networks that need it (the installer uses the Domain and Private profiles).
- [ ] SQL Server: the service account has only `db_datareader` + `db_datawriter` (the installer does this; no sysadmin, no `sa` in the connection string).
- [ ] Run `backup.ps1` once, **copy the backup off the server**, and test a restore on another machine.
- [ ] Run `tools/security/pentest.py` and `tools/qa/ui-qa.js` against the real server (see top of each file) — expect 0 FAIL.
- [ ] Scan each switch model once, compare ports/uplinks/VLANs with the switch CLI, scan again and check the utilization numbers against the switch's own counters.
- [ ] Windows updates / .NET 8 runtime patching and a review of `dotnet list package --vulnerable` every few months (a monthly scheduled task is enough).
