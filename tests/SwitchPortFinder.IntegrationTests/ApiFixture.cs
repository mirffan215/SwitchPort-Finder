using System.Net;
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
    public Task<string?> ResolveAsync(string ip, CancellationToken ct) => Task.FromResult(Names.TryGetValue(ip, out var n) ? n : null);
}

public class ApiFixture : WebApplicationFactory<Program>, IDisposable
{
    public string Dir { get; } = Path.Combine(Path.GetTempPath(), "spf-it-" + Guid.NewGuid().ToString("N"));
    public string DbPath => Path.Combine(Dir, "test.db");
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        // UseSetting (not ConfigureAppConfiguration): Program.cs reads configuration during startup, before late config sources apply.
        foreach (var (k, v) in new Dictionary<string, string>
        {
            ["Database:Provider"] = "Sqlite", ["Database:AutoMigrate"] = "true",
            ["ConnectionStrings:Default"] = $"Data Source={DbPath}", ["Security:KeysPath"] = KeysPath,
            ["Serilog:MinimumLevel:Default"] = "Debug", ["Snmp:TimeoutMs"] = "500", ["Snmp:Retries"] = "0",
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

    public new void Dispose()
    {
        if (_captured) Console.SetOut(_origOut);
        base.Dispose();
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
