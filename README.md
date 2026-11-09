# SwitchPort Finder

Internal web portal that answers **"Which switch and which port is this device connected to?"**
**Read-only against network equipment** (SNMP GET / GETNEXT / GETBULK / walk only; there is no SET anywhere in the code).

**Status: V1 feature-complete (Phase 6 of 6).** Add or bulk-import switches · test SNMPv2c/v3 · background discovery of ports, VLANs, MAC tables
(incl. Cisco per-VLAN), ARP and hostnames · LLDP/CDP neighbors with uplink classification and the edge-port rule · global search · device details ·
trace · topology graph · dashboard · discovery history · CSV/Excel export · runtime settings. See [docs/V1-CHECKLIST.md](docs/V1-CHECKLIST.md) for the
evidence behind each item **and what has not been verified on real hardware or SQL Server**.

* One ASP.NET Core process (Kestrel) serves the API **and** the built React UI. No desktop client, no CDN, no internet needed at runtime.
* No login and plain HTTP in V1 (as specified). *HTTPS is recommended when the portal is moved beyond a trusted internal network.*
* Credentials are encrypted at rest and never returned, logged or placed in URLs.

## Documentation

| | |
|---|---|
| [INSTALLATION.md](docs/INSTALLATION.md) | .NET, SQL Server, migrations, build, publish, Windows service, IIS reverse proxy |
| [CONFIGURATION.md](docs/CONFIGURATION.md) | Every setting, environment variables, enabling HTTPS |
| [SNMP-SETUP.md](docs/SNMP-SETUP.md) | Read-only v2c/v3 setup (Cisco, HPE/Aruba, Dell, Juniper, Extreme), ACLs, LLDP/CDP, the Cisco per-VLAN requirement |
| [IMPORT-GUIDE.md](docs/IMPORT-GUIDE.md) | Template columns, examples, credential profiles, security notes |
| [TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) | Symptoms → causes → fixes |
| [SECURITY.md](docs/SECURITY.md) · [PERFORMANCE.md](docs/PERFORMANCE.md) · [V1-CHECKLIST.md](docs/V1-CHECKLIST.md) | Controls and residual risks · measurements · completion evidence |

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

## Production

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
