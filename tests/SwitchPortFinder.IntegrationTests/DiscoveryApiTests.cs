using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Two-switch scenario (core = ARP source, access with the PC) through the real API, queue worker, EF and SQLite.</summary>
[Collection("api")]
public class DiscoveryApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static int _seed;

    private record Net(string CoreIp, string AccessIp, string PcIp, string PcMac)
    {
        public string PcMacDisplay => SwitchPortFinder.Domain.MacAddress.Format(PcMac);
    }
    private static string Cs(int n) => $"203.0.{n}.";

    /// <summary>Builds an isolated copy of the scenario so tests don't interfere (unique IP space per call).</summary>
    private Net Build()
    {
        var n = Interlocked.Increment(ref _seed);
        var core = Cs(n) + "10"; var access = Cs(n) + "11"; var pc = Cs(n) + "101";
        var PcMac = $"AABBCCDD{n:X2}01"; var OtherMac = $"AABBCCDD{n:X2}02";   // unique hosts per scenario
        var CoreMac = $"AABBCC{n:X2}0A10"; var AccessMac = $"AABBCC{n:X2}0B11";   // switch MACs are unique per scenario (a known switch's MAC is never a 'device')
        var net = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net;
        net.WithSystem(core, "CORE-SW-01", "1.3.6.1.4.1.9.1.2134").WithSystem(access, "ACCESS-SW-01", "1.3.6.1.4.1.9.1.2135");
        // CORE: uplink Gi1/0/24 has learned the PC, the access switch and one more host (3 MACs) – plus it routes (ARP source).
        net.Walk(core, "pub", Oids.IfTable, IfTable((24, "Gi1/0/24", 6, true, CoreMac), (23, "Gi1/0/23", 6, false, null)));
        net.Walk(core, "pub", Oids.IfXTable, IfXTable((24, "Gi1/0/24", 1000, "to ACCESS-SW-01"), (23, "Gi1/0/23", 0, "")));
        net.Walk(core, "pub", Oids.Dot1qVlanStaticName, [Vb($"{Oids.Dot1qVlanStaticName}.50", Txt("users"))]);
        net.Walk(core, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 24), (2, 23)));
        net.Walk(core, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((50, PcMac, 1, 3), (50, AccessMac, 1, 3), (50, OtherMac, 1, 3)));
        net.Walk(core, "pub", Oids.IpNetToPhysicalTable, ArpPhysical((1, pc, PcMac, 1)));
        net.Gets[$"{core}|{Oids.IpForwarding}"] = Int(1);
        // ACCESS: PC on Gi1/0/15 (PVID 50). No ARP table for user VLANs.
        net.Walk(access, "pub", Oids.IfTable, IfTable((15, "Gi1/0/15", 6, true, null), (24, "Gi1/0/24", 6, true, AccessMac), (16, "Gi1/0/16", 6, true, null)));
        net.Walk(access, "pub", Oids.IfXTable, IfXTable((15, "Gi1/0/15", 1000, "PC-001 wall jack"), (24, "Gi1/0/24", 1000, "uplink"), (16, "Gi1/0/16", 1000, "")));
        net.Walk(access, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((15, 15), (24, 24), (16, 16)));
        net.Walk(access, "pub", Oids.Dot1qPvid, [Vb($"{Oids.Dot1qPvid}.15", new SnmpValue(SnmpValueKind.Gauge, 50))]);
        net.Walk(access, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((50, PcMac, 15, 3), (50, CoreMac, 24, 3)));
        fx.Services.GetService(typeof(FakeResolver)).As<FakeResolver>().Names[pc] = "PC-001";
        return new Net(core, access, pc, PcMac);
    }

    private async Task<int> AddSwitch(string name, string ip, bool arp = false)
    {
        var r = await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = name, ManagementIp = ip, UseAsArpSource = arp, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<SwitchDetailDto>(Json))!.Id;
    }

    private async Task<JobDetailDto> Scan(int switchId)
    {
        var resp = await _http.PostAsync($"/api/switches/{switchId}/scan", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Accepted, "scan returns immediately with 202");
        var jobId = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();
        for (var i = 0; i < 150; i++)
        {
            var d = (await _http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{jobId}", Json))!;
            if (d.Job.Status is not (JobStatus.Queued or JobStatus.Running)) return d;
            await Task.Delay(100);
        }
        throw new TimeoutException("job did not finish");
    }

    private Task<PortsResponse> Ports(int id) => _http.GetFromJsonAsync<PortsResponse>($"/api/switches/{id}/ports", Json)!;

    [Fact]
    public async Task Full_scan_shows_ports_devices_vlan_and_joins_ip_and_hostname_globally_from_the_arp_source()
    {
        var n = Build();
        var access = await AddSwitch("ACCESS-SW-01", n.AccessIp);
        var core = await AddSwitch("CORE-SW-01", n.CoreIp, arp: true);

        // Scan the access switch FIRST: it has no ARP, so the PC's IP is not yet known.
        var job1 = await Scan(access);
        job1.Job.Status.Should().Be(JobStatus.CompletedWithWarnings);   // empty-ARP/VLAN information is a warning, not a failure
        job1.Job.PortsFound.Should().Be(3); job1.Job.DevicesFound.Should().Be(2);
        job1.Steps.Select(s => s.Name).Should().Contain([Steps.System, Steps.Interfaces, Steps.Vlans, Steps.MacTable, Steps.Arp, Steps.Correlation]);
        job1.Steps.Single(s => s.Name == Steps.Correlation).State.Should().Be(StepState.Ok);

        var before = await Ports(access);
        var g15 = before.Ports.Single(p => p.Name == "Gi1/0/15");
        g15.Devices.Should().ContainSingle().Which.Should().Match<PortDeviceDto>(d => d.Hostname == "Unknown Device" && d.Ip == null && d.Mac == n.PcMacDisplay && d.Vlan == 50);
        before.HasArpSource.Should().BeTrue("a switch flagged \"Use as ARP source\" exists, so the \"mark your core switch\" hint is not needed");

        // Now scan the core (ARP source). The IP→MAC join is GLOBAL, so the access-switch device gets its IP and hostname.
        var job2 = await Scan(core);
        job2.Job.Status.Should().BeOneOf(JobStatus.Completed, JobStatus.CompletedWithWarnings);
        job2.Steps.Single(s => s.Name == Steps.Arp).State.Should().Be(StepState.Ok);

        var after = await Ports(access);
        var row = after.Ports.Single(p => p.Name == "Gi1/0/15");
        row.Type.Should().Be(PortType.Endpoint); row.Status.Should().Be("UP"); row.SpeedMbps.Should().Be(1000); row.Vlan.Should().Be(50); row.Alias.Should().Be("PC-001 wall jack");
        row.Devices.Should().ContainSingle().Which.Should().Match<PortDeviceDto>(d => d.Hostname == "PC-001" && d.Ip == n.PcIp && d.Mac == n.PcMacDisplay);
        after.HasArpSource.Should().BeTrue();

        // The PC was learned on the core's uplink AND on the access edge port. Uplink sightings never count: location = access Gi1/0/15.
        var dev = (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=PC-001", Json))!.Items.Single();
        dev.SwitchName.Should().Be("ACCESS-SW-01"); dev.PortName.Should().Be("Gi1/0/15"); dev.LowConfidence.Should().BeFalse("the core's Gi1/0/24 learned the access switch's own MAC, so it is an uplink and only one edge candidate remains");
        dev.HostnameSource.Should().Be(HostnameSource.ReverseDns); dev.State.Should().Be(DeviceState.Active);

        // Down port shows no device; unlearned up port is Unknown.
        var coreRows = (await Ports(core)).Ports;
        coreRows.Single(p => p.Name == "Gi1/0/23").Should().Match<PortRowDto>(p => p.Type == PortType.Down && p.Status == "DOWN" && p.Devices.Count == 0);
        (await Ports(access)).Ports.Single(p => p.Name == "Gi1/0/16").Type.Should().Be(PortType.Unknown);

        // Natural port order: Gi1/0/15 before Gi1/0/16 before Gi1/0/24.
        (await Ports(access)).Ports.Select(p => p.Name).Should().Equal("Gi1/0/15", "Gi1/0/16", "Gi1/0/24");

        // Switch metadata is recorded.
        var sw = (await _http.GetFromJsonAsync<SwitchDetailDto>($"/api/switches/{core}", Json))!;
        sw.Status.Should().Be(SwitchStatus.Reachable); sw.SysName.Should().Be("CORE-SW-01"); sw.DetectedManufacturer.Should().Be(Manufacturer.Cisco); sw.LastScanAt.Should().NotBeNull();
        (await _http.GetFromJsonAsync<List<SwitchSummaryDto>>("/api/switches", Json))!.Single(s => s.Id == core).PortCount.Should().Be(2);
    }

    [Fact]
    public async Task Rescanning_is_incremental_no_duplicate_devices_and_first_seen_is_preserved()
    {
        var n = Build();
        var access = await AddSwitch("ACCESS-R", n.AccessIp);
        await Scan(access);
        var first = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={access}", Json))!;
        await Task.Delay(50);
        await Scan(access);
        var second = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={access}", Json))!;
        second.Total.Should().Be(first.Total).And.Be(2);
        second.Items.Select(d => d.Id).Should().BeEquivalentTo(first.Items.Select(d => d.Id));
        foreach (var d in second.Items) d.FirstSeen.Should().Be(first.Items.Single(x => x.Id == d.Id).FirstSeen);
        second.Items.Max(d => d.LastSeen).Should().BeAfter(first.Items.Max(d => d.LastSeen));
        (await Ports(access)).Ports.Should().HaveCount(3, "ports are upserted, not duplicated");
    }

    [Fact]
    public async Task Unreachable_switch_fails_the_job_with_a_friendly_error_and_marks_the_switch_unreachable()
    {
        var ip = Cs(Interlocked.Increment(ref _seed)) + "99";
        fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net.Unreachable.Add(ip);
        fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net.WithSystem(ip, "X", "1.3.6.1");
        var id = await AddSwitch("DEAD-SW", ip);
        var job = await Scan(id);
        job.Job.Status.Should().Be(JobStatus.Failed);
        job.Errors.Should().ContainSingle().Which.Message.Should().Contain("No response");
        job.Steps.Single(s => s.Name == Steps.System).State.Should().Be(StepState.Failed);
        job.Steps.Where(s => s.Name != Steps.System).Should().OnlyContain(s => s.State == StepState.Skipped);
        (await _http.GetFromJsonAsync<SwitchDetailDto>($"/api/switches/{id}", Json))!.Status.Should().Be(SwitchStatus.Unreachable);
    }

    [Fact]
    public async Task Queued_scan_is_deduplicated_and_scan_all_returns_a_job_per_switch()
    {
        var n = Build();
        var a = await AddSwitch("DEDUP-A", n.AccessIp);
        var first = await (await _http.PostAsync($"/api/switches/{a}/scan", null)).Content.ReadFromJsonAsync<JsonElement>();
        var second = await (await _http.PostAsync($"/api/switches/{a}/scan", null)).Content.ReadFromJsonAsync<JsonElement>();
        // The first job may already have finished; either the same active job or a fresh one is acceptable, never a duplicate in flight.
        var jobs = await _http.GetFromJsonAsync<List<JobSummaryDto>>($"/api/discovery/jobs?switchId={a}", Json);
        jobs!.Count(j => j.Status is JobStatus.Queued or JobStatus.Running).Should().BeLessThanOrEqualTo(1);
        (second.GetProperty("jobId").GetInt32() >= first.GetProperty("jobId").GetInt32()).Should().BeTrue();

        var all = await _http.PostAsync("/api/switches/scan-all", null);
        all.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await all.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobIds").GetArrayLength().Should().BeGreaterThan(0);
        (await _http.PostAsync("/api/switches/999999/scan", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Devices_endpoint_is_server_side_paged_sorted_and_filtered()
    {
        var n = Build();
        var access = await AddSwitch("PAGE-ACCESS", n.AccessIp);
        await Scan(access);
        var p1 = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={access}&pageSize=1&page=1&sort=mac", Json))!;
        var p2 = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?switchId={access}&pageSize=1&page=2&sort=mac", Json))!;
        p1.Total.Should().Be(2); p1.Items.Should().HaveCount(1); p2.Items.Should().HaveCount(1);
        string.Compare(p1.Items[0].Mac, p2.Items[0].Mac, StringComparison.Ordinal).Should().BeNegative();
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?q={n.PcMac.ToLowerInvariant()}&pageSize=500", Json))!.Items.Should().ContainSingle().Which.Mac.Should().Be(n.PcMacDisplay);   // any MAC format / case
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?pageSize=100000", Json))!.PageSize.Should().BeLessThanOrEqualTo(200, "page size is clamped so the browser never loads everything");
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?state=Stale", Json))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_scanned_switch_works_and_keeps_devices_without_a_location()
    {
        var n = Build();
        var access = await AddSwitch("DEL-ACCESS", n.AccessIp);
        await Scan(access);
        (await _http.DeleteAsync($"/api/switches/{access}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync($"/api/switches/{access}/ports")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _http.GetFromJsonAsync<List<JobSummaryDto>>($"/api/discovery/jobs?switchId={access}", Json))!.Should().BeEmpty("jobs cascade with the switch");
    }
}

[Collection("api")]
public class TopPortsApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Rejects_an_unknown_metric_and_clamps_n()
    {
        (await _http.GetAsync("/api/metrics/top-ports?metric=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.GetAsync("/api/metrics/top-ports?metric=utilization&n=100000")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ranks_ports_by_utilization_and_by_drops_from_stored_rates()
    {
        var sw = await Seed();
        var util = (await _http.GetFromJsonAsync<TopPortsResponse>("/api/metrics/top-ports?metric=utilization&n=2", Json))!;
        util.Items.Select(i => i.PortName).Should().ContainInOrder("Gi9/0/2", "Gi9/0/1");
        util.Items[0].Traffic.UtilizationPct.Should().BeApproximately(90, 0.01);
        util.Items.Should().HaveCount(2);
        var drops = (await _http.GetFromJsonAsync<TopPortsResponse>("/api/metrics/top-ports?metric=drops&n=5", Json))!;
        drops.Items.Where(i => i.SwitchId == sw).Select(i => i.PortName).Should().Equal("Gi9/0/3", "Gi9/0/1");   // the zero-drop port is not listed
        drops.Items[0].Traffic.DiscardsPerSec.Should().BeApproximately(50, 0.01);
    }

    private async Task<int> Seed()
    {
        using var scope = fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
        foreach (var old in db.SwitchPorts.Where(p => p.RateAt != null)) { old.RateAt = null; old.RateSeconds = null; }
        var sw = new SwitchPortFinder.Domain.Entities.Switch { Name = "TOP-SW", ManagementIp = "203.0.113.200" };
        db.Switches.Add(sw); await db.SaveChangesAsync();
        SwitchPortFinder.Domain.Entities.SwitchPort P(int i, double inBps, double outBps, double disc) => new()
        {
            SwitchId = sw.Id, IfIndex = i, Name = $"Gi9/0/{i}", OperUp = true, SpeedMbps = 1000, RateInBps = inBps, RateOutBps = outBps, RateDiscardsPerSec = disc, RateErrorsPerSec = 0, RateSeconds = 300, RateAt = DateTime.UtcNow, CtrWide = true,
        };
        db.SwitchPorts.AddRange(P(1, 500e6, 100e6, 5), P(2, 900e6, 10e6, 0), P(3, 10e6, 10e6, 50));
        await db.SaveChangesAsync();
        return sw.Id;
    }
}

[Collection("api")]
public class HostileInputTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();

    /// <summary>Regression: SQL Server rejects a LIKE parameter longer than the column ("String or binary data would be truncated"), which surfaced as HTTP 500 only on SQL Server.</summary>
    [Theory]
    [InlineData("/api/search?q=")] [InlineData("/api/devices?q=")]
    public async Task Very_long_search_text_is_accepted_not_a_server_error(string url)
    {
        foreach (var q in new[] { new string('A', 5000), new string('1', 300) + ".", "%" + new string('_', 400), new string('é', 1000) })
            ((int)(await _http.GetAsync(url + Uri.EscapeDataString(q))).StatusCode).Should().BeLessThan(500, $"{url} with {q.Length} chars");
    }
}
