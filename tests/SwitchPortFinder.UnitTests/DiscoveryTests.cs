using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery;
using SwitchPortFinder.Discovery.Parsing;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.TestSupport;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.UnitTests;

public class MacAddressTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:FF")] [InlineData("aa-bb-cc-dd-ee-ff")] [InlineData("AABB.CCDD.EEFF")] [InlineData("aabbccddeeff")] [InlineData("  aaBB.ccDD.eeFF ")]
    public void All_formats_normalise_to_the_same_value(string input) => MacAddress.Normalize(input).Should().Be("AABBCCDDEEFF");

    [Theory] [InlineData("AA:BB:CC:DD:EE")] [InlineData("AA:BB:CC:DD:EE:FF:00")] [InlineData("GG:BB:CC:DD:EE:FF")] [InlineData("")] [InlineData(null)]
    public void Invalid_macs_return_null(string? input) => MacAddress.Normalize(input).Should().BeNull();

    [Fact] public void Display_format_is_colon_separated() => MacAddress.Format("AABBCCDDEE01").Should().Be("AA:BB:CC:DD:EE:01");

    [Theory]
    [InlineData("aabb", "AABB")] [InlineData("AA:BB:CC", "AABBCC")] [InlineData("aabb.ccdd", "AABBCCDD")] [InlineData("aab", null)] [InlineData("zzzz", null)] [InlineData("PC-001", null)]
    public void Partial_search_keys(string input, string? expected) => MacAddress.PartialKey(input).Should().Be(expected);

    [Fact] public void Multicast_and_broadcast_are_detected()
    {
        MacAddress.IsMulticastOrBroadcast("FFFFFFFFFFFF").Should().BeTrue();
        MacAddress.IsMulticastOrBroadcast("01005E000001").Should().BeTrue();
        MacAddress.IsMulticastOrBroadcast("AABBCCDDEE01").Should().BeFalse();
    }
}

public class ParserTests
{
    [Fact]
    public void Interfaces_prefer_ifXTable_name_and_highspeed_and_keep_only_ethernet_types()
    {
        var ifs = InterfaceParser.Parse(
            IfTable((1, "GigabitEthernet1/0/1", 6, true, "AABBCCDD0001"), (2, "Vlan10", 53, true, null), (3, "lo", 24, true, null), (4, "Gi1/0/2", 6, false, null)),
            IfXTable((1, "Gi1/0/1", 1000, "to PC"), (4, "Gi1/0/2", 0, "")));
        ifs.Select(i => i.IfIndex).Should().Equal(1, 4);
        ifs[0].Name.Should().Be("Gi1/0/1"); ifs[0].Alias.Should().Be("to PC"); ifs[0].SpeedMbps.Should().Be(1000);
        ifs[0].Mac.Should().Be("AABBCCDD0001"); ifs[0].OperUp.Should().BeTrue();
        ifs[1].OperUp.Should().BeFalse(); ifs[1].SpeedMbps.Should().Be(1000, "falls back to ifSpeed (bits/s) when ifHighSpeed is 0");
    }

    [Fact]
    public void Bridge_port_maps_to_ifIndex_not_the_other_way_round()
    {
        var map = FdbParser.BasePortMap(BasePortMap((1, 10101), (2, 10102)));
        map[2].Should().Be(10102);
        var fdb = FdbParser.ParseDot1d(Dot1dFdb(("AABBCCDDEE01", 2, 3)), map, null);
        fdb.Single().IfIndex.Should().Be(10102, "FDB rows hold a bridge port that must be translated via dot1dBasePortIfIndex");
        fdb.Single().BridgePort.Should().Be(2);
    }

    [Fact]
    public void Qbridge_rows_carry_the_vlan_and_map_fdbId_to_vlan_when_known()
    {
        var map = FdbParser.BasePortMap(BasePortMap((5, 7)));
        var rows = FdbParser.ParseDot1q(Dot1qFdb((20, "AABBCCDDEE01", 5, 3), (30, "AABBCCDDEE02", 5, 3)), map, new Dictionary<int, int> { [20] = 200 });
        rows.Single(r => r.Mac == "AABBCCDDEE01").Vlan.Should().Be(200);
        rows.Single(r => r.Mac == "AABBCCDDEE02").Vlan.Should().Be(30, "identity when no dot1qVlanFdbId mapping exists");
    }

    [Fact]
    public void Only_learned_entries_survive_and_self_multicast_and_own_macs_are_dropped()
    {
        var map = FdbParser.BasePortMap(BasePortMap((1, 1), (2, 2)));
        var all = FdbParser.ParseDot1d(Dot1dFdb(
            ("AABBCCDDEE01", 1, 3),   // learned  ✔
            ("AABBCCDDEE02", 1, 4),   // self     ✘
            ("AABBCCDDEE03", 1, 2),   // invalid  ✘
            ("AABBCCDDEE04", 1, 1),   // other    ✘
            ("01005E000001", 1, 3),   // multicast ✘
            ("FFFFFFFFFFFF", 1, 3),   // broadcast ✘
            ("AABBCCDDEE05", 2, 5),   // mgmt/static ✔ (flagged)
            ("AABBCCDDEE06", 99, 3),  // unmappable bridge port ✘
            ("AABBCCDDEE07", 0, 3),   // port 0 = the switch CPU ✘
            ("AABBCCDDEE08", 1, 3)),  // the switch's own interface MAC ✘
            map, null);
        var usable = FdbParser.Usable(all, new HashSet<string> { "AABBCCDDEE08" });
        usable.Select(e => e.Mac).Should().BeEquivalentTo("AABBCCDDEE01", "AABBCCDDEE05");
        usable.Single(e => e.Mac == "AABBCCDDEE05").Kind.Should().Be(FdbKind.Static);
        usable.Single(e => e.Mac == "AABBCCDDEE01").Kind.Should().Be(FdbKind.Learned);
    }

    [Fact]
    public void Arp_physical_table_skips_incomplete_and_invalid()
    {
        var arp = ArpParser.ParsePhysical(ArpPhysical((1, "192.0.2.10", "AABBCCDDEE01", 1), (1, "192.0.2.11", "AABBCCDDEE02", 7), (1, "192.0.2.12", "AABBCCDDEE03", 5), (1, "192.0.2.13", "AABBCCDDEE04", 2)));
        arp.Select(a => a.Ip).Should().Equal("192.0.2.10", "192.0.2.13");
        arp[0].Mac.Should().Be("AABBCCDDEE01");
    }

    [Fact]
    public void Arp_media_fallback_skips_invalid_entries()
    {
        var arp = ArpParser.ParseMedia(ArpMedia((1, "192.0.2.10", "AABBCCDDEE01", 3), (1, "192.0.2.11", "AABBCCDDEE02", 2)));
        arp.Should().ContainSingle().Which.Ip.Should().Be("192.0.2.10");
    }
}

public class TruncatedWalkTests
{
    [Fact]
    public void An_interface_without_ifOperStatus_is_unknown_not_down()
    {
        // ifType/ifDescr present, but the agent's reply was cut off before the ifOperStatus (column 8) rows.
        var rows = IfTable((1, "Gi1/0/1", 6, true, null), (2, "Gi1/0/2", 6, true, null)).Where(v => !(v.Oid.StartsWith(Oids.IfTable + ".8.") && v.Oid.EndsWith(".2")));
        var ifs = InterfaceParser.Parse(rows, []);
        ifs.Single(i => i.IfIndex == 1).OperKnown.Should().BeTrue();
        ifs.Single(i => i.IfIndex == 2).OperKnown.Should().BeFalse();
    }

    [Fact]
    public async Task A_truncated_interface_table_produces_a_warning_and_never_marks_ports_down()
    {
        var s = new ScriptedSnmp().WithSystem("192.0.2.10", "SW", "1.3.6.1.4.1.8072");
        s.Walk("192.0.2.10", "pub", Oids.IfTable, IfTable((1, "Gi1/0/1", 6, true, null)).Where(v => !v.Oid.StartsWith(Oids.IfTable + ".8.")));
        var scanner = new SnmpSwitchScanner(s, Options.Create(new SnmpOptions()), Options.Create(new DiscoveryOptions()), NullLogger<SnmpSwitchScanner>.Instance);
        var r = await scanner.ScanAsync(new ScanRequest("192.0.2.10", new SnmpCredentials { Version = SnmpVersion.V2c, Community = "pub" }, Manufacturer.AutoDetect, false), _ => Task.CompletedTask, default);
        r.Steps.Single(x => x.Name == Steps.Interfaces).State.Should().Be(StepState.Warning);
        r.Errors.Should().Contain(e => e.Code == "Incomplete");
    }
}

public class ScannerTests
{
    private const string Cisco = "1.3.6.1.4.1.9.1.2134";
    private const string Ip = "192.0.2.10";

    private static SnmpSwitchScanner Scanner(ScriptedSnmp s) => new(s, Options.Create(new SnmpOptions()), Options.Create(new DiscoveryOptions()), NullLogger<SnmpSwitchScanner>.Instance);
    private static ScanRequest Req(SnmpCredentials? c = null, Manufacturer m = Manufacturer.AutoDetect, bool arp = false) =>
        new(Ip, c ?? new SnmpCredentials { Version = SnmpVersion.V2c, Community = "pub" }, m, arp);
    private static Task Noop(StepResult _) => Task.CompletedTask;

    private static ScriptedSnmp Base(string objectId = "1.3.6.1.4.1.8072.3.2.10")
    {
        var s = new ScriptedSnmp().WithSystem(Ip, "SW-1", objectId);
        s.Walk(Ip, "pub", Oids.IfTable, IfTable((1, "Gi1/0/1", 6, true, "AABBCCDD0001"), (2, "Gi1/0/2", 6, true, null)));
        s.Walk(Ip, "pub", Oids.IfXTable, IfXTable((1, "Gi1/0/1", 1000, ""), (2, "Gi1/0/2", 1000, "")));
        return s;
    }

    [Fact]
    public async Task Qbridge_is_used_first_and_bridge_port_is_mapped_to_ifIndex()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((11, 1), (12, 2)));
        s.Walk(Ip, "pub", Oids.Dot1qTpFdbTable, Dot1qFdb((10, "AABBCCDDEE01", 12, 3)));
        var r = await Scanner(s).ScanAsync(Req(), Noop, default);
        r.FdbSource.Should().Be("Q-BRIDGE-MIB");
        r.Fdb.Should().ContainSingle().Which.Should().BeEquivalentTo(new FdbEntry("AABBCCDDEE01", 10, 12, 2, FdbKind.Learned));
        s.WalkLog.Should().NotContain(w => w.Root == Oids.Dot1dTpFdbTable, "no fallback needed when Q-BRIDGE has data");
    }

    [Fact]
    public async Task Falls_back_to_bridge_mib_when_qbridge_is_empty()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        var r = await Scanner(s).ScanAsync(Req(), Noop, default);
        r.FdbSource.Should().Be("BRIDGE-MIB"); r.Fdb.Should().ContainSingle().Which.IfIndex.Should().Be(1);
    }

    [Fact]
    public async Task Cisco_queries_each_vlan_with_community_at_vlan_and_maps_ports_per_vlan_instance()
    {
        var s = Base(Cisco);
        // VLANs from the Cisco VTP MIB: 1, 10, 20 operational, 30 not, 1002-1005 are the legacy FDDI/TokenRing VLANs.
        s.Walk(Ip, "pub", Oids.CiscoVtpVlanState, new[] { 1, 10, 20 }.Select(v => Vb($"{Oids.CiscoVtpVlanState}.1.{v}", Int(1)))
            .Concat([Vb($"{Oids.CiscoVtpVlanState}.1.30", Int(2)), Vb($"{Oids.CiscoVtpVlanState}.1.1002", Int(1)), Vb($"{Oids.CiscoVtpVlanState}.1.1005", Int(1))]));
        // Each VLAN instance has its OWN bridge-port numbering.
        s.Walk(Ip, "pub@10", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub@10", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        s.Walk(Ip, "pub@20", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 2)));     // same bridge port number, different ifIndex
        s.Walk(Ip, "pub@20", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE02", 1, 3)));

        var r = await Scanner(s).ScanAsync(Req(), Noop, default);

        r.FdbSource.Should().Be("BRIDGE-MIB (per-VLAN)");
        r.Vlans.Should().Contain([1, 10, 20]);   // discovered list is informational; 1002-1005 are skipped when querying (asserted below)
        r.Fdb.Single(e => e.Mac == "AABBCCDDEE01").Should().Match<FdbEntry>(e => e.Vlan == 10 && e.IfIndex == 1);
        r.Fdb.Single(e => e.Mac == "AABBCCDDEE02").Should().Match<FdbEntry>(e => e.Vlan == 20 && e.IfIndex == 2);
        // Every operational VLAN is probed; VLAN 1 has no data so its FDB is never walked; 1002-1005 and non-operational VLAN 30 are never touched.
        s.WalkLog.Where(w => w.Root == Oids.Dot1dBasePortIfIndex && w.CredKey.Contains('@')).Select(w => w.CredKey).Distinct().Should().BeEquivalentTo("pub@1", "pub@10", "pub@20");
        s.WalkLog.Where(w => w.Root == Oids.Dot1dTpFdbTable).Select(w => w.CredKey).Distinct().Should().BeEquivalentTo("pub@10", "pub@20");
        s.WalkLog.Should().NotContain(w => w.CredKey.EndsWith("@1002") || w.CredKey.EndsWith("@1005") || w.CredKey.EndsWith("@30"));
    }

    [Fact]
    public async Task Cisco_with_snmpv3_uses_context_name_vlan_N()
    {
        var s = Base(Cisco);
        s.Walk(Ip, "", Oids.CiscoVtpVlanState, [Vb($"{Oids.CiscoVtpVlanState}.1.10", Int(1))]);
        s.Walk(Ip, "vlan-10", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "vlan-10", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        var v3 = new SnmpCredentials { Version = SnmpVersion.V3, Username = "u" };
        var r = await Scanner(s).ScanAsync(Req(v3), Noop, default);
        r.Fdb.Should().ContainSingle().Which.Vlan.Should().Be(10);
        s.WalkLog.Should().Contain(w => w.CredKey == "vlan-10" && w.Root == Oids.Dot1dTpFdbTable);
    }

    [Fact]
    public async Task Selected_manufacturer_overrides_detection_for_provider_choice()
    {
        // Device reports a non-Cisco sysObjectID but the admin selected Cisco → per-VLAN behaviour must apply.
        var s = Base("1.3.6.1.4.1.8072.3.2.10");
        s.Walk(Ip, "pub", Oids.Dot1qVlanStaticName, [Vb($"{Oids.Dot1qVlanStaticName}.10", Txt("users"))]);
        s.Walk(Ip, "pub@10", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub@10", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        var r = await Scanner(s).ScanAsync(Req(m: Manufacturer.Cisco), Noop, default);
        r.Fdb.Should().HaveCount(1);
        r.DetectedManufacturer.Should().Be(Manufacturer.OtherGeneric);
        s.WalkLog.Should().NotContain(w => w.Root == Oids.Dot1dTpFdbTable && w.CredKey == "pub", "Cisco provider goes straight to per-VLAN queries");
    }

    [Fact]
    public async Task Non_cisco_with_empty_standard_tables_tries_per_vlan_as_a_last_resort()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.Dot1qVlanStaticName, [Vb($"{Oids.Dot1qVlanStaticName}.5", Txt("v5"))]);
        s.Walk(Ip, "pub@5", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub@5", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        (await Scanner(s).ScanAsync(Req(), Noop, default)).Fdb.Should().ContainSingle().Which.Vlan.Should().Be(5);
    }

    [Fact]
    public async Task Own_interface_macs_are_not_reported_as_devices()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDD0001", 1, 3), ("AABBCCDDEE01", 1, 3)));   // 0001 = Gi1/0/1's own ifPhysAddress
        (await Scanner(s).ScanAsync(Req(), Noop, default)).Fdb.Select(f => f.Mac).Should().Equal("AABBCCDDEE01");
    }

    [Fact]
    public async Task Pvid_is_mapped_through_the_bridge_port()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((11, 1)));
        s.Walk(Ip, "pub", Oids.Dot1qPvid, [Vb($"{Oids.Dot1qPvid}.11", new SnmpValue(SnmpValueKind.Gauge, 30))]);
        (await Scanner(s).ScanAsync(Req(), Noop, default)).PvidByIfIndex.Should().Contain(1, 30);
    }

    [Fact]
    public async Task Arp_is_skipped_on_an_access_switch_but_read_when_flagged_or_when_routing()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.IpNetToMediaTable, ArpMedia((1, "192.0.2.50", "AABBCCDDEE01", 3)));
        (await Scanner(s).ScanAsync(Req(), Noop, default)).Arp.Should().BeEmpty("access switches have no ARP for user VLANs and ipForwarding is not 1");

        var flagged = await Scanner(s).ScanAsync(Req(arp: true), Noop, default);
        flagged.Arp.Should().ContainSingle().Which.Ip.Should().Be("192.0.2.50");

        s.Gets[$"{Ip}|{Oids.IpForwarding}"] = Int(1);
        var routing = await Scanner(s).ScanAsync(Req(), Noop, default);
        routing.Arp.Should().HaveCount(1); routing.IsLayer3.Should().BeTrue("ipForwarding=1 with a non-empty ARP table is auto-detected as Layer 3");
    }

    [Fact]
    public async Task Prefers_ipNetToPhysical_over_ipNetToMedia()
    {
        var s = Base();
        s.Walk(Ip, "pub", Oids.IpNetToPhysicalTable, ArpPhysical((1, "192.0.2.60", "AABBCCDDEE01", 1)));
        s.Walk(Ip, "pub", Oids.IpNetToMediaTable, ArpMedia((1, "192.0.2.99", "AABBCCDDEE09", 3)));
        (await Scanner(s).ScanAsync(Req(arp: true), Noop, default)).Arp.Select(a => a.Ip).Should().Equal("192.0.2.60");
    }

    [Fact]
    public async Task One_failing_collector_never_fails_the_whole_scan()
    {
        var s = Base();
        s.ThrowOnWalk.Add($"{Ip}|{Oids.IfXTable}");           // ifXTable times out
        s.ThrowOnWalk.Add($"{Ip}|{Oids.Dot1qTpFdbTable}");    // Q-BRIDGE times out
        s.Walk(Ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1)));
        s.Walk(Ip, "pub", Oids.Dot1dTpFdbTable, Dot1dFdb(("AABBCCDDEE01", 1, 3)));
        var steps = new List<StepResult>();
        var r = await Scanner(s).ScanAsync(Req(), x => { steps.Add(x); return Task.CompletedTask; }, default);
        r.Reachable.Should().BeTrue();
        r.Interfaces.Should().HaveCount(2, "names fall back to ifDescr");
        r.Fdb.Should().HaveCount(1);
        r.Errors.Should().Contain(e => e.Code == "OidUnavailable");
        steps.Where(x => x.State == StepState.Running).Select(x => x.Name).Should().Contain([Steps.System, Steps.Interfaces, Steps.Vlans, Steps.MacTable, Steps.Arp]);
    }

    [Fact]
    public async Task Unreachable_switch_stops_after_the_system_step_with_a_clear_error()
    {
        var s = Base(); s.Unreachable.Add(Ip);
        var r = await Scanner(s).ScanAsync(Req(), Noop, default);
        r.Reachable.Should().BeFalse();
        r.Steps.Should().ContainSingle().Which.Should().Match<StepResult>(x => x.Name == Steps.System && x.State == StepState.Failed);
        r.Errors.Single().Message.Should().Contain("No response").And.NotContain("pub");
        s.WalkLog.Should().BeEmpty("nothing else is attempted once the switch is unreachable");
    }

    [Fact]
    public async Task Empty_mac_table_is_a_warning_not_a_failure()
    {
        var r = await Scanner(Base()).ScanAsync(Req(), Noop, default);
        r.Reachable.Should().BeTrue();
        r.Steps.Single(x => x.Name == Steps.MacTable).State.Should().Be(StepState.Warning);
        r.Errors.Should().Contain(e => e.Step == Steps.MacTable && e.Code == "Empty");
    }

    [Fact]
    public void Vlan_credentials_for_v2c_and_v3()
    {
        SnmpSwitchScanner.VlanCredentials(new SnmpCredentials { Version = SnmpVersion.V2c, Community = "c" }, 7).Community.Should().Be("c@7");
        SnmpSwitchScanner.VlanCredentials(new SnmpCredentials { Version = SnmpVersion.V3, Username = "u" }, 7).ContextName.Should().Be("vlan-7");
    }
}
