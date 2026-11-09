using Microsoft.AspNetCore.Hosting;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Production-mode hardening: same-origin enforcement, headers, target policy, rate limits, upload limits.</summary>
public class SecurityTests(IsolatedApiFixture fx) : IClassFixture<IsolatedApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static HttpRequestMessage Req(HttpMethod m, string url, string? origin = null, HttpContent? body = null)
    { var r = new HttpRequestMessage(m, url) { Content = body }; if (origin is not null) r.Headers.TryAddWithoutValidation("Origin", origin); return r; }

    [Fact]
    public async Task Cross_origin_state_changing_requests_are_blocked_but_same_origin_and_non_browser_clients_work()
    {
        using var f = fx.Variant("Production"); var http = f.CreateClient();
        foreach (var (m, url) in new[] { (HttpMethod.Post, "/api/switches/scan-all"), (HttpMethod.Delete, "/api/switches/1"), (HttpMethod.Put, "/api/switches/1"), (HttpMethod.Post, "/api/admin/import/switches/commit") })
        {
            var evil = await http.SendAsync(Req(m, url, "http://evil.example", new StringContent("{}", Encoding.UTF8, "application/json")));
            evil.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{m} {url} from a foreign origin");
            (await evil.Content.ReadAsStringAsync()).Should().NotContain("   at ");
        }
        (await http.SendAsync(Req(HttpMethod.Post, "/api/switches/scan-all", "http://localhost"))).StatusCode.Should().Be(HttpStatusCode.Accepted);      // same origin as Host
        (await http.SendAsync(Req(HttpMethod.Post, "/api/switches/scan-all"))).StatusCode.Should().Be(HttpStatusCode.Accepted);                         // curl / scripts send no Origin
        // reads are allowed, but there is NO CORS: the browser would refuse to hand the response to the foreign page
        var read = await http.SendAsync(Req(HttpMethod.Get, "/api/switches", "http://evil.example"));
        read.StatusCode.Should().Be(HttpStatusCode.OK); read.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
        var pre = await http.SendAsync(Req(HttpMethod.Options, "/api/switches", "http://evil.example")); pre.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Fact]
    public async Task Trusted_origins_and_forwarded_host_allow_reverse_proxy_setups()
    {
        using var f = fx.Variant("Production", ("Security:TrustedOrigins:0", "https://portal.corp.example")); var http = f.CreateClient();
        (await http.SendAsync(Req(HttpMethod.Post, "/api/switches/scan-all", "https://portal.corp.example"))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var fwd = Req(HttpMethod.Post, "/api/switches/scan-all", "https://proxy.corp.example"); fwd.Headers.Add("X-Forwarded-Host", "proxy.corp.example");
        (await http.SendAsync(fwd)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await http.SendAsync(Req(HttpMethod.Post, "/api/switches/scan-all", "https://other.example"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Security_headers_are_set_the_api_is_never_cached_and_swagger_is_not_exposed_in_production()
    {
        using var f = fx.Variant("Production"); var http = f.CreateClient();
        var api = await http.GetAsync("/api/switches");
        api.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff"); api.Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        api.Headers.GetValues("Referrer-Policy").Should().Contain("no-referrer"); api.Headers.CacheControl!.NoStore.Should().BeTrue();
        api.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'self'").And.Contain("frame-ancestors 'none'");
        (await http.GetAsync("/")).Headers.GetValues("X-Frame-Options").Should().Contain("DENY");
        var swagger = await http.GetAsync("/swagger/v1/swagger.json"); (await swagger.Content.ReadAsStringAsync()).Should().NotContain("openapi", "Swagger exists only in Development");
    }

    [Theory]
    [InlineData("127.0.0.1")] [InlineData("::1")] [InlineData("0.0.0.0")] [InlineData("255.255.255.255")] [InlineData("224.0.0.1")] [InlineData("ff02::1")]
    public async Task Addresses_that_can_never_be_switches_are_rejected_in_production(string ip)
    {
        using var f = fx.Variant("Production"); var http = f.CreateClient();
        var cred = new CredentialInput { Version = SnmpVersion.V2c, Community = "x" };
        var create = await http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "X", ManagementIp = ip, Credential = cred }, Json);
        create.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await create.Content.ReadAsStringAsync()).Should().Contain("managementIp");
        (await http.PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = ip, Credential = cred }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes($"Name,ManagementIP,SnmpVersion,Community\nA,{ip},v2c,c\n")), "file", "x.csv" } };
        var prev = await (await http.PostAsync("/api/admin/import/switches/preview", form)).Content.ReadFromJsonAsync<JsonElement>();
        prev.GetProperty("rows")[0].GetProperty("status").GetString().Should().Be("Error");
    }

    [Fact]
    public async Task Loopback_is_allowed_only_in_development()
    {
        var cred = new CredentialInput { Version = SnmpVersion.V2c, Community = "x" };
        using var dev = fx.Variant("Development"); 
        (await dev.CreateClient().PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "127.0.0.9", Credential = cred }, Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var prod = fx.Variant("Production");
        (await prod.CreateClient().PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "127.0.0.9", Credential = cred }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_snmp_test_endpoints_are_rate_limited()
    {
        using var f = fx.Variant("Production", ("Security:SnmpTestsPerMinute", "4")); var http = f.CreateClient();
        var body = new TestConnectionRequest { ManagementIp = "192.0.2.77", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "ok" } };
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 7; i++) codes.Add((await http.PostAsJsonAsync("/api/switches/test", body, Json)).StatusCode);
        codes.Take(4).Should().OnlyContain(c => c == HttpStatusCode.OK); codes.Skip(4).Should().OnlyContain(c => c == HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_zip_bomb_posing_as_xlsx_is_rejected_without_being_expanded()
    {
        var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        using (var s = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.SmallestSize).Open())
        { var chunk = new byte[1024 * 1024]; for (var i = 0; i < 300; i++) s.Write(chunk); }          // 300 MB of zeros compresses to ~300 KB
        bytes.Length.Should().BeLessThan(2_000_000);
        var http = fx.CreateClient(); var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await http.PostAsync("/api/admin/import/switches/preview", new MultipartFormDataContent { { new ByteArrayContent(bytes.ToArray()), "file", "bomb.xlsx" } });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await resp.Content.ReadAsStringAsync()).Should().Contain("too large when unpacked");
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public async Task Oversized_uploads_and_bad_input_give_clean_problem_details_without_internals()
    {
        var http = fx.CreateClient();
        var big = await http.PostAsync("/api/admin/import/switches/preview", new MultipartFormDataContent { { new ByteArrayContent(new byte[11 * 1024 * 1024]), "file", "big.csv" } });
        big.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        foreach (var resp in new[] { await http.PostAsync("/api/switches", new StringContent("{ not json", Encoding.UTF8, "application/json")),
                                      await http.PostAsync("/api/switches", new StringContent("{\"name\":1,\"managementIp\":[]}", Encoding.UTF8, "application/json")),
                                      await http.GetAsync("/api/switches/abc"), await http.GetAsync("/api/devices/-5"), await http.GetAsync("/api/switches/99999999/ports") })
        {
            ((int)resp.StatusCode).Should().BeInRange(400, 404);
            var text = await resp.Content.ReadAsStringAsync();
            text.Should().NotContain("   at ").And.NotContain("System.").And.NotContain("Microsoft.").And.NotContain("SwitchPortFinder.").And.NotContain("SELECT ");
        }
    }

    [Fact]
    public void Production_wiring_uses_the_real_snmp_client_and_contains_no_mock_or_fake_types()
    {
        using var f = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            var dir = Path.Combine(Path.GetTempPath(), "spf-wiring-" + Guid.NewGuid().ToString("N")[..6]); Directory.CreateDirectory(dir);
            b.UseEnvironment("Production"); b.UseSetting("Database:Provider", "Sqlite"); b.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(dir, "w.db")}"); b.UseSetting("Security:KeysPath", dir);
        });
        using var scope = f.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ISnmpClient>().GetType().Name.Should().Be("SharpSnmpClient");
        scope.ServiceProvider.GetRequiredService<IHostnameResolver>().GetType().Name.Should().Be("DnsHostnameResolver");
        scope.ServiceProvider.GetRequiredService<ICredentialProtector>().GetType().Name.Should().Be("DataProtectionCredentialProtector");
        var prodAssemblies = new[] { typeof(Program).Assembly, typeof(ISnmpClient).Assembly, typeof(SwitchPortFinder.Discovery.SnmpSwitchScanner).Assembly, typeof(SwitchPortFinder.Infrastructure.Persistence.AppDbContext).Assembly };
        prodAssemblies.SelectMany(a => a.GetTypes()).Select(t => t.Name).Should().NotContain(n => n.Contains("Mock") || n.Contains("Fake") || n.Contains("Scripted"));
    }
}

/// <summary>Error-handling matrix (spec 15B): each failure is handled, logged, stored as a DiscoveryError, friendly, and never aborts the rest.</summary>
public class ErrorMatrixTests(IsolatedApiFixture fx) : IClassFixture<IsolatedApiFixture>
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private FakeSnmpClient Snmp => fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>();
    private static int _n;
    private const string Pc = "02AA00000001";

    private (string Ip, int N) NewSwitchIp() { var n = Interlocked.Increment(ref _n); return ($"198.51.{100 + n}.1", n); }

    /// <summary>A healthy 2-port switch with one learned MAC; individual tests then break one thing.</summary>
    private string Healthy(string objectId = "1.3.6.1.4.1.9.1.2134", bool arp = false)
    {
        var (ip, n) = NewSwitchIp(); var net = Snmp.Net; var mac = $"02AA0000{n:X4}";
        net.WithSystem(ip, "SW-" + n, objectId);
        net.Walk(ip, "pub", Oids.IfTable, IfTable((1, "Gi1/0/1", 6, true, null), (2, "Gi1/0/2", 6, true, null)));
        net.Walk(ip, "pub", Oids.IfXTable, IfXTable((1, "Gi1/0/1", 1000, ""), (2, "Gi1/0/2", 1000, "")));
        net.Walk(ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1), (2, 2)));
        net.Walk(ip, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((10, mac, 1, 3)));
        if (arp) { net.Gets[$"{ip}|{Oids.IpForwarding}"] = Int(1); }
        return ip;
    }

    private async Task<int> Add(string ip, Manufacturer m = Manufacturer.AutoDetect, bool arp = false, CredentialInput? c = null)
    {
        var r = await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "S-" + ip, ManagementIp = ip, Manufacturer = m, UseAsArpSource = arp, Credential = c ?? new CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<SwitchDetailDto>(Json))!.Id;
    }

    private async Task<JobDetailDto> Scan(int id)
    {
        var jobId = (await (await _http.PostAsync($"/api/switches/{id}/scan", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();
        for (var i = 0; i < 200; i++) { var d = (await _http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{jobId}", Json))!; if (d.Job.Status is not (JobStatus.Queued or JobStatus.Running)) return d; await Task.Delay(50); }
        throw new TimeoutException();
    }

    private static StepState State(JobDetailDto j, string step) => j.Steps.Single(s => s.Name == step).State;

    [Fact] public async Task Invalid_snmpv3_credentials_fail_the_job_with_a_clear_message_that_contains_no_secret()
    {
        var ip = Healthy(); Snmp.Net.AuthFail.Add(ip);
        var id = await Add(ip, c: new CredentialInput { Version = SnmpVersion.V3, Username = "matrix-user-1", AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = "MATRIX-AUTH-PW1", PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = "MATRIX-PRIV-PW1" });
        var j = await Scan(id);
        j.Job.Status.Should().Be(JobStatus.Failed); j.Errors.Single().Message.Should().Contain("authentication failed");
        var all = JsonSerializer.Serialize(j) + await _http.GetStringAsync($"/api/switches/{id}");
        all.Should().NotContain("MATRIX-AUTH-PW1").And.NotContain("MATRIX-PRIV-PW1").And.NotContain("matrix-user-1");
        (await _http.GetFromJsonAsync<SwitchDetailDto>($"/api/switches/{id}", Json))!.Status.Should().Be(SwitchStatus.Unreachable);
    }

    [Fact] public async Task A_collector_that_hits_an_internal_bug_is_isolated_and_its_detail_is_not_exposed()
    {
        var ip = Healthy(); Snmp.Net.ThrowUnexpected.Add($"{ip}|{Oids.IfXTable}");
        var j = await Scan(await Add(ip));
        j.Job.Status.Should().Be(JobStatus.CompletedWithWarnings, "one broken step never fails the whole scan");
        State(j, Steps.Interfaces).Should().Be(StepState.Failed); State(j, Steps.MacTable).Should().Be(StepState.Ok); State(j, Steps.Correlation).Should().Be(StepState.Ok);
        JsonSerializer.Serialize(j).Should().NotContain("boom-internal-detail");
        j.Errors.Should().Contain(e => e.Step == Steps.Interfaces && e.Code == "Unexpected");
    }

    [Fact] public async Task Dns_failure_of_any_kind_never_fails_a_scan_and_leaves_Unknown_Device()
    {
        var ip = Healthy(arp: true); var n = _n; var mac = $"02AA0000{n:X4}"; var pcIp = $"198.18.{n}.5";
        Snmp.Net.Walk(ip, "pub", Oids.IpNetToPhysicalTable, ArpPhysical((1, pcIp, mac, 1)));
        fx.Services.GetService(typeof(FakeResolver)).As<FakeResolver>().Throw.Add(pcIp);
        var j = await Scan(await Add(ip, arp: true));
        j.Job.Status.Should().NotBe(JobStatus.Failed); State(j, Steps.Correlation).Should().Be(StepState.Ok);
        var dev = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?q={pcIp}", Json))!.Items.Single();
        (dev.Hostname, dev.Ip).Should().Be(("Unknown Device", pcIp));
    }

    [Theory] [InlineData(Manufacturer.TpLink)] [InlineData(Manufacturer.Netgear)] [InlineData(Manufacturer.OtherGeneric)] [InlineData(Manufacturer.AutoDetect)]
    public async Task Unknown_or_unsupported_manufacturers_fall_back_to_generic_and_still_return_ports_macs(Manufacturer selected)
    {
        var ip = Healthy(objectId: "1.3.6.1.4.1.99999.1");      // an enterprise number we have never heard of
        var id = await Add(ip, selected); var j = await Scan(id);
        j.Job.Status.Should().NotBe(JobStatus.Failed); j.Job.PortsFound.Should().Be(2); j.Job.DevicesFound.Should().Be(1);
        State(j, Steps.Cdp).Should().Be(StepState.Skipped, "CDP is never required for non-Cisco gear");
    }

    [Fact] public async Task Missing_CDP_on_a_cisco_is_a_warning_and_missing_LLDP_is_a_warning_not_a_failure()
    {
        var ip = Healthy(); Snmp.Net.ThrowOnWalk.Add($"{ip}|{Oids.CdpCacheTable}"); Snmp.Net.ThrowOnWalk.Add($"{ip}|{Oids.LldpRemTable}");
        var j = await Scan(await Add(ip));
        j.Job.Status.Should().Be(JobStatus.CompletedWithWarnings);
        State(j, Steps.Cdp).Should().Be(StepState.Warning); State(j, Steps.Lldp).Should().Be(StepState.Warning);
        j.Steps.Single(s => s.Name == Steps.Lldp).Message.Should().Be("LLDP unavailable. Continuing with MAC and interface discovery...");
        j.Errors.Should().Contain(e => e.Code == "OidUnavailable" && e.Step == Steps.Cdp).And.Contain(e => e.Code == "OidUnavailable" && e.Step == Steps.Lldp);
        j.Job.PortsFound.Should().Be(2);
    }

    [Fact] public async Task Every_interface_attribute_is_persisted_name_status_speed_mac_vlan()
    {
        var ip = Healthy(); var id = await Add(ip);
        Snmp.Net.Walk(ip, "pub", Oids.Dot1qPvid, [Vb($"{Oids.Dot1qPvid}.1", new SnmpValue(SnmpValueKind.Gauge, 42))]);
        await Scan(id);
        var p1 = (await _http.GetFromJsonAsync<PortsResponse>($"/api/switches/{id}/ports", Json))!.Ports.Single(p => p.Name == "Gi1/0/1");
        (p1.Status, p1.SpeedMbps, p1.Vlan, p1.Alias).Should().Be(("UP", 1000L, 42, null));
    }

    [Fact] public async Task A_truncated_interface_walk_keeps_the_previous_port_status_instead_of_marking_everything_down()
    {
        var ip = Healthy(); var id = await Add(ip);
        await Scan(id);
        (await _http.GetFromJsonAsync<PortsResponse>($"/api/switches/{id}/ports", Json))!.Summary.Connected.Should().Be(2);
        // the agent now returns the ifTable WITHOUT ifOperStatus (a cut-off reply)
        Snmp.Net.Walks[$"{ip}|pub"][Oids.IfTable] = IfTable((1, "Gi1/0/1", 6, true, null), (2, "Gi1/0/2", 6, true, null)).Where(v => !v.Oid.StartsWith(Oids.IfTable + ".8.")).ToList();
        var j = await Scan(id);
        State(j, Steps.Interfaces).Should().Be(StepState.Warning); j.Errors.Should().Contain(e => e.Code == "Incomplete");
        var ports = (await _http.GetFromJsonAsync<PortsResponse>($"/api/switches/{id}/ports", Json))!;
        ports.Summary.Connected.Should().Be(2, "unknown status must not flip ports to DOWN"); ports.Ports.Should().OnlyContain(p => p.Status == "UP");
    }

    [Fact] public async Task Timeouts_in_the_mac_table_and_empty_tables_are_warnings_with_stored_errors()
    {
        var ip = Healthy(arp: true); Snmp.Net.ThrowOnWalk.Add($"{ip}|{Oids.Dot1qTpFdbTable}");
        var j = await Scan(await Add(ip, arp: true));          // ARP source with an empty ARP table, Q-BRIDGE timing out, no BRIDGE-MIB data
        State(j, Steps.MacTable).Should().Be(StepState.Warning); State(j, Steps.Arp).Should().Be(StepState.Warning);
        j.Errors.Select(e => e.Code).Should().Contain(["OidUnavailable", "Empty"]);
        j.Job.Status.Should().Be(JobStatus.CompletedWithWarnings); j.Job.ErrorCount.Should().Be(j.Errors.Count);
    }

    [Fact] public async Task Unreachable_switch_is_reported_friendly_and_does_not_affect_other_switches_in_a_scan_all()
    {
        var dead = Healthy(); Snmp.Net.Unreachable.Add(dead); var live = Healthy();
        var deadId = await Add(dead); var liveId = await Add(live);
        var all = await (await _http.PostAsync("/api/switches/scan-all", null)).Content.ReadFromJsonAsync<JsonElement>();
        all.GetProperty("jobIds").GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        var d = await WaitFor(deadId); var l = await WaitFor(liveId);
        d.Job.Status.Should().Be(JobStatus.Failed); d.Errors.Single().Message.Should().Contain("No response");
        l.Job.Status.Should().NotBe(JobStatus.Failed); l.Job.DevicesFound.Should().Be(1);

        async Task<JobDetailDto> WaitFor(int sw)
        {
            for (var i = 0; i < 200; i++) { var j = (await _http.GetFromJsonAsync<List<JobSummaryDto>>($"/api/discovery/jobs?switchId={sw}", Json))!; if (j.Count > 0 && j[0].Status is not (JobStatus.Queued or JobStatus.Running)) return (await _http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{j[0].Id}", Json))!; await Task.Delay(50); }
            throw new TimeoutException();
        }
    }
}

public class SettingsApiTests(IsolatedApiFixture fx) : IClassFixture<IsolatedApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Settings_are_validated_applied_live_and_survive_a_restart()
    {
        var http = fx.CreateClient();
        var s = (await http.GetFromJsonAsync<RuntimeSettingsDto>("/api/settings", Json))!;
        (s.SnmpTimeoutMs, s.MaxParallelScans, s.ScanIntervalMinutes).Should().Be((500, 5, 0), "the fixture runs with a 500 ms timeout from its configuration");

        var bad = await http.PutAsJsonAsync("/api/settings", s with { SnmpTimeoutMs = 5, MaxParallelScans = 0 }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest); var body = await bad.Content.ReadAsStringAsync(); body.Should().Contain("snmpTimeoutMs").And.Contain("maxParallelScans");

        var saved = await http.PutAsJsonAsync("/api/settings", s with { SnmpTimeoutMs = 2500, SnmpRetries = 0, MaxParallelScans = 7, ScanIntervalMinutes = 30, StaleAfterDays = 21 }, Json);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        // applied live: the SNMP client sees the new timeout/retries on the very next call, no restart
        var snmp = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>();
        await http.PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "192.0.2.123", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "ok" } }, Json);
        snmp.Calls.Last().Should().Match<SnmpTarget>(t => t.TimeoutMs == 2500 && t.Retries == 0);

        // and persisted: a brand-new host on the same database loads them at startup
        using var second = fx.WithWebHostBuilder(_ => { }); var again = (await second.CreateClient().GetFromJsonAsync<RuntimeSettingsDto>("/api/settings", Json))!;
        (again.SnmpTimeoutMs, again.MaxParallelScans, again.ScanIntervalMinutes, again.StaleAfterDays).Should().Be((2500, 7, 30, 21));
    }

    private record RuntimeSettingsDto(int SnmpTimeoutMs, int SnmpRetries, int SnmpMaxRepetitions, int MaxParallelScans, int PerVlanTimeoutMs, int PerVlanParallelism, int OfflineAfterMinutes, int StaleAfterDays,
        int ScanIntervalMinutes, int DnsTimeoutMs, int HostnameRefreshHours, int UplinkHeuristicMinMacs, double UplinkHeuristicRatio);
}
