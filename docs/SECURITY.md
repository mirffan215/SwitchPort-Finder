# Security notes

## Design stance (V1)
* **Read-only against the network.** `ISnmpClient` exposes only `GetAsync`, `GetNextAsync` and `WalkAsync`; no SET, trap or inform code exists in the product, and a test scans the source for it.
* **The browser never talks to switches.** All SNMP runs on the server.
* **No login and no TLS by default** (as specified for V1). **Optional local accounts** can be switched on with `Authentication:Mode=Local` (below). Run it only on a trusted internal network, and consider HTTPS (CONFIGURATION.md) when exposing it more widely.

## Authentication (optional, off by default)
`Authentication:Mode=Local` turns on a login page and two roles; the default `None` keeps V1's open behaviour.
* **Roles.** *Admin*: everything. *Viewer*: search, browse, trace, export; **cannot** add/edit/scan/delete, manage users, change settings, or read credential profiles. Enforced **on the server** (every endpoint requires a login; every state-changing call and every admin-only endpoint requires Admin); the UI merely hides what a user cannot do.
* **Passwords.** PBKDF2-HMAC-SHA256, 210,000 iterations, random salt, constant-time compare; policy: 12+ characters, not the username, not trivially repetitive. Only the hash is stored (a test inspects the database for clear-text passwords).
* **Sessions.** Cookie `spf.session`: `HttpOnly`, `SameSite=Strict`, `Secure` automatically over HTTPS, session (non-persistent) cookie, 8 h sliding by default. The account is re-checked on every request (cached 15 s), so disabling, deleting or demoting a user ends their session.
* **Brute force.** Per-account lock after 5 failures for 15 minutes (the correct password is refused while locked), plus a per-IP limit on the login endpoint. Unknown user and wrong password return the same message and cost the same hashing time.
* **Safeguards.** The last active administrator cannot be demoted, disabled or deleted. The first administrator comes from `Authentication:BootstrapAdmin` (used only when no user exists; remove it from configuration afterwards). Login/lock/user-change events are logged without passwords.
* Not implemented: Windows/AD integrated authentication, SSO, MFA, per-switch permissions.

## Credentials
* Stored encrypted (ASP.NET Core Data Protection; keys in `Security:KeysPath`, wrapped with machine-scope DPAPI on Windows). **Back up the keys folder** and protect it with NTFS permissions (service account only). Keys wrapped by machine DPAPI **cannot be restored on a different machine**.
* Never returned by any API response, written to logs, put in URLs, or included in error messages. Edit forms show "unchanged" placeholders and only overwrite what you type.
* Enforced by tests: API responses, log output **and the database file** are scanned for known secrets (and the test fails if a log line leaks one).
* Import files may contain secrets: processed in memory only, never written to disk; use credential profiles.

## Controls that exist
| Risk | Control |
|---|---|
| A web page in a user's browser calling the portal (CSRF) | State-changing requests carrying an `Origin` header must match the portal's own origin or `Security:TrustedOrigins`; there is **no CORS**, so other sites cannot read responses. |
| Clickjacking / sniffing / referrer leaks | `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`; strict CSP (`default-src 'self'`, `frame-ancestors 'none'`) in production; `Cache-Control: no-store` for API responses. |
| Portal used to probe arbitrary hosts via SNMP | Targets that can never be switches are refused (loopback unless allowed, `0.0.0.0`, broadcast, multicast); *Test Connection* is rate-limited. |
| SQL injection | EF Core parameters everywhere; user text in `LIKE` has wildcards escaped; covered by tests with `%`, `_`, `[`, `' OR 1=1 --`. |
| Spreadsheet formula injection | CSV cells starting with `= + @ -` (non-numeric) are prefixed with `'`; Excel cells are written as text. |
| Zip bombs / oversized uploads | 10 MB upload cap, 10,000-row cap, `.xlsx` unpacked-size and entry-count checks before parsing. |
| Information disclosure | Problem-details responses contain no stack traces, SQL or .NET type names; unknown `/api/*` paths return JSON 404; Swagger is Development-only. |
| API robustness | The whole OpenAPI surface (39 operations) was fuzzed with **schemathesis** (thousands of malformed/edge-case requests, anonymous and authenticated, in both auth modes): it found 3 unhandled 500s, all fixed with regression tests; the final runs show **zero** server errors and zero unhandled exceptions. |
| Vulnerable dependencies | `dotnet list package --vulnerable` and `npm audit` report **no known vulnerabilities** at the time of release (re-run them before each deployment). |
| Hard-coded networks / external dependencies | Tests fail if production code contains private IP literals, SNMP write calls, or if the frontend references external URLs. |

## Residual risks / not done
* With authentication **off** (default), anyone who can reach the port can view the network and change switches/credentials. Turn on Local mode and/or restrict network access.
* SNMPv2c community strings are clear text on the wire (prefer SNMPv3 authPriv). Plain HTTP exposes the portal's own traffic and, in Local mode, the login and session cookie (the cookie becomes `Secure` over HTTPS) — **enable HTTPS whenever authentication is on**.
* The `Origin` check protects against browser-borne attacks, not against other clients on the network (nor is it meant to).
* **No external, human penetration test has been performed** (not possible in this development environment). `docs/PENTEST.md` lists what was tested automatically and a recommended scope for an external test.
