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
    public async Task A_scan_stores_the_switch_uptime_for_the_dashboard_gauges()
    {
        var n = Build();
        var access = await AddSwitch("UPTIME-SCAN-SW", n.AccessIp);
        (await Scan(access)).Job.Status.Should().NotBe(JobStatus.Failed);
        var up = (await _http.GetFromJsonAsync<SwitchUptimeResponse>("/api/metrics/switch-uptime?n=20", Json))!.Items.Single(i => i.SwitchId == access);
        up.UptimeSeconds.Should().BeInRange(10, 70, "the fake switch reports sysUpTime = 1000 ticks (10 s)");
    }

    [Fact]
    public async Task A_manual_name_and_pinned_port_survive_rescans_and_make_the_device_traceable()
    {
        var n = Build();
        var access = await AddSwitch("MAN-ACCESS", n.AccessIp); var core = await AddSwitch("MAN-CORE", n.CoreIp, arp: true);
        await Scan(access); await Scan(core);
        var dev = (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?q={n.PcMac}", Json))!.Items.Single();
        var ports = await Ports(access); var target = ports.Ports.Single(p => p.Name == "Gi1/0/16"); var uplink = ports.Ports.Single(p => p.Name == "Gi1/0/24");

        // refused: an uplink, a port of another switch, a malformed request
        (await _http.PutAsJsonAsync($"/api/devices/{dev.Id}/manual", new ManualDeviceRequest(null, "x", null, null, null, access, uplink.PortId, null), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an uplink is not where a device is plugged in");
        var corePort = (await Ports(core)).Ports.First();
        (await _http.PutAsJsonAsync($"/api/devices/{dev.Id}/manual", new ManualDeviceRequest(null, "x", null, null, null, access, corePort.PortId, null), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.PutAsJsonAsync($"/api/devices/{dev.Id}/manual", new ManualDeviceRequest(null, "bad\u0001name", null, null, null, null, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var ok = await _http.PutAsJsonAsync($"/api/devices/{dev.Id}/manual", new ManualDeviceRequest(null, "Reception printer", null, "Front desk", "HP LaserJet, asset 4711", access, target.PortId, 60), Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var d1 = (await ok.Content.ReadFromJsonAsync<DeviceDetailDto>(Json))!.Device;
        (d1.Hostname, d1.PortName, d1.Vlan, d1.Pinned, d1.Owner).Should().Be(("Reception printer", "Gi1/0/16", 60, true, "Front desk"));

        await Scan(access); await Scan(core);   // rescans must not undo it
        var d2 = (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{dev.Id}", Json))!;
        (d2.Device.Hostname, d2.Device.PortName, d2.Device.Pinned).Should().Be(("Reception printer", "Gi1/0/16", true));
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=Front%20desk", Json))!.Items.Should().ContainSingle(d => d.Id == dev.Id, "owner is searchable");
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=asset%204711", Json))!.Devices.Should().ContainSingle(d => d.Id == dev.Id, "notes are searchable");
        var trace = await _http.GetAsync($"/api/devices/{dev.Id}/trace"); trace.StatusCode.Should().Be(HttpStatusCode.OK);
        (await trace.Content.ReadAsStringAsync()).Should().Contain("Gi1/0/16");

        // clearing the manual details hands everything back to discovery
        (await _http.PutAsJsonAsync($"/api/devices/{dev.Id}/manual", new ManualDeviceRequest(null, null, null, null, null, null, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        var d3 = (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{dev.Id}", Json))!.Device;
        (d3.Pinned, d3.PortName).Should().Be((false, "Gi1/0/15"), "the discovered edge port is restored");
        (await _http.DeleteAsync($"/api/devices/{dev.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "a discovered device cannot be deleted: it would reappear");
    }

    [Fact]
    public async Task The_health_poll_records_cpu_memory_latency_uptime_traffic_and_downtime_history()
    {
        var n = Build();
        var access = await AddSwitch("POLL-ACCESS", n.AccessIp);
        await Scan(access);                                   // creates the ports; the poll only reads existing ones
        var net = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net;
        SnmpVarBind C(string oid, long v) => Vb(oid, new SnmpValue(SnmpValueKind.Counter, v));
        void Counters(long inOct) => net.Walk(n.AccessIp, "pub", Oids.IfXTable, IfXTable((15, "Gi1/0/15", 1000, "PC-001 wall jack"), (24, "Gi1/0/24", 1000, "uplink"), (16, "Gi1/0/16", 1000, ""))
            .Concat([C($"{Oids.IfXTable}.6.15", inOct), C($"{Oids.IfXTable}.10.15", inOct / 2)]));
        net.Walk(n.AccessIp, "pub", Oids.CiscoCpuTotalEntry, [Vb($"{Oids.CiscoCpuTotalEntry}.8.1", new SnmpValue(SnmpValueKind.Gauge, 37))]);
        net.Walk(n.AccessIp, "pub", Oids.CiscoMemoryPoolEntry, [Vb($"{Oids.CiscoMemoryPoolEntry}.5.1", new SnmpValue(SnmpValueKind.Gauge, 250)), Vb($"{Oids.CiscoMemoryPoolEntry}.6.1", new SnmpValue(SnmpValueKind.Gauge, 750))]);
        var poll = fx.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SwitchPortFinder.Infrastructure.Discovery.HealthPollService>().Single();

        Counters(0); await poll.PollOneAsync(access, CancellationToken.None);                       // baseline
        using (var scope = fx.Services.CreateScope())
        {   // pretend the baseline was taken 5 minutes ago
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            foreach (var p in db.SwitchPorts.Where(p => p.SwitchId == access)) if (p.CtrAt != null) p.CtrAt = p.CtrAt.Value.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        Counters(75_000_000); await poll.PollOneAsync(access, CancellationToken.None);               // 75 MB in -> 2 Mbit/s over 300 s

        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            var sw = db.Switches.Single(x => x.Id == access);
            (sw.CpuPct, sw.MemPct).Should().Be((37, 25)); sw.LatencyMs.Should().NotBeNull(); sw.UptimeSeconds.Should().BeGreaterThan(0);
            var port = db.SwitchPorts.Single(p => p.SwitchId == access && p.IfIndex == 15);
            port.RateInBps.Should().BeApproximately(2_000_000, 2_000);
            db.PortMetrics.Where(m => m.PortId == port.Id).Select(m => m.UtilPct).Single().Should().BeApproximately(0.2, 0.01);
            db.SwitchMetrics.Count(m => m.SwitchId == access && m.Reachable).Should().BeGreaterThanOrEqualTo(3, "scan + two polls");
        }

        // the switch goes away: a failed poll is a downtime sample and flips the status
        net.Unreachable.Add(n.AccessIp);
        await poll.PollOneAsync(access, CancellationToken.None);
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            db.Switches.Single(x => x.Id == access).Status.Should().Be(SwitchStatus.Unreachable);
            db.SwitchMetrics.Count(m => m.SwitchId == access && !m.Reachable).Should().Be(1);
        }
        net.Unreachable.Remove(n.AccessIp);
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

    [Fact]
    public async Task A_wireless_controller_scan_lists_its_access_points_and_marks_the_ones_it_stops_listing_offline()
    {
        var n = Interlocked.Increment(ref _seed); var ip = $"203.9.{n}.5";
        var net = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net;
        net.WithSystem(ip, "WLC-01", "1.3.6.1.4.1.9.1.2530");
        string Idx(int a) => $"0.17.34.51.68.{a}";
        void Aps(params int[] which)
        {
            string R(int c) => $"{Oids.LwappApEntry}.{c}";
            net.Walks.Remove($"{ip}|pub");
            void Col(int c, Func<int, SnmpValue> v) => net.Walk(ip, "pub", R(c), which.Select(a => Vb($"{R(c)}.{Idx(a)}", v(a))));
            Col(2, a => new SnmpValue(SnmpValueKind.OctetString, 0, [0, 17, 34, 51, 68, (byte)a]));
            Col(5, a => Txt($"AP-{n}-{a}"));
            Col(6, _ => new SnmpValue(SnmpValueKind.TimeTicks, 8_640_000));
            Col(54, a => new SnmpValue(SnmpValueKind.Gauge, a));
        }
        Aps(1, 2, 3);
        var r = await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = $"WLC-{n}", ManagementIp = ip, IsWirelessController = true, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await r.Content.ReadFromJsonAsync<SwitchDetailDto>(Json))!.Id;
        (await Scan(id)).Job.Status.Should().NotBe(JobStatus.Failed);

        var ours = (await _http.GetFromJsonAsync<AccessPointsResponse>("/api/access-points", Json))!.Items.Where(i => i.Name.StartsWith($"AP-{n}-")).ToList();
        ours.Should().HaveCount(3);
        ours.Should().OnlyContain(a => a.Online && a.Controller == $"WLC-{n}" && a.UptimeSeconds == 86400);
        ours.Single(a => a.Name.EndsWith("-3")).WifiClients.Should().Be(3);

        Aps(1, 2);                                  // AP 3 is no longer joined
        await Scan(id);
        var after = (await _http.GetFromJsonAsync<AccessPointsResponse>("/api/access-points", Json))!.Items.Where(i => i.Name.StartsWith($"AP-{n}-")).ToList();
        after.Single(a => a.Name.EndsWith("-3")).Online.Should().BeFalse();
        after.Where(a => !a.Name.EndsWith("-3")).Should().OnlyContain(a => a.Online);
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

[Collection("api")]
public class VendorApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Manufacturer_is_shown_and_searchable_in_the_device_list_and_in_global_search()
    {
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            var now = DateTime.UtcNow;
            db.Devices.AddRange(
                new SwitchPortFinder.Domain.Entities.Device { Mac = "1866DA163EB5", FirstSeen = now, LastSeen = now, Vendor = "Dell Inc." },
                new SwitchPortFinder.Domain.Entities.Device { Mac = "0000C0031E29", FirstSeen = now, LastSeen = now, Vendor = null },   // created before the column existed: computed on the fly
                new SwitchPortFinder.Domain.Entities.Device { Mac = "06299573847A", FirstSeen = now, LastSeen = now, Vendor = SwitchPortFinder.Domain.OuiLookup.PrivateMac });
            await db.SaveChangesAsync();
        }
        var list = (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=Dell&pageSize=50", Json))!;
        list.Items.Should().ContainSingle(d => d.Mac == "18:66:DA:16:3E:B5" && d.Vendor == "Dell Inc.");
        var search = (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=dell", Json))!;
        search.Devices.Should().Contain(d => d.Mac == "18:66:DA:16:3E:B5");
        var all = (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?pageSize=200&sort=vendor", Json))!.Items;
        all.Single(d => d.Mac == "00:00:C0:03:1E:29").Vendor.Should().Be("WESTERN DIGITAL CORPORATION", "a device without a stored vendor is resolved from its MAC on the fly");
        all.Single(d => d.Mac.StartsWith("06:29:95")).Vendor.Should().Be(SwitchPortFinder.Domain.OuiLookup.PrivateMac);
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=randomized", Json))!.Items.Should().Contain(d => d.Mac.StartsWith("06:29:95"));
    }

    [Fact]
    public async Task Global_search_also_finds_switches_by_location_and_model_and_things_by_vlan()
    {
        var r = await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "VND-SW-" + Guid.NewGuid().ToString("N")[..6], ManagementIp = "203.0.113.150", Location = "Building Zulu, Rack 9", Model = "WS-C9999-ZZ", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created);
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=Zulu", Json))!.Switches.Should().NotBeEmpty("location is searchable");
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=C9999-ZZ", Json))!.Switches.Should().NotBeEmpty("model is searchable");
        ((int)(await _http.GetAsync("/api/search?q=vlan%2050")).StatusCode).Should().Be(200);
    }
}

[Collection("api")]
public class SwitchUptimeApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Uptime_lists_core_switches_first_then_the_most_recently_restarted_and_hides_unreachable_estimates()
    {
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            db.Switches.RemoveRange(db.Switches.Where(s => s.Name.StartsWith("UP-")));
            foreach (var other in db.Switches.Where(s => !s.Name.StartsWith("UP-"))) { other.UptimeSeconds = null; other.UptimeAt = null; }   // other tests scan switches too; the endpoint is capped at 20 rows
            var t = DateTime.UtcNow;
            SwitchPortFinder.Domain.Entities.Switch S(string n, int ip, long up, bool core = false, SwitchStatus st = SwitchStatus.Reachable) => new() { Name = n, ManagementIp = $"203.0.113.{ip}", UseAsArpSource = core, Status = st, UptimeSeconds = up, UptimeAt = t };
            db.Switches.AddRange(S("UP-old", 211, 400 * 86400L), S("UP-new", 212, 3600), S("UP-core", 213, 90 * 86400L, core: true), S("UP-mid", 214, 10 * 86400L), S("UP-down", 215, 99 * 86400L, st: SwitchStatus.Unreachable));
            await db.SaveChangesAsync();
        }
        var r = (await _http.GetFromJsonAsync<SwitchUptimeResponse>("/api/metrics/switch-uptime?n=20", Json))!;
        var mine = r.Items.Where(i => i.Name.StartsWith("UP-")).ToList();
        mine.Select(i => i.Name).Should().Equal("UP-core", "UP-new", "UP-mid", "UP-old", "UP-down");   // core first, then lowest uptime, unknown last
        mine[0].IsCore.Should().BeTrue(); mine[0].UptimeSeconds.Should().BeInRange(90 * 86400L, 90 * 86400L + 60);
        mine.Single(i => i.Name == "UP-down").UptimeSeconds.Should().BeNull("an unreachable switch has no trustworthy uptime");
        (await _http.GetFromJsonAsync<SwitchUptimeResponse>("/api/metrics/switch-uptime?n=2", Json))!.Items.Should().HaveCount(2);
        (await _http.GetFromJsonAsync<SwitchUptimeResponse>("/api/metrics/switch-uptime?n=99999", Json))!.Items.Count.Should().BeLessThanOrEqualTo(20);
    }
}

[Collection("api")]
public class PortsListApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Ports_list_filters_by_status_type_search_and_pages()
    {
        int sw;
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            var s = new SwitchPortFinder.Domain.Entities.Switch { Name = "PL-SW-" + Guid.NewGuid().ToString("N")[..5], ManagementIp = "203.0.113.230" }; db.Switches.Add(s); await db.SaveChangesAsync(); sw = s.Id;
            SwitchPortFinder.Domain.Entities.SwitchPort P(int i, bool up, PortType t, string? alias = null) => new() { SwitchId = s.Id, IfIndex = i, Name = $"Gi7/0/{i}", OperUp = up, PortType = t, SpeedMbps = 1000, Alias = alias, Vlan = 70 };
            db.SwitchPorts.AddRange(P(1, true, PortType.Endpoint), P(2, true, PortType.SwitchUplink, "to-core"), P(3, false, PortType.Down), P(4, true, PortType.Endpoint));
            await db.SaveChangesAsync();
        }
        var up = (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&status=up", Json))!; up.Total.Should().Be(3);
        (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&type=SwitchUplink", Json))!.Items.Should().ContainSingle(p => p.Name == "Gi7/0/2");
        (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&status=down", Json))!.Total.Should().Be(1);
        (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&q=to-core", Json))!.Total.Should().Be(1);
        (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&q=70", Json))!.Total.Should().Be(4, "a number also matches the VLAN");
        var p1 = (await _http.GetFromJsonAsync<Paged<PortListItem>>($"/api/ports?switchId={sw}&pageSize=2&page=2", Json))!; p1.Items.Should().HaveCount(2); p1.Total.Should().Be(4);
        foreach (var bad in new[] { "q=%27%20OR%201%3D1--", "q=" + new string('A', 5000), "type=Bogus", "status=%00", "pageSize=-5&page=-1", "sort=%3B%20DROP%20TABLE" })
            ((int)(await _http.GetAsync("/api/ports?" + bad)).StatusCode).Should().BeLessThan(500, bad);
    }
}

[Collection("api")]
public class ManualDeviceApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static int _n;

    [Fact]
    public async Task A_device_can_be_added_by_hand_with_a_location_and_deleted_again()
    {
        int swId, portId;
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            var sw = new SwitchPortFinder.Domain.Entities.Switch { Name = "MD-SW-" + Guid.NewGuid().ToString("N")[..5], ManagementIp = "203.0.113.240" }; db.Switches.Add(sw); await db.SaveChangesAsync();
            var p = new SwitchPortFinder.Domain.Entities.SwitchPort { SwitchId = sw.Id, IfIndex = 5, Name = "Gi5/0/5", OperUp = true, PortType = PortType.Endpoint, Vlan = 20 }; db.SwitchPorts.Add(p); await db.SaveChangesAsync(); swId = sw.Id; portId = p.Id;
        }
        var mac = $"1866DA00{Interlocked.Increment(ref _n):X4}"[..12];
        (await _http.PostAsJsonAsync("/api/devices", new ManualDeviceRequest("not-a-mac", "x", null, null, null, null, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.PostAsJsonAsync("/api/devices", new ManualDeviceRequest("01:00:5E:00:00:01", "multicast", null, null, null, null, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var r = await _http.PostAsJsonAsync("/api/devices", new ManualDeviceRequest(mac, "CNC machine controller", "10.20.30.40", "Workshop", "Unmanaged switch behind it", swId, portId, null), Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var d = (await r.Content.ReadFromJsonAsync<DeviceDetailDto>(Json))!.Device;
        (d.Hostname, d.Ip, d.Manual, d.Pinned, d.PortName, d.Vlan, d.Vendor).Should().Be(("CNC machine controller", "10.20.30.40", true, true, "Gi5/0/5", 20, "Dell Inc."));
        (await _http.PostAsJsonAsync("/api/devices", new ManualDeviceRequest(mac, "dup", null, null, null, null, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=CNC%20machine", Json))!.Devices.Should().ContainSingle(x => x.Id == d.Id);
        (await _http.DeleteAsync($"/api/devices/{d.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync($"/api/devices/{d.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}

[Collection("api")]
public class DeviceOnlineStateApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Status_follows_the_last_scan_not_the_clock_in_the_list_filters_search_and_dashboard()
    {
        var scanAt = DateTime.UtcNow.AddDays(-3);   // the switch was scanned three days ago and nobody has scanned since
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SwitchPortFinder.Infrastructure.Persistence.AppDbContext>();
            foreach (var other in db.Switches) other.LastScanAt = null;   // isolate from other tests: only this switch has scanned
            db.Devices.RemoveRange(db.Devices.Where(d => d.Hostname.StartsWith("ONL-"))); db.Switches.RemoveRange(db.Switches.Where(s => s.Name == "ONLINE-SW"));
            var sw = new SwitchPortFinder.Domain.Entities.Switch { Name = "ONLINE-SW", ManagementIp = "198.18.70.1", LastScanAt = scanAt }; db.Switches.Add(sw); await db.SaveChangesAsync();
            SwitchPortFinder.Domain.Entities.Device D(string n, string mac, DateTime seen, int? swId) => new() { Mac = mac, Hostname = n, FirstSeen = seen, LastSeen = seen, SwitchId = swId };
            db.Devices.AddRange(D("ONL-found", "AA00000000A1", scanAt, sw.Id),                          // found by that scan
                                D("ONL-missed", "AA00000000A2", scanAt.AddMinutes(-45), sw.Id),         // not found by that scan
                                D("ONL-ancient", "AA00000000A3", scanAt.AddDays(-30), sw.Id),           // not found for a month of scans
                                D("ONL-behind-uplink", "AA00000000A4", scanAt, null),                   // located nowhere: compared with the newest scan anywhere
                                D("ONL-behind-uplink-missed", "AA00000000A5", scanAt.AddHours(-3), null));
            await db.SaveChangesAsync();
        }
        async Task<List<string>> Names(string state) => (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?q=ONL-&state={state}&pageSize=50", Json))!.Items.Select(d => d.Hostname).OrderBy(x => x).ToList();
        (await Names("Active")).Should().Equal("ONL-behind-uplink", "ONL-found");
        (await Names("Offline")).Should().Equal("ONL-behind-uplink-missed", "ONL-missed");
        (await Names("Stale")).Should().Equal("ONL-ancient");
        var all = (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=ONL-&pageSize=50", Json))!.Items.ToDictionary(d => d.Hostname, d => d.State);
        (all["ONL-found"], all["ONL-missed"], all["ONL-ancient"]).Should().Be((DeviceState.Active, DeviceState.Offline, DeviceState.Stale));
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=ONL-found", Json))!.Devices.Single().State.Should().Be(DeviceState.Active);
        (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{(await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=ONL-found", Json))!.Items.Single().Id}", Json))!.Device.State.Should().Be(DeviceState.Active);
        var dash = (await _http.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json))!;
        dash.OfflineDevices.Should().BeGreaterThanOrEqualTo(3, "missed + behind-uplink-missed + ancient");
    }
}
