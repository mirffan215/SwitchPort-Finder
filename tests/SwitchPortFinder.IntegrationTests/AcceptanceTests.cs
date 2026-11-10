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

/// <summary>
/// The acceptance topology from the spec:  CORE-SW-01 (10.10.50.10, ARP source) —Gi1/0/24— ACCESS-SW-01 (10.10.50.11) —Gi1/0/15— PC-001 10.10.50.101 AA:BB:CC:DD:EE:01
/// Each fixture has its own database, so the fixed IPs/MACs never collide.
/// </summary>
public abstract class AcceptanceBase(IsolatedApiFixture fx)
{
    protected readonly IsolatedApiFixture Fx = fx;
    protected readonly HttpClient Http = fx.CreateClient();
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    protected const string CoreIp = "10.10.50.10", AccessIp = "10.10.50.11", PcIp = "10.10.50.101", PcMac = "AABBCCDDEE01";
    protected const string CoreBase = "AABBCC000A01", CoreG24 = "AABBCC000A18", AccessBase = "AABBCC000B01", AccessG24 = "AABBCC000B18";

    /// <summary>Builds the scripted network. <paramref name="lldp"/> = false simulates LLDP/CDP disabled on every switch.</summary>
    protected void BuildNetwork(bool lldp)
    {
        var net = Fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Net;
        net.Clear();
        net.WithSystem(CoreIp, "CORE-SW-01", "1.3.6.1.4.1.9.1.2134").WithSystem(AccessIp, "ACCESS-SW-01", "1.3.6.1.4.1.9.1.2135");
        // ---- CORE ----
        net.Gets[$"{CoreIp}|{Oids.IpForwarding}"] = Int(1);
        net.Walk(CoreIp, "pub", Oids.IfTable, IfTable((24, "Gi1/0/24", 6, true, CoreG24)));
        net.Walk(CoreIp, "pub", Oids.IfXTable, IfXTable((24, "Gi1/0/24", 1000, "to access")));
        net.Walk(CoreIp, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((24, 24)));
        // the core's Gi1/0/24 has learned the PC AND the access switch's own MAC (it sits behind that port)
        net.Walk(CoreIp, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((50, PcMac, 24, 3), (50, AccessG24, 24, 3)));
        net.Walk(CoreIp, "pub", Oids.IpNetToPhysicalTable, ArpPhysical((24, PcIp, PcMac, 1)));
        // ---- ACCESS ----
        net.Walk(AccessIp, "pub", Oids.IfTable, IfTable((15, "Gi1/0/15", 6, true, null), (24, "Gi1/0/24", 6, true, AccessG24)));
        net.Walk(AccessIp, "pub", Oids.IfXTable, IfXTable((15, "Gi1/0/15", 1000, "PC-001"), (24, "Gi1/0/24", 1000, "uplink")));
        net.Walk(AccessIp, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((15, 15), (24, 24)));
        net.Walk(AccessIp, "pub", Oids.Dot1qPvid, [Vb($"{Oids.Dot1qPvid}.15", new SnmpValue(SnmpValueKind.Gauge, 50))]);
        net.Walk(AccessIp, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((50, PcMac, 15, 3), (50, CoreG24, 24, 3)));
        if (lldp)
        {
            net.Gets[$"{CoreIp}|{Oids.LldpLocChassisIdSubtype}"] = Int(4); net.Gets[$"{CoreIp}|{Oids.LldpLocChassisId}"] = Bytes(MacBytes(CoreBase));
            net.Gets[$"{AccessIp}|{Oids.LldpLocChassisIdSubtype}"] = Int(4); net.Gets[$"{AccessIp}|{Oids.LldpLocChassisId}"] = Bytes(MacBytes(AccessBase));
            net.Walk(CoreIp, "pub", Oids.LldpLocPortTable, LldpLocPort(24, 5, "Gi1/0/24"));
            net.Walk(CoreIp, "pub", Oids.LldpRemTable, LldpRem(24, 1, 4, MacBytes(AccessBase), 5, System.Text.Encoding.UTF8.GetBytes("Gi1/0/24"), "ACCESS-SW-01", caps: 0x20));
            net.Walk(CoreIp, "pub", Oids.LldpRemManAddrTable, LldpMan(24, 1, AccessIp));
            net.Walk(AccessIp, "pub", Oids.LldpLocPortTable, LldpLocPort(24, 5, "Gi1/0/24"));
            net.Walk(AccessIp, "pub", Oids.LldpRemTable, LldpRem(24, 1, 4, MacBytes(CoreBase), 5, System.Text.Encoding.UTF8.GetBytes("Gi1/0/24"), "CORE-SW-01", caps: 0x28));
            net.Walk(AccessIp, "pub", Oids.LldpRemManAddrTable, LldpMan(24, 1, CoreIp));
        }
        Fx.Services.GetService(typeof(FakeResolver)).As<FakeResolver>().Names[PcIp] = "PC-001";
    }

    /// <summary>Each theory case starts from an empty switch list (also exercises delete + re-classification).</summary>
    protected async Task Reset()
    {
        foreach (var s in (await Http.GetFromJsonAsync<List<SwitchSummaryDto>>("/api/switches", Json))!)
            (await Http.DeleteAsync($"/api/switches/{s.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    protected async Task<int> AddSwitch(string name, string ip, bool arp = false)
    {
        var r = await Http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = name, ManagementIp = ip, UseAsArpSource = arp, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "pub" } }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<SwitchDetailDto>(Json))!.Id;
    }

    protected async Task<JobDetailDto> Scan(int id)
    {
        var jobId = (await (await Http.PostAsync($"/api/switches/{id}/scan", null)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetInt32();
        for (var i = 0; i < 200; i++)
        {
            var d = (await Http.GetFromJsonAsync<JobDetailDto>($"/api/discovery/jobs/{jobId}", Json))!;
            if (d.Job.Status is not (JobStatus.Queued or JobStatus.Running)) return d;
            await Task.Delay(50);
        }
        throw new TimeoutException();
    }

    protected Task<PortsResponse> Ports(int id) => Http.GetFromJsonAsync<PortsResponse>($"/api/switches/{id}/ports", Json)!;

    /// <summary>The spec's acceptance assertions (those that do not need search/trace, which arrive in Phase 4).</summary>
    protected async Task AssertAcceptance(int core, int access, NeighborConfidence expected)
    {
        // CORE-SW-01: Gi1/0/24 is a Switch Uplink → ACCESS-SW-01 (10.10.50.11) with Open Switch, and does NOT list PC-001.
        var coreUp = (await Ports(core)).Ports.Single(p => p.Name == "Gi1/0/24");
        coreUp.Type.Should().Be(PortType.SwitchUplink);
        coreUp.Devices.Should().BeEmpty("an uplink never lists downstream devices (5.8)");
        coreUp.Neighbor.Should().NotBeNull();
        coreUp.Neighbor!.ConnectedSwitchId.Should().Be(access, "Open Switch navigates to the neighbor's page");
        coreUp.Neighbor.ConnectedSwitchName.Should().Be("ACCESS-SW-01"); coreUp.Neighbor.ConnectedSwitchIp.Should().Be(AccessIp);
        coreUp.Neighbor.Confidence.Should().Be(expected);
        coreUp.Neighbor.CanAdd.Should().BeFalse("the neighbor is already registered");
        (await Ports(core)).Summary.Uplinks.Should().Be(1);

        // ACCESS-SW-01: Gi1/0/15 is an Endpoint: PC-001, 10.10.50.101, AA:BB:CC:DD:EE:01.
        var edge = (await Ports(access)).Ports.Single(p => p.Name == "Gi1/0/15");
        edge.Type.Should().Be(PortType.Endpoint);
        edge.Devices.Should().ContainSingle().Which.Should().Match<PortDeviceDto>(d => d.Hostname == "PC-001" && d.Ip == PcIp && d.Mac == "AA:BB:CC:DD:EE:01");
        edge.Neighbor.Should().BeNull();

        // The access switch's own uplink points back at the core.
        var accUp = (await Ports(access)).Ports.Single(p => p.Name == "Gi1/0/24");
        accUp.Type.Should().Be(PortType.SwitchUplink); accUp.Devices.Should().BeEmpty();
        accUp.Neighbor!.ConnectedSwitchId.Should().Be(core); accUp.Neighbor.ConnectedSwitchName.Should().Be("CORE-SW-01");

        // The PC's authoritative location is the edge port, with full confidence now that the core port is known to be an uplink.
        var devs = (await Http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?pageSize=100", Json))!;
        var pc = devs.Items.Should().ContainSingle(d => d.Mac == "AA:BB:CC:DD:EE:01").Subject;
        pc.SwitchName.Should().Be("ACCESS-SW-01"); pc.PortName.Should().Be("Gi1/0/15"); pc.LowConfidence.Should().BeFalse(); pc.Ip.Should().Be(PcIp); pc.Hostname.Should().Be("PC-001");
        devs.Items.Should().NotContain(d => d.Mac == "AA:BB:CC:00:0B:18" || d.Mac == "AA:BB:CC:00:0A:18", "switches are not devices");

        // Searching each identifier locates ACCESS-SW-01 Gi1/0/15 (spec 15).
        foreach (var q in new[] { "AA:BB:CC:DD:EE:01", "aabb.ccdd.ee01", "10.10.50.101", "PC-001" })
        {
            var found = (await Http.GetFromJsonAsync<SearchResponse>($"/api/search?q={Uri.EscapeDataString(q)}", Json))!;
            var hit = found.Devices.Should().ContainSingle(d => d.Mac == "AA:BB:CC:DD:EE:01", $"query '{q}'").Subject;
            hit.SwitchName.Should().Be("ACCESS-SW-01", q); hit.PortName.Should().Be("Gi1/0/15", q); hit.IfIndex.Should().Be(15, q); hit.SwitchId.Should().Be(access, q);
            hit.Ip.Should().Be(PcIp); hit.Vlan.Should().Be(50); hit.State.Should().Be(DeviceState.Active);
        }

        // Trace: CORE-SW-01 —Gi1/0/24→ ACCESS-SW-01 —Gi1/0/15→ PC-001.
        var trace = (await Http.GetFromJsonAsync<TraceResult>($"/api/devices/{pc.Id}/trace", Json))!;
        trace.Partial.Should().BeFalse(trace.Message);
        trace.Hops.Select(h => (h.SwitchName, h.PortName, h.IsFinal)).Should().Equal(("CORE-SW-01", "Gi1/0/24", false), ("ACCESS-SW-01", "Gi1/0/15", true));
        trace.Hops[0].LinkConfidence.Should().Be(expected);
        trace.Final.Should().Match<FinalLocation>(f => f.Hostname == "PC-001" && f.Ip == PcIp && f.SwitchName == "ACCESS-SW-01" && f.PortName == "Gi1/0/15" && f.Vlan == 50 && f.Mac == "AA:BB:CC:DD:EE:01");

        // Device details.
        var detail = (await Http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{pc.Id}", Json))!;
        detail.IpSourceSwitch.Should().Be("CORE-SW-01", "the IP came from the core's ARP table");
        detail.Sightings.Should().HaveCount(2).And.Contain(x => x.SwitchName == "CORE-SW-01" && x.IsUplink).And.Contain(x => x.SwitchName == "ACCESS-SW-01" && !x.IsUplink);
    }
}

public class AcceptanceWithLldpTests(IsolatedApiFixture fx) : AcceptanceBase(fx), IClassFixture<IsolatedApiFixture>
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Topology_is_classified_via_LLDP_as_Confirmed_Switch_in_any_scan_order(bool coreFirst)
    {
        await Reset(); BuildNetwork(lldp: true);
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); var access = await AddSwitch("ACCESS-SW-01", AccessIp);
        var first = await Scan(coreFirst ? core : access); var second = await Scan(coreFirst ? access : core);
        first.Steps.Single(s => s.Name == Steps.Lldp).State.Should().Be(StepState.Ok);
        second.Job.NeighborsFound.Should().Be(1);
        await AssertAcceptance(core, access, NeighborConfidence.ConfirmedSwitch);
        var nb = (await Http.GetFromJsonAsync<List<NeighborRowDto>>($"/api/switches/{core}/neighbors", Json))!.Single();
        nb.Neighbor.Protocol.Should().Be(NeighborProtocol.Lldp); nb.Neighbor.RemotePort.Should().Be("Gi1/0/24"); nb.Neighbor.RemotePortIfIndex.Should().Be(24, "so Open Switch can highlight the remote port");
        nb.Neighbor.Capabilities.Should().Contain("Bridge"); nb.Neighbor.RemoteIp.Should().Be(AccessIp);
    }
}

public class SearchApiTests(IsolatedApiFixture fx) : AcceptanceBase(fx), IClassFixture<IsolatedApiFixture>
{
    private async Task<SearchResponse> S(string q) => (await Http.GetFromJsonAsync<SearchResponse>($"/api/search?q={Uri.EscapeDataString(q)}", Json))!;

    [Fact]
    public async Task Search_covers_devices_switches_and_ports_and_is_safe_and_case_insensitive()
    {
        await Reset(); BuildNetwork(lldp: true);
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); var access = await AddSwitch("ACCESS-SW-01", AccessIp);
        await Scan(core); await Scan(access);

        // switch name (any case) and switch IP
        (await S("core-sw")).Switches.Should().ContainSingle(s => s.Name == "CORE-SW-01");
        (await S("10.10.50.11")).Switches.Should().ContainSingle(s => s.ManagementIp == AccessIp);
        // switch name also finds the devices connected to that switch
        (await S("access-sw")).Devices.Should().ContainSingle(d => d.Hostname == "PC-001");
        // port name: one hit per switch, with enough detail to open it
        var ports = (await S("Gi1/0/24")).Ports;
        ports.Should().HaveCount(2).And.OnlyContain(p => p.Type == PortType.SwitchUplink && p.PortName == "Gi1/0/24");
        (await S("Gi1/0/15")).Ports.Should().ContainSingle().Which.Should().Match<PortHit>(p => p.SwitchName == "ACCESS-SW-01" && p.IfIndex == 15 && p.SwitchId == access);
        // partial MACs (>= 4 hex chars), any separator style
        (await S("aabb.ccdd")).Devices.Should().ContainSingle(d => d.Mac == "AA:BB:CC:DD:EE:01");
        (await S("AA-BB-CC-DD")).Devices.Should().ContainSingle(d => d.Mac == "AA:BB:CC:DD:EE:01");
        (await S("aab")).Devices.Should().BeEmpty("3 hex characters is too short to be a MAC fragment, and no text matches");
        // partial IP: exact hit is ranked first
        (await S("10.10.50.10")).Devices.Should().ContainSingle(d => d.Ip == PcIp);
        // an IP fragment must not be misread as a MAC fragment ('10.10.50' → hex 101050)
        (await S("10.10.50")).Devices.Should().OnlyContain(d => d.Ip!.StartsWith("10.10.50"));
        // no match, too short, and LIKE wildcards are treated literally
        (await S("no-such-thing")).Devices.Should().BeEmpty();
        (await S("a")).Devices.Should().BeEmpty();
        foreach (var wild in new[] { "%", "_", "%%", "[a-z]", "\\", "' OR 1=1 --" })
        { var r = await S(wild); r.Devices.Should().BeEmpty(wild); r.Switches.Should().BeEmpty(wild); r.Ports.Should().BeEmpty(wild); }
        // empty query is a clean empty result, not an error
        (await Http.GetAsync("/api/search?q=")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Http.GetAsync("/api/search")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Device_details_and_trace_for_unknown_ids_are_404()
    {
        (await Http.GetAsync("/api/devices/999999")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Http.GetAsync("/api/devices/999999/trace")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_device_seen_only_behind_an_uplink_has_no_location_and_the_trace_says_so()
    {
        await Reset(); BuildNetwork(lldp: true);
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true);
        await Scan(core);   // access switch is NOT registered: PC is only known behind the core's uplink
        var coreUp = (await Ports(core)).Ports.Single(p => p.Name == "Gi1/0/24");
        coreUp.Type.Should().Be(PortType.SwitchUplink, "LLDP says an (unregistered) bridge is on this port"); coreUp.Neighbor!.CanAdd.Should().BeTrue("external switch → Add this switch");
        var pc = (await S("PC-001")).Devices.Single();
        pc.SwitchId.Should().BeNull("an uplink is never a device's location");
        var trace = (await Http.GetFromJsonAsync<TraceResult>($"/api/devices/{pc.Id}/trace", Json))!;
        trace.Partial.Should().BeTrue(); trace.Final.Should().BeNull(); trace.Hops.Should().BeEmpty(); trace.Message.Should().Contain("not been seen on an edge port");
    }
}

public class AcceptanceWithoutLldpTests(IsolatedApiFixture fx) : AcceptanceBase(fx), IClassFixture<IsolatedApiFixture>
{
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task With_LLDP_disabled_the_FDB_heuristic_yields_a_Likely_Switch_uplink_in_any_scan_order(bool coreFirst)
    {
        await Reset(); BuildNetwork(lldp: false);
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); var access = await AddSwitch("ACCESS-SW-01", AccessIp);
        var first = await Scan(coreFirst ? core : access); await Scan(coreFirst ? access : core);
        var lldpStep = first.Steps.Single(s => s.Name == Steps.Lldp);
        lldpStep.State.Should().Be(StepState.Warning); lldpStep.Message.Should().Be("LLDP unavailable. Continuing with MAC and interface discovery...");
        first.Errors.Should().Contain(e => e.Code == "OidUnavailable" && e.Step == Steps.Lldp);
        first.Job.Status.Should().Be(JobStatus.CompletedWithWarnings, "missing LLDP is a warning, never a failure");
        await AssertAcceptance(core, access, NeighborConfidence.LikelySwitch);
        (await Http.GetFromJsonAsync<List<NeighborRowDto>>($"/api/switches/{core}/neighbors", Json))!.Single().Neighbor.Protocol.Should().Be(NeighborProtocol.Inferred);
    }
}

public class Phase5ApiTests(IsolatedApiFixture fx) : AcceptanceBase(fx), IClassFixture<IsolatedApiFixture>
{
    private async Task<(int core, int access)> Setup(bool lldp = true, string? pcHostname = null)
    {
        await Reset(); BuildNetwork(lldp);
        if (pcHostname is not null) Fx.Services.GetService(typeof(FakeResolver)).As<FakeResolver>().Names[PcIp] = pcHostname;
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); var access = await AddSwitch("ACCESS-SW-01", AccessIp);
        await Scan(core); await Scan(access);
        return (core, access);
    }

    [Theory]
    [InlineData(true, NeighborConfidence.ConfirmedSwitch)] [InlineData(false, NeighborConfidence.LikelySwitch)]
    public async Task Topology_shows_one_paired_edge_between_the_switches_rooted_at_the_core(bool lldp, NeighborConfidence expected)
    {
        var (core, access) = await Setup(lldp);
        var t = (await Http.GetFromJsonAsync<TopologyResponse>("/api/topology", Json))!;
        t.Nodes.Should().HaveCount(2);
        var c = t.Nodes.Single(n => n.SwitchId == core); var a = t.Nodes.Single(n => n.SwitchId == access);
        (c.Level, c.IsRoot, a.Level, a.IsRoot).Should().Be((0, true, 1, false), "the ARP-source core is the root");
        c.Uplinks.Should().Be(1); a.Devices.Should().Be(1); a.Name.Should().Be("ACCESS-SW-01"); a.Status.Should().Be("Reachable");
        var e = t.Edges.Should().ContainSingle("A→B and B→A are one link").Subject;
        (e.SourceSwitchId, e.TargetSwitchId, e.SourcePort, e.TargetPort).Should().Be((core, access, "Gi1/0/24", "Gi1/0/24"));
        e.Confidence.Should().Be(expected);
        e.SourceIfIndex.Should().Be(24, "so clicking the link can open the port details");
    }

    [Fact]
    public async Task Topology_draws_an_unregistered_neighbor_as_an_external_node()
    {
        await Reset(); BuildNetwork(true);
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); await Scan(core);   // ACCESS-SW-01 is not registered
        var t = (await Http.GetFromJsonAsync<TopologyResponse>("/api/topology", Json))!;
        t.Nodes.Should().HaveCount(2);
        var ghost = t.Nodes.Single(n => n.External); ghost.Name.Should().Be("ACCESS-SW-01"); ghost.SwitchId.Should().BeNull(); ghost.Ip.Should().Be(AccessIp);
        t.Edges.Should().ContainSingle().Which.TargetSwitchId.Should().BeNull();
    }

    [Fact]
    public async Task Dashboard_counts_recent_jobs_and_recently_discovered_devices()
    {
        var (_, _) = await Setup();
        var d = (await Http.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json))!;
        (d.Switches, d.Devices, d.ConnectedPorts, d.SwitchUplinks, d.OfflineDevices).Should().Be((2, 1, 3, 2, 0));
        d.LastDiscovery.Should().NotBeNull(); d.LastDiscovery!.Status.Should().BeOneOf(JobStatus.Completed, JobStatus.CompletedWithWarnings);
        d.RecentJobs.Should().HaveCount(2).And.OnlyContain(j => j.CompletedAt != null);
        d.RecentDevices.Should().ContainSingle().Which.Hostname.Should().Be("PC-001");
    }

    [Fact]
    public async Task Dashboard_is_clean_when_nothing_exists_yet()
    {
        await Reset();
        var d = (await Http.GetFromJsonAsync<DashboardDto>("/api/dashboard", Json))!;
        (d.Switches, d.ConnectedPorts, d.SwitchUplinks).Should().Be((0, 0, 0)); d.LastDiscovery.Should().BeNull(); d.RecentJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Csv_export_has_the_spec_columns_and_respects_filters_and_sorting()
    {
        await Setup();
        var all = await Http.GetAsync("/api/export/devices?format=csv");
        all.StatusCode.Should().Be(HttpStatusCode.OK); all.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        all.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".csv");
        var rows = SwitchPortFinder.Application.Import.Csv.Parse((await all.Content.ReadAsStringAsync()).TrimStart('﻿'), ',');
        rows[0].Should().Equal("Switch", "Port", "MAC", "IP", "Hostname", "VLAN", "Status", "Last Seen", "Manufacturer");
        rows.Should().HaveCount(2);
        rows[1].Take(7).Should().Equal("ACCESS-SW-01", "Gi1/0/15", "AA:BB:CC:DD:EE:01", PcIp, "PC-001", "50", "Active");

        async Task<int> Count(string qs) => SwitchPortFinder.Application.Import.Csv.Parse((await Http.GetStringAsync($"/api/export/devices?format=csv&{qs}")).TrimStart('﻿'), ',').Count - 1;
        (await Count("q=PC-001")).Should().Be(1);
        (await Count("q=aabb.ccdd.ee01")).Should().Be(1, "same MAC matching as search");
        (await Count("q=nothing-matches")).Should().Be(0, "a filter with no hits exports just the header row");
        (await Count("state=Stale")).Should().Be(0);
        (await Count("state=Active")).Should().Be(1);
        (await Count("switchId=999999")).Should().Be(0);
        (await Count("pageSize=1&page=2")).Should().Be(1, "paging parameters are ignored: the export is never paged");
    }

    [Fact]
    public async Task Discovery_job_list_can_be_filtered_by_switch_and_records_neighbors_found()
    {
        var (core, _) = await Setup();
        var jobs = (await Http.GetFromJsonAsync<List<JobSummaryDto>>($"/api/discovery/jobs?switchId={core}", Json))!;
        jobs.Should().ContainSingle().Which.Should().Match<JobSummaryDto>(j => j.NeighborsFound == 1 && j.DurationSeconds != null && j.PortsFound == 1);
    }
}

/// <summary>Own database: the hostname must be resolved for the first time with the hostile DNS answer.</summary>
public class ExportInjectionTests(IsolatedApiFixture fx) : AcceptanceBase(fx), IClassFixture<IsolatedApiFixture>
{
    [Fact]
    public async Task Excel_export_opens_and_formula_looking_hostnames_stay_text()
    {
        await Reset(); BuildNetwork(true);
        Fx.Services.GetService(typeof(FakeResolver)).As<FakeResolver>().Names[PcIp] = "=HYPERLINK(\"http://evil\",\"x\")";   // a hostile reverse-DNS answer
        var core = await AddSwitch("CORE-SW-01", CoreIp, arp: true); var access = await AddSwitch("ACCESS-SW-01", AccessIp);
        await Scan(core); await Scan(access);
        var resp = await Http.GetAsync("/api/export/devices?format=xlsx");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        using var wb = new ClosedXML.Excel.XLWorkbook(new MemoryStream(await resp.Content.ReadAsByteArrayAsync()));
        var ws = wb.Worksheet(1);
        ws.Cell(1, 1).GetString().Should().Be("Switch"); ws.Cell(2, 1).GetString().Should().Be("ACCESS-SW-01"); ws.Cell(2, 3).GetString().Should().Be("AA:BB:CC:DD:EE:01");
        ws.Cell(2, 5).HasFormula.Should().BeFalse(); ws.Cell(2, 5).GetString().Should().StartWith("=HYPERLINK");
        // CSV variant is neutralised with a leading apostrophe.
        var csv = SwitchPortFinder.Application.Import.Csv.Parse((await Http.GetStringAsync("/api/export/devices?format=csv")).TrimStart('﻿'), ',');
        csv[1][4].Should().StartWith("'=HYPERLINK");
    }

}
