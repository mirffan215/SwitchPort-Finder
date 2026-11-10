# SwitchPort Finder

Internal web portal that answers **"Which switch and which port is this device connected to?"**
**Read-only against network equipment** (SNMP GET / GETNEXT / GETBULK / walk only; there is no SET anywhere in the code).

**Status: V1 feature-complete and hardened.** Add or bulk-import switches · test SNMPv2c/v3 · background discovery of ports, VLANs, MAC tables
(incl. Cisco per-VLAN), ARP and hostnames · LLDP/CDP neighbors with uplink classification and the edge-port rule · global search · device details ·
trace · topology graph · dashboard · discovery history · CSV/Excel export · runtime settings · optional local accounts (Admin/Viewer). See [docs/V1-CHECKLIST.md](docs/V1-CHECKLIST.md) for the
evidence behind each item. It has been tested on **real SQL Server 2022** and over real SNMP against test agents; what could **not** be checked is physical vendor hardware.

* One ASP.NET Core process (Kestrel) serves the API **and** the built React UI. No desktop client, no CDN, no internet needed at runtime.
* No login and plain HTTP in V1 (as specified). *HTTPS is recommended when the portal is moved beyond a trusted internal network.*
* Credentials are encrypted at rest and never returned, logged or placed in URLs.
* Optional login: set `Authentication:Mode=Local` (see [SECURITY.md](docs/SECURITY.md)).

## Documentation

| | |
|---|---|
| [INSTALLATION.md](docs/INSTALLATION.md) | .NET, SQL Server, migrations, build, publish, Windows service, IIS reverse proxy |
| [CONFIGURATION.md](docs/CONFIGURATION.md) | Every setting, environment variables, enabling HTTPS |
| [SNMP-SETUP.md](docs/SNMP-SETUP.md) | Read-only v2c/v3 setup (Cisco, HPE/Aruba, Dell, Juniper, Extreme), ACLs, LLDP/CDP, the Cisco per-VLAN requirement |
| [IMPORT-GUIDE.md](docs/IMPORT-GUIDE.md) | Template columns, examples, credential profiles, security notes |
| [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Symptoms → causes → fixes |
| [SECURITY.md](docs/SECURITY.md) · [PENTEST.md](docs/PENTEST.md) · [PERFORMANCE.md](docs/PERFORMANCE.md) · [V1-CHECKLIST.md](docs/V1-CHECKLIST.md) | Controls and residual risks · security testing done and recommended · measurements · completion evidence |
| [tools/lab](tools/lab/README.md) · [tools/sqlserver](tools/sqlserver/run-sqlserver-tests.sh) | Two-switch SNMP test lab · run the test suite on real SQL Server in Docker |

## Quick start (development)

Requirements: .NET 8 SDK, Node 22, SQL Server (LocalDB/Express/full).

```bash
dotnet restore
dotnet build
dotnet run --project src/SwitchPortFinder.Api          # http://localhost:5000 (applies migrations in Development)

cd src/SwitchPortFinder.Web
npm install
npm run dev                                            # http://localhost:5173 (proxies /api to :5000)
```

## Windows Server install (one script)

`.\scripts\publish.ps1 -Output .\publish -Runtime win-x64 -Zip`, copy the package to the server, then `.\install.ps1` as Administrator: creates the database, HTTPS (self-signed certificate), local login, Windows service and firewall rule. See [docs/INSTALLATION.md](docs/INSTALLATION.md).

## Production (manual)

```bash
scripts/publish.sh ./publish            # or  .\scripts\publish.ps1 -Output .\publish
cd publish && dotnet SwitchPortFinder.Api.dll          # http://SERVER-IP:5000
```
Details (connection string, migrations, service, firewall) are in [docs/INSTALLATION.md](docs/INSTALLATION.md).

## Tests

```bash
dotnet test                                            # unit + integration (SQLite + scripted SNMP; no SQL Server or network needed)
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"   # 500 switches / 50k devices load test
```
Production uses SQL Server only (`Database:Provider=SqlServer`, the default); `Sqlite` exists solely for tests and sandboxes.

See [docs/SECURITY.md](docs/SECURITY.md) for the security model.

## Testing and production readiness

See [docs/TEST-REPORT.md](docs/TEST-REPORT.md) for the security, penetration-test and QA results, the defects found and fixed, what was *not* tested, and a go-live checklist. Repeatable tools: `tools/security/pentest.py` (black-box security suite), `tools/security/loadtest.py`, `tools/qa/ui-qa.js` (browser QA + accessibility), `tools/sqlserver/run-sqlserver-tests.sh` (integration tests on real SQL Server).
