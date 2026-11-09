# Installation

## Automated install on Windows Server (recommended)

One script does everything: database, deployment, **HTTPS** (self-signed certificate for this machine), **local login**, Windows service, firewall, file logging.

**1. Build the package** (on any machine with the .NET 8 SDK and Node.js 22 — or on the server itself):

```powershell
git clone <repo-url> SwitchPortFinder; cd SwitchPortFinder
.\scripts\publish.ps1 -Output .\publish -Runtime win-x64 -Zip      # creates publish.zip (installer, schema script and docs are inside)
```

**2. On the server** (Windows Server 2022 with SQL Server 2022 on the same machine): install the **ASP.NET Core Runtime 8** once
(`winget install Microsoft.DotNet.AspNetCore.8`, or run the installer with `-InstallPrerequisites`), unzip the package, open **PowerShell as Administrator** in that folder and run:

```powershell
.\install.ps1                      # uses this computer's name, the default SQL instance, https://<computer-name>/ and a generated admin password
```

Typical variations:

```powershell
.\install.ps1 -HostName spf.corp.local                                   # the DNS name users will type
.\install.ps1 -SqlInstance .\SQLEXPRESS -HttpsPort 8443                   # SQL Express, port 8443 (if 443 is taken, e.g. by IIS)
.\install.ps1 -AdminPassword (Read-Host -AsSecureString)                 # choose the administrator password yourself (12+ characters)
.\install.ps1 -ConnectionString "Server=SQLHOST;Database=SwitchPortFinder;User Id=spf;Password=...;TrustServerCertificate=True"   # remote SQL / SQL authentication
```

**What it does** (and prints as it goes):
1. Checks the .NET runtime, that the SQL Server service is running and the HTTPS port is free.
2. Copies the application to `C:\SwitchPortFinder`.
3. Creates a self-signed certificate (names: host name, FQDN, `localhost`, the server's IP addresses; 3 years), trusts it **on this server**, and exports `C:\ProgramData\SwitchPortFinder\cert\SwitchPortFinder.cer` for other PCs.
4. Writes `appsettings.Production.json`: **HTTPS only on the network** (plain HTTP is bound to `127.0.0.1` for health checks only), `Authentication:Mode=Local`, daily log files (30 kept), no auto-migration.
5. Creates the service `SwitchPortFinder` as the virtual account `NT SERVICE\SwitchPortFinder`, delayed automatic start (after SQL Server), restart on failure.
6. Creates the database, applies the migrations and gives the service account **`db_datareader` + `db_datawriter` only** — the running portal has no right to change the schema.
7. Opens the firewall port (Domain and Private profiles), starts the service, checks HTTPS, signs in as the administrator, then **removes the bootstrap password from the configuration file**.
8. Prints the URL and the generated administrator password **once**.

The person running the script needs SQL Server rights to create a database and a login (a member of `sysadmin`, or `dbcreator` + `securityadmin`).

**Other PCs and the certificate warning.** A self-signed certificate is trusted on the server only. On each client PC, import `SwitchPortFinder.cer` into *Trusted Root Certification Authorities* (or distribute it with Group Policy); then browsers stop warning. If you later have a certificate from your own CA, replace the `Kestrel:Endpoints:Https:Certificate` settings (CONFIGURATION.md).

**Upgrading:** build a new package, unzip, run `.\install.ps1 -KeepConfig` — files are replaced, the schema is migrated, your configuration, keys and data are kept.
**Backups:** `.\backup.ps1 -Destination D:\SPF-Backups` backs up the database **and** the keys/certificate/configuration (both are needed to restore); schedule it daily in Task Scheduler.
**Uninstall:** `.\uninstall.ps1` removes the service, firewall rule and certificate and keeps your data (`-RemoveData`, `-RemoveInstallFolder` to delete it).
**Renew the certificate:** `.\install.ps1 -KeepConfig -RenewCertificate`.

> Tested here on Linux (the configuration the installer writes was run against real SQL Server 2022 with HTTPS, login and file logging; the setup command was verified, including that the least-privileged database account runs the portal with real scans). The Windows-only steps (service creation, certificate store, firewall, ACLs) could not be executed in the development environment — **run the installer on a test VM first.**

---

The sections below describe the same steps manually.

SwitchPort Finder is an internal web portal: **one ASP.NET Core process (Kestrel) + SQL Server**. Users only need a browser; nothing is installed on client PCs.

> **Trying it quickly with Docker for SQL Server:**
> `docker run -d --name spf-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='Your-Strong-Passw0rd' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest`, connection string
> `Server=localhost,1433;Database=SwitchPortFinder;User Id=sa;Password=Your-Strong-Passw0rd;TrustServerCertificate=True`. (Use a proper SQL Server installation in production.)

## 1. Requirements

| Component | Requirement |
|---|---|
| Server OS | Windows Server 2019+ / Windows 10+ (also runs on Linux) |
| .NET | **.NET 8 LTS** — *ASP.NET Core Runtime 8.x* to run, *SDK 8.x* to build |
| Database | **SQL Server** 2019+ (Express, Standard, LocalDB for evaluation) |
| Node.js | 22 LTS (only to **build** the frontend; Vite 8 needs Node ≥ 20.19) — not needed on the server if you copy a built `publish` folder |
| Network | Server → switches: **UDP 161** (SNMP). Users → server: **TCP 5000** (configurable). No internet access is needed at runtime. |

Install .NET 8: <https://dotnet.microsoft.com/download/dotnet/8.0> (Windows: `winget install Microsoft.DotNet.AspNetCore.8` for the runtime, `Microsoft.DotNet.SDK.8` for the SDK).

## 2. Install SQL Server and create the database

1. Install SQL Server (or SQL Server Express). Enable TCP/IP if the portal runs on another machine.
2. Create an empty database, e.g. `SwitchPortFinder` (the portal can create the *tables*; it does not need to create the database).
3. Give the portal's identity rights on it. For the first run (which applies migrations) `db_owner` is simplest; afterwards you may reduce it to `db_datareader` + `db_datawriter` if you apply migrations manually (below).

## 3. Get the code and build

```bash
git clone <repository-url> SwitchPortFinder && cd SwitchPortFinder
dotnet restore
dotnet build
dotnet test          # optional: ~320 unit and ~75 integration tests (SQLite + a scripted SNMP client; no SQL Server or network needed)
```

## 4. Configure the connection string

Edit `src/SwitchPortFinder.Api/appsettings.json` **or** (recommended in production) set an environment variable / `appsettings.Production.json` next to the published DLL:

```json
{ "ConnectionStrings": { "Default": "Server=SQLHOST;Database=SwitchPortFinder;Trusted_Connection=True;TrustServerCertificate=True" } }
```

```text
# SQL authentication
Server=SQLHOST,1433;Database=SwitchPortFinder;User Id=spf;Password=...;TrustServerCertificate=True
# LocalDB (evaluation only)
Server=(localdb)\MSSQLLocalDB;Database=SwitchPortFinder;Trusted_Connection=True
```
Environment-variable form: `ConnectionStrings__Default`. See [CONFIGURATION.md](CONFIGURATION.md).

## 5. Create the schema (migrations)

Pick one:

* **Automatic (simplest):** set `Database:AutoMigrate` to `true` for the first start (it is `true` in Development, `false` in production by default), then set it back.
* **EF tool:**
  ```bash
  dotnet tool install --global dotnet-ef --version 8.*
  dotnet ef database update --project src/SwitchPortFinder.Infrastructure --startup-project src/SwitchPortFinder.Api
  ```
* **SQL script:** run `docs/schema.sql` (idempotent, generated from the migrations) with SSMS/`sqlcmd`. Regenerate with
  `dotnet ef migrations script --idempotent --project src/SwitchPortFinder.Infrastructure --startup-project src/SwitchPortFinder.Api -o docs/schema.sql`.

## 6. Development: run it

```bash
dotnet run --project src/SwitchPortFinder.Api          # API on http://localhost:5000 (Swagger at /swagger in Development)
cd src/SwitchPortFinder.Web
npm install
npm run dev                                            # UI with hot reload on http://localhost:5173 (proxies /api to :5000)
```
Development mode allows loopback SNMP targets (see `Security:AllowLoopbackTargets`).

## 7. Production: build and publish

One command builds the React app into the API's `wwwroot` and publishes a single folder; Kestrel then serves **both** the API and the UI (no separate web server):

```bash
scripts/publish.sh ./publish                 # Linux/macOS/Git Bash
.\scripts\publish.ps1 -Output .\publish      # PowerShell
# add a runtime id to get a Windows apphost (SwitchPortFinder.Api.exe):   scripts/publish.sh ./publish win-x64
```
Copy `publish` to the server (e.g. `C:\SwitchPortFinder`), put your `appsettings.Production.json` beside it and start:

```bash
cd C:\SwitchPortFinder
set ASPNETCORE_ENVIRONMENT=Production
dotnet SwitchPortFinder.Api.dll
```
Open **`http://SERVER-IP:5000`** from any browser. Allow inbound TCP 5000 in the Windows firewall:
`netsh advfirewall firewall add rule name="SwitchPort Finder" dir=in action=allow protocol=TCP localport=5000`.

The default listen address is `http://0.0.0.0:5000` (`Kestrel:Endpoints:Http:Url`). V1 runs over **plain HTTP with no login**, as specified. *HTTPS is recommended when the portal is moved beyond a trusted internal network* — see "Enabling HTTPS" in CONFIGURATION.md.

## 8. Run as a Windows service (optional)

The app supports the Windows Service Control Manager natively. Publish with a Windows runtime id so you get the `.exe`, then:

```bat
sc.exe create SwitchPortFinder binPath= "C:\SwitchPortFinder\SwitchPortFinder.Api.exe" start= auto obj= "DOMAIN\svc-spf" password= "..."
sc.exe description SwitchPortFinder "SwitchPort Finder internal portal"
sc.exe start SwitchPortFinder
```
* Put production settings in `appsettings.Production.json` and set the machine environment variable `ASPNETCORE_ENVIRONMENT=Production`.
* The service account needs: access to the SQL Server database, **read** on the install folder, **write** on the data-protection keys folder (`Security:KeysPath`, default `keys` beside the DLL).
* Logs go to the console sink; for a service add a file sink (Serilog config, see CONFIGURATION.md) or use the Windows Event Log of your choice.

## 9. IIS reverse proxy (optional)

You can keep Kestrel as the service and put IIS in front for HTTPS/host names: install **URL Rewrite** and **Application Request Routing**, enable proxy, then add a rule on the site:

```xml
<rule name="spf" stopProcessing="true">
  <match url="(.*)" />
  <action type="Rewrite" url="http://localhost:5000/{R:1}" />
  <serverVariables><set name="HTTP_X_FORWARDED_HOST" value="{HTTP_HOST}" /></serverVariables>
</rule>
```
(allow the `HTTP_X_FORWARDED_HOST` server variable in URL Rewrite). Because the portal rejects cross-origin *state-changing* requests, a proxy that rewrites the `Host` header must send `X-Forwarded-Host` (above) **or** you list the public origin under `Security:TrustedOrigins`.

## 10. Updating

Stop the service, copy the new `publish` output over the old one (keep `appsettings.Production.json` and the `keys` folder), apply new migrations (step 5), start. Credentials stay readable as long as the **keys folder** is kept and the service runs on the same machine (see SECURITY.md).

## 11. Optional: turn on login (local accounts)

```json
{ "Authentication": { "Mode": "Local", "BootstrapAdmin": { "Username": "admin", "Password": "a-long-unique-passphrase" } } }
```
Restart, sign in as that user, then create your own accounts under *Settings → Admin → Users*, change the bootstrap password and **delete the password from the configuration file**. Use HTTPS (section 9 or CONFIGURATION.md).
*Lost the administrator password?* With the service stopped run `DELETE FROM Users;` on the database and start it again with `BootstrapAdmin` set — a new administrator is created. (Switch and discovery data are untouched.)

## 12. First use

1. **Switches → + Add Switch**: name, management IP, manufacturer, SNMP version + credentials → **Test Connection** → Save. Or **Settings → Admin → Import Switches** for many switches ([IMPORT-GUIDE.md](IMPORT-GUIDE.md)).
2. Tick **Use as ARP source** on your core / Layer 3 switch (IP addresses come from its ARP table).
3. **Scan Now** (or **Scan all switches**). Scan the switches that connect to each other so uplinks can be identified.
4. Search from the box at the top.
