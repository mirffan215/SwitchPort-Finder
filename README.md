# SwitchPort Finder

Internal web portal that answers **"Which switch and which port is this device connected to?"**
Read-only against network equipment (SNMP GET / GETNEXT / GETBULK / walk only — there is no SET anywhere in the code).

> **Status: Phase 5 of 6 (Topology, history, export).** Implemented: switch management, SNMPv2c/v3, encrypted credentials, bulk
> import/export, discovery (ports, VLANs, MAC/FDB incl. Cisco per-VLAN, ARP, hostnames), LLDP/CDP neighbors with uplink
> classification and the edge-port rule, global search, device details and trace, and now the **topology graph** (React Flow, fully
> bundled; click a switch to open it, click a link to open its port), the **dashboard** (counts, recent jobs, recently discovered
> devices), **discovery history** with filters, and **device export to CSV/Excel** that respects the current filters.
> **Not yet (Phase 6):** performance test at 500 switches / 50k devices, security review, error-handling review, full documentation,
> production publish, settings UI.

## Quick start (development)

Requirements: .NET 8 SDK, Node 20+, SQL Server (LocalDB, Express or full).

```bash
# 1. API (applies migrations automatically in Development)
dotnet restore
dotnet build
dotnet run --project src/SwitchPortFinder.Api          # http://localhost:5000

# 2. Frontend with hot reload (proxies /api to :5000)
cd src/SwitchPortFinder.Web
npm install
npm run dev                                            # http://localhost:5173
```

Set the connection string in `src/SwitchPortFinder.Api/appsettings.json` (`ConnectionStrings:Default`) or via the
`ConnectionStrings__Default` environment variable.

## Production (single process)

```bash
cd src/SwitchPortFinder.Web && npm install && npm run build   # writes into the API's wwwroot
cd ../.. && dotnet publish src/SwitchPortFinder.Api -c Release -o publish
# first run: set Database:AutoMigrate=true once (or run `dotnet ef database update`), then start:
cd publish && dotnet SwitchPortFinder.Api.dll                 # http://0.0.0.0:5000
```

Plain HTTP is used in V1 and there is no login. *HTTPS is recommended when the portal is moved beyond a trusted internal network.*
Kestrel endpoints are configured under `Kestrel:Endpoints` in `appsettings.json`, so HTTPS can be added without code changes.

## Tests

```bash
dotnet test
```

Integration tests run the real API against SQLite with a fake `ISnmpClient` (no SQL Server or network needed).
Production uses SQL Server only (`Database:Provider` = `SqlServer`, the default); `Sqlite` exists solely for tests/sandboxes.

## Security notes

* SNMP credentials are encrypted at rest (ASP.NET Core Data Protection; keys in `Security:KeysPath`, wrapped with machine-scope DPAPI on Windows).
* Credentials are never returned by the API, logged, placed in URLs or included in error messages. Tests scan responses, logs and the database file for this.
* Import files may contain secrets: they are processed in memory only. Prefer credential profiles.

More documentation (installation, configuration, SNMP setup, troubleshooting, import guide) lands in Phase 6.
