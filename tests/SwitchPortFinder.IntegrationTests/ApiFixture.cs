using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Scriptable fake: behaviour is chosen by the community / username so tests need no network.</summary>
public sealed class FakeSnmpClient : ISnmpClient
{
    public List<SnmpTarget> Calls { get; } = [];
    /// <summary>Scripted network used by discovery tests (walks and per-IP system data).</summary>
    public SwitchPortFinder.TestSupport.ScriptedSnmp Net { get; } = new();

    public Task<IReadOnlyList<SnmpVarBind>> GetAsync(SnmpTarget t, IEnumerable<string> oids, CancellationToken ct)
    {
        lock (Calls) Calls.Add(t);
        if (Net.Gets.Keys.Any(k => k.StartsWith(t.Address + "|"))) return Net.GetAsync(t, oids, ct);
        var c = t.Credentials;
        if (c.Version == SnmpVersion.V2c && c.Community == "bad") throw new SnmpException(SnmpFailure.Timeout, "timeout");
        if (c.Version == SnmpVersion.V3 && c.AuthPassword == "wrongpassword") throw new SnmpException(SnmpFailure.AuthenticationFailed, "auth");
        if (t.Address.ToString() == "192.0.2.99") throw new SnmpException(SnmpFailure.Timeout, "timeout");
        IReadOnlyList<SnmpVarBind> r =
        [
            new(Oids.SysDescr, new SnmpValue(SnmpValueKind.OctetString, 0, null, "Cisco IOS Software, Catalyst L3 Switch")),
            new(Oids.SysObjectId, new SnmpValue(SnmpValueKind.ObjectId, 0, null, "1.3.6.1.4.1.9.1.2134")),
            new(Oids.SysUpTime, new SnmpValue(SnmpValueKind.TimeTicks, 123456)),
            new(Oids.SysName, new SnmpValue(SnmpValueKind.OctetString, 0, null, "CORE-SW-01")),
        ];
        return Task.FromResult(r);
    }
    public Task<IReadOnlyList<SnmpVarBind>> GetNextAsync(SnmpTarget t, IEnumerable<string> o, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<SnmpVarBind>> WalkAsync(SnmpTarget t, string r, CancellationToken ct) => Net.WalkAsync(t, r, ct);
}

public sealed class FakeResolver : SwitchPortFinder.Application.Abstractions.IHostnameResolver
{
    public Dictionary<string, string> Names { get; } = [];
    public HashSet<string> Throw { get; } = [];
    public Task<string?> ResolveAsync(string ip, CancellationToken ct) =>
        Throw.Contains(ip) ? throw new InvalidOperationException("dns exploded") : Task.FromResult(Names.TryGetValue(ip, out var n) ? n : null);
}

public class ApiFixture : WebApplicationFactory<Program>, IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "spf-it-" + Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Dir, "test.db");

    /// <summary>
    /// Set SPF_TEST_SQLSERVER to a SQL Server connection string (WITHOUT a Database=) to run the whole suite on real SQL Server through the real
    /// migrations; each fixture gets a throwaway database. Unset = SQLite (the default, no external dependency).
    /// </summary>
    public static readonly string? SqlServer = Environment.GetEnvironmentVariable("SPF_TEST_SQLSERVER");
    public bool UsingSqlServer => SqlServer is not null;
    private readonly List<string> _databases = [];
    public string DbName { get; } = "spf_it_" + Guid.NewGuid().ToString("N")[..12];
    /// <summary>(provider, connection string) for a database of this fixture.</summary>
    public (string Provider, string Conn) Db(string name, string sqlitePath)
    {
        if (SqlServer is null) return ("Sqlite", $"Data Source={sqlitePath}");
        lock (_databases) _databases.Add(name);
        return ("SqlServer", $"{SqlServer.TrimEnd(';')};Database={name}");
    }

    /// <summary>Connection to the master database for admin work (drop).</summary>
    private static void DropDatabase(string name)
    {
        try
        {
            using var c = new Microsoft.Data.SqlClient.SqlConnection(SqlServer!.TrimEnd(';') + ";Database=master"); c.Open();
            using var cmd = c.CreateCommand(); cmd.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END"; cmd.ExecuteNonQuery();
        }
        catch { /* best effort */ }
    }

    /// <summary>All text values currently stored in this fixture's database (used to prove secrets are not stored in clear).</summary>
    public string DumpDatabaseText()
    {
        if (SqlServer is null) return Encoding.Latin1.GetString(File.ReadAllBytes(DbPath)) + (File.Exists(DbPath + "-wal") ? Encoding.Latin1.GetString(File.ReadAllBytes(DbPath + "-wal")) : "");
        var sb = new StringBuilder();
        using var c = new Microsoft.Data.SqlClient.SqlConnection($"{SqlServer.TrimEnd(';')};Database={DbName}"); c.Open();
        var cols = new List<(string T, string C)>();
        using (var q = c.CreateCommand())
        {
            q.CommandText = "SELECT TABLE_NAME, COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE DATA_TYPE IN ('char','varchar','nchar','nvarchar','text','ntext')";
            using var r = q.ExecuteReader(); while (r.Read()) cols.Add((r.GetString(0), r.GetString(1)));
        }
        foreach (var (t, col) in cols)
        {
            using var q = c.CreateCommand(); q.CommandText = $"SELECT [{col}] FROM [{t}] WHERE [{col}] IS NOT NULL";
            using var r = q.ExecuteReader(); while (r.Read()) sb.Append(r.GetString(0)).Append('\n');
        }
        return sb.ToString();
    }
    public string KeysPath => Path.Combine(Dir, "keys");
    public StringWriter Logs { get; } = new();
    private readonly TextWriter _origOut = Console.Out;

    public ApiFixture() : this(true) { }

    protected ApiFixture(bool captureLogs)
    {
        Directory.CreateDirectory(Dir);
        // Serilog's console sink writes to Console.Out; capture it so tests can scan all log output.
        if (captureLogs) Console.SetOut(new TeeWriter(_origOut, TextWriter.Synchronized(Logs)));
        _captured = captureLogs;
    }
    private readonly bool _captured;

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads configuration during startup, before late config sources apply.
        var (provider, conn) = Db(DbName, DbPath);
        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["Database:Provider"] = provider, ["Database:AutoMigrate"] = "true",
            ["ConnectionStrings:Default"] = conn, ["Security:KeysPath"] = KeysPath,
            ["Serilog:MinimumLevel:Default"] = "Debug", ["Snmp:TimeoutMs"] = "500", ["Snmp:Retries"] = "0", ["Discovery:HealthPollMinutes"] = "0", ["Notifications:Worker"] = "false",   // the background poll is driven explicitly by the tests that need it
        }) builder.UseSetting(k, v);
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<ISnmpClient>();
            s.AddSingleton<FakeSnmpClient>();
            s.AddSingleton<ISnmpClient>(sp => sp.GetRequiredService<FakeSnmpClient>());
            s.RemoveAll<SwitchPortFinder.Application.Abstractions.IHostnameResolver>();
            s.AddSingleton<FakeResolver>();
            s.AddSingleton<SwitchPortFinder.Application.Abstractions.IHostnameResolver>(sp => sp.GetRequiredService<FakeResolver>());
        });
    }

    /// <summary>A second host on its own database, e.g. in Production mode or with extra settings.</summary>
    public WebApplicationFactory<Program> Variant(string environment, params (string Key, string Value)[] settings)
    {
        var sub = Path.Combine(Dir, "variant-" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(sub);
        var (provider, conn) = Db("spf_it_" + Guid.NewGuid().ToString("N")[..12], Path.Combine(sub, "v.db"));
        return WithWebHostBuilder(b =>
        {
            b.UseEnvironment(environment);
            b.UseSetting("Database:Provider", provider); b.UseSetting("ConnectionStrings:Default", conn); b.UseSetting("Security:KeysPath", Path.Combine(sub, "keys"));
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });
    }

    public new void Dispose()
    {
        if (_captured) Console.SetOut(_origOut);
        base.Dispose();
        if (SqlServer is not null) { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); string[] names; lock (_databases) names = _databases.ToArray(); foreach (var n in names) DropDatabase(n); }
        try { Directory.Delete(Dir, true); } catch { /* best effort */ }
    }

    private sealed class TeeWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); b.Write(value); }
        public override void Write(string? value) { a.Write(value); b.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }
}

/// <summary>Own database, no console capture: for test classes that need an isolated, fixed topology (IPs/MACs reused across scenarios).</summary>
public sealed class IsolatedApiFixture : ApiFixture { public IsolatedApiFixture() : base(false) { } }

[CollectionDefinition("api")] public class ApiCollection : ICollectionFixture<ApiFixture>;

/// <summary>Host with local accounts switched on (Authentication:Mode=Local), small lockout/limits so tests are fast.</summary>
public sealed class AuthApiFixture : ApiFixture
{
    public const string AdminPassword = "Bootstrap-Root-Pass-2026";
    public AuthApiFixture() : base(false) { }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:Mode", "Local");
        builder.UseSetting("Authentication:BootstrapAdmin:Username", "admin");
        builder.UseSetting("Authentication:BootstrapAdmin:Password", AdminPassword);
        builder.UseSetting("Authentication:MaxFailedLogins", "3");
        builder.UseSetting("Authentication:LockoutSeconds", "2");
        builder.UseSetting("Authentication:LoginsPerMinute", "40");
        builder.UseSetting("Authentication:RequireMfaForAdmins", "false");   // the classic tests use a password-only administrator; MfaTests covers the policy
    }
}

/// <summary>Local accounts with the production default: administrators must enrol an authenticator app.</summary>
public sealed class MfaApiFixture : ApiFixture
{
    public const string AdminPassword = "Bootstrap-Root-Pass-2026";
    public MfaApiFixture() : base(false) { }
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("Authentication:Mode", "Local");
        builder.UseSetting("Authentication:BootstrapAdmin:Username", "admin");
        builder.UseSetting("Authentication:BootstrapAdmin:Password", AdminPassword);
        builder.UseSetting("Authentication:MaxFailedLogins", "3");
        builder.UseSetting("Authentication:LockoutSeconds", "2");
        builder.UseSetting("Authentication:LoginsPerMinute", "200");
        builder.UseSetting("Authentication:RequireMfaForAdmins", "true");
    }
}
