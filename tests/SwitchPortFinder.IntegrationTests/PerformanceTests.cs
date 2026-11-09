using Stopwatch = System.Diagnostics.Stopwatch;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Discovery;
using SwitchPortFinder.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>
/// Load test at the design target: 500 switches, 48 ports each, 50,000 devices, a 10-ary switch tree of uplinks.
/// Data is generated here (test project only) and written straight to the database. NOTE: runs on SQLite in this repo's CI/sandbox;
/// SQL Server timings must be re-measured on the target server (see docs/PERFORMANCE.md).
/// </summary>
[Trait("Category", "Performance")]
public class PerformanceTests(IsolatedApiFixture fx, ITestOutputHelper output) : IClassFixture<IsolatedApiFixture>
{
    private const int Switches = 500, PortsPerSwitch = 48, Devices = 50_000;
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static bool _seeded; private static readonly SemaphoreSlim Gate = new(1, 1);

    private async Task Seed()
    {
        await Gate.WaitAsync();
        try
        {
            if (_seeded) return;
            using var scope = fx.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var now = DateTime.UtcNow; var sw = Stopwatch.StartNew();
            var codec = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Application.Switches.CredentialCodec>();
            var secrets = codec.Encrypt(new SwitchPortFinder.Application.Snmp.SnmpCredentials { Version = SnmpVersion.V2c, Community = "perf" });
            for (var i = 1; i <= Switches; i++)
                db.Switches.Add(new Switch { Name = $"SW-{i:D3}", ManagementIp = $"10.{i / 250}.{i % 250}.1", UseAsArpSource = i == 1, Status = SwitchStatus.Reachable, LastScanAt = now, CreatedAt = now, UpdatedAt = now,
                    Credential = new SwitchCredential { Version = SnmpVersion.V2c, EncryptedSecrets = secrets } });
            await db.SaveChangesAsync();
            var ids = db.Switches.OrderBy(s => s.Id).Select(s => s.Id).ToList();
            foreach (var id in ids) for (var p = 1; p <= PortsPerSwitch; p++)
                db.SwitchPorts.Add(new SwitchPort { SwitchId = id, IfIndex = p, Name = $"Gi1/0/{p}", AdminUp = true, OperUp = p % 4 != 0 || p == 48, SpeedMbps = 1000, LastSeen = now,
                    PortType = p == 48 && id != ids[0] ? PortType.SwitchUplink : p % 4 == 0 && p != 48 ? PortType.Down : PortType.Endpoint, Confidence = p == 48 ? NeighborConfidence.ConfirmedSwitch : NeighborConfidence.Unknown });
            await db.SaveChangesAsync();
            var ports = db.SwitchPorts.Select(p => new { p.Id, p.SwitchId, p.IfIndex }).ToList().ToDictionary(p => (p.SwitchId, p.IfIndex), p => p.Id);
            // uplinks: switch k (k>0) port 48 -> parent (k-1)/10, via LLDP neighbors
            for (var k = 1; k < Switches; k++)
                db.Neighbors.Add(new SwitchNeighbor { SwitchId = ids[k], PortId = ports[(ids[k], 48)], Protocol = NeighborProtocol.Lldp, RemoteKey = $"p{k}", SysName = $"SW-{(k - 1) / 10 + 1:D3}", Caps = NeighborCaps.Bridge, MatchedSwitchId = ids[(k - 1) / 10], FirstSeen = now, LastSeen = now });
            await db.SaveChangesAsync();
            var nb = db.Neighbors.Select(n => new { n.Id, n.PortId }).ToList().ToDictionary(n => n.PortId, n => n.Id);
            foreach (var p in db.SwitchPorts.Where(p => p.PortType == PortType.SwitchUplink).ToList()) p.NeighborId = nb[p.Id];
            db.ChangeTracker.DetectChanges();   // auto-detect is off for bulk speed
            await db.SaveChangesAsync();
            for (var i = 0; i < ids.Count; i++) db.SwitchMacs.Add(new SwitchMac { SwitchId = ids[i], Mac = $"AA00{i:X8}" });
            for (var k = 0; k < Devices; k++)
            {
                var swIdx = k % Switches; var portNo = (k / Switches) % 47 + 1; if (portNo % 4 == 0) portNo++;
                var pid = ports[(ids[swIdx], portNo)]; var mac = $"02{k:X10}";
                db.Devices.Add(new Device { Mac = mac, Hostname = $"host-{k}", HostnameSource = HostnameSource.ReverseDns, IpAddress = $"172.{16 + k / 60000}.{k / 250 % 250}.{k % 250 + 1}", SwitchId = ids[swIdx], PortId = pid, Vlan = 10 + k % 20, FirstSeen = now.AddMinutes(-k % 5000), LastSeen = now.AddMinutes(-k % 90) });
                db.Observations.Add(new DeviceObservation { Mac = mac, SwitchId = ids[swIdx], PortId = pid, Vlan = 10 + k % 20, FirstSeen = now, LastSeen = now });
                if (k % 5000 == 4999) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); db.ChangeTracker.AutoDetectChangesEnabled = false; }
            }
            await db.SaveChangesAsync();
            output.WriteLine($"SEED: {Switches} switches, {Switches * PortsPerSwitch} ports, {Devices} devices+observations in {sw.ElapsedMilliseconds} ms");
            _seeded = true;
        }
        finally { Gate.Release(); }
    }

    private async Task<(T Result, long Ms)> Time<T>(string label, string url, double budgetMs)
    {
        await _http.GetAsync(url);                                     // warm-up (JIT, query plan)
        var best = long.MaxValue; HttpResponseMessage? r = null;
        for (var i = 0; i < 3; i++) { var sw = Stopwatch.StartNew(); r = await _http.GetAsync(url); sw.Stop(); best = Math.Min(best, sw.ElapsedMilliseconds); }
        r!.EnsureSuccessStatusCode();
        output.WriteLine($"{label,-46} {best,6} ms   (budget {budgetMs} ms)");
        best.Should().BeLessThan((long)budgetMs, $"{label} must stay interactive at 500 switches / 50k devices");
        return (await r.Content.ReadFromJsonAsync<T>(Json)!, best)!;
    }

    [Fact]
    public async Task Interactive_queries_stay_fast_at_500_switches_and_50k_devices()
    {
        await Seed();
        var list = (await Time<Paged<DeviceDto>>("device list page 1 (default sort)", "/api/devices?pageSize=50", 1000)).Result;
        list.Total.Should().Be(Devices); list.Items.Should().HaveCount(50);
        await Time<Paged<DeviceDto>>("device list sorted by hostname, page 500", "/api/devices?pageSize=50&page=500&sort=hostname", 1500);
        await Time<Paged<DeviceDto>>("device list sorted by IP desc", "/api/devices?pageSize=50&sort=ip&desc=true", 1500);
        await Time<Paged<DeviceDto>>("device list filter state=Offline", "/api/devices?pageSize=50&state=Offline", 1500);
        await Time<Paged<DeviceDto>>("device list filter by switch", "/api/devices?pageSize=50&switchId=7", 1000);
        (await Time<Paged<DeviceDto>>("device list text filter 'host-4999'", "/api/devices?q=host-4999", 1500)).Result.Total.Should().BeGreaterThan(0);

        (await Time<SearchResponse>("search hostname exact host-12345", "/api/search?q=host-12345", 1500)).Result.Devices.Should().Contain(d => d.Hostname == "host-12345");
        (await Time<SearchResponse>("search IP 172.16.5.9", "/api/search?q=172.16.5.9", 1500)).Result.Devices.Should().NotBeEmpty();
        (await Time<SearchResponse>("search full MAC any format", "/api/search?q=02:00:00:00:30:39", 1500)).Result.Devices.Should().ContainSingle();   // k=12345 = 0x3039
        (await Time<SearchResponse>("search partial MAC aabb-ish '02000000'", "/api/search?q=0200.0000", 1500)).Result.Devices.Should().NotBeEmpty();
        (await Time<SearchResponse>("search switch name SW-250", "/api/search?q=SW-250", 1500)).Result.Switches.Should().ContainSingle();
        (await Time<SearchResponse>("search port name Gi1/0/17", "/api/search?q=Gi1/0/17", 2000)).Result.Ports.Should().NotBeEmpty();
        await Time<SearchResponse>("search no match", "/api/search?q=zzz-nothing-zzz", 1500);

        await Time<List<SwitchSummaryDtoLite>>("switch list (500)", "/api/switches", 1500);
        await Time<PortsResponse>("switch ports page (48 ports)", "/api/switches/9/ports", 1000);
        await Time<DashboardDto>("dashboard", "/api/dashboard", 2000);
        var topo = (await Time<TopologyResponse>("topology (500 nodes)", "/api/topology", 4000)).Result;
        topo.Nodes.Should().HaveCount(Switches); topo.Edges.Should().HaveCount(Switches - 1);
        var dev = list.Items[0];
        await Time<TraceResult>("trace a device (500-switch graph)", $"/api/devices/{dev.Id}/trace", 2000);
        await Time<DeviceDetailDto>("device details", $"/api/devices/{dev.Id}", 1000);
    }

    [Fact]
    public async Task Full_device_export_of_50k_rows_completes()
    {
        await Seed();
        var sw = Stopwatch.StartNew();
        var csv = await _http.GetAsync("/api/export/devices?format=csv"); var bytes = (await csv.Content.ReadAsByteArrayAsync()).Length; var csvMs = sw.ElapsedMilliseconds;
        sw.Restart(); var x = await _http.GetAsync("/api/export/devices?format=xlsx"); var xb = (await x.Content.ReadAsByteArrayAsync()).Length; var xMs = sw.ElapsedMilliseconds;
        output.WriteLine($"EXPORT 50k via API: csv {csvMs} ms ({bytes / 1024} KB), xlsx {xMs} ms ({xb / 1024} KB)");
        csv.EnsureSuccessStatusCode(); x.EnsureSuccessStatusCode();
        csvMs.Should().BeLessThan(20_000); xMs.Should().BeLessThan(40_000);
    }

    [Fact]
    public async Task Global_classification_after_a_scan_scales_to_the_full_fleet()
    {
        await Seed();
        using var scope = fx.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ClassificationService>();
        var sw = Stopwatch.StartNew();
        await svc.RunExclusiveAsync([], default);
        var first = sw.ElapsedMilliseconds;
        output.WriteLine($"CLASSIFICATION (all 500 switches / 24,000 ports / 50,000 sightings): {first} ms per run");
        // Every scan triggers one run, serialised: "Scan all" of 500 switches costs 500 runs.
        output.WriteLine($"  => projected 'Scan all' classification cost: {first * Switches / 1000.0:F0} s of serialised DB work");
        first.Should().BeLessThan(15_000);
    }

    [Fact]
    public async Task A_busy_switch_scan_is_written_in_batches_into_a_full_database()
    {
        await Seed();
        const string ip = "192.0.2.250"; const int macs = 2000;
        var net = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net;
        net.WithSystem(ip, "BIG-SW", "1.3.6.1.4.1.9.1.2134");
        net.Gets[$"{ip}|{SwitchPortFinder.Application.Snmp.Oids.IpForwarding}"] = SwitchPortFinder.TestSupport.ScriptedSnmp.Int(1);
        net.Walk(ip, "pub", SwitchPortFinder.Application.Snmp.Oids.IfTable, SwitchPortFinder.TestSupport.ScriptedSnmp.IfTable(Enumerable.Range(1, 48).Select(i => (i, $"Gi1/0/{i}", 6, true, (string?)null)).ToArray()));
        net.Walk(ip, "pub", SwitchPortFinder.Application.Snmp.Oids.Dot1dBasePortIfIndex, SwitchPortFinder.TestSupport.ScriptedSnmp.BasePortMap(Enumerable.Range(1, 48).Select(i => (i, i)).ToArray()));
        var mac = (int i) => $"0A{i:X10}";
        net.Walk(ip, "pub", SwitchPortFinder.Application.Snmp.Oids.Dot1qTpFdbTable, SwitchPortFinder.TestSupport.ScriptedSnmp.Dot1qFdb(Enumerable.Range(0, macs).Select(i => (20, mac(i), i % 40 + 1, 3)).ToArray()));
        net.Walk(ip, "pub", SwitchPortFinder.Application.Snmp.Oids.IpNetToPhysicalTable, SwitchPortFinder.TestSupport.ScriptedSnmp.ArpPhysical(Enumerable.Range(0, macs).Select(i => (1, $"198.18.{i / 250}.{i % 250 + 1}", mac(i), 1)).ToArray()));
        var created = await _http.PostAsJsonAsync("/api/switches", new SwitchPortFinder.Application.Switches.SwitchRequest { Name = "BIG-SW", ManagementIp = ip, UseAsArpSource = true, Credential = new SwitchPortFinder.Application.Switches.CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        var id = (await created.Content.ReadFromJsonAsync<SwitchPortFinder.Application.Switches.SwitchDetailDto>(Json))!.Id;
        var jobId = (await (await _http.PostAsync($"/api/switches/{id}/scan", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();
        JobDetailDto job;
        do { await Task.Delay(100); job = (await _http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{jobId}", Json))!; } while (job.Job.Status is JobStatus.Queued or JobStatus.Running);
        output.WriteLine($"SCAN WRITE: {macs} MACs + {macs} ARP entries + 48 ports into a 50k-device DB: {job.Job.DurationSeconds:F2} s total job time");
        job.Job.Status.Should().NotBe(JobStatus.Failed); job.Job.DevicesFound.Should().Be(macs);
        job.Job.DurationSeconds!.Value.Should().BeLessThan(60);
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={id}&pageSize=1", Json))!.Total.Should().Be(macs);
        // a second scan of unchanged data must not rewrite rows or duplicate anything
        var jobId2 = (await (await _http.PostAsync($"/api/switches/{id}/scan", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();
        do { await Task.Delay(100); job = (await _http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{jobId2}", Json))!; } while (job.Job.Status is JobStatus.Queued or JobStatus.Running);
        output.WriteLine($"RESCAN (unchanged): {job.Job.DurationSeconds:F2} s");
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={id}&pageSize=1", Json))!.Total.Should().Be(macs);
    }

    private record SwitchSummaryDtoLite(int Id, string Name);
}
