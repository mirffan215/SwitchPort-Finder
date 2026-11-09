# Security notes

## Design stance (V1)
* **Read-only against the network.** `ISnmpClient` exposes only `GetAsync`, `GetNextAsync` and `WalkAsync`; no SET, trap or inform code exists in the product, and a test scans the source for it.
* **The browser never talks to switches.** All SNMP runs on the server.
* **No login and no TLS in V1** (as specified). `IUserAuthorizationService` and an `AdminOnly` policy are wired in so authentication can be added without rewrites. **Run it only on a trusted internal network, and consider HTTPS (CONFIGURATION.md) and a reverse proxy with authentication when exposing it more widely.**

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
| Vulnerable dependencies | `dotnet list package --vulnerable` and `npm audit` report **no known vulnerabilities** at the time of release (re-run them before each deployment). |
| Hard-coded networks / external dependencies | Tests fail if production code contains private IP literals, SNMP write calls, or if the frontend references external URLs. |

## Residual risks / not done
* No authentication or authorization in V1; anyone who can reach the port can view the network and change switches/credentials in the portal.
* SNMPv2c community strings are clear text on the wire (prefer SNMPv3 authPriv). Plain HTTP exposes the portal's own traffic.
* The `Origin` check does not protect against non-browser clients on the network (nor is it meant to).
* A full external penetration test has **not** been performed.
