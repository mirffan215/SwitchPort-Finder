using FluentAssertions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Discovery.Parsing;
using SwitchPortFinder.Domain.Enums;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;
using static SwitchPortFinder.Application.Snmp.Oids;

namespace SwitchPortFinder.UnitTests;

public class LldpParserTests
{
    private static InterfaceInfo If(int idx, string name, string? mac = null) => new(idx, name, "descr-" + name, null, true, true, 1000, mac, 1500, 6);
    private static readonly InterfaceInfo[] Ifs = [If(10101, "Gi1/0/1", "AABBCC000101"), If(10102, "Gi1/0/2")];
    private static byte[] T(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData(0x20, NeighborCaps.Bridge)]
    [InlineData(0x28, NeighborCaps.Bridge | NeighborCaps.Router)]     // bit 2 = bridge, bit 4 = router
    [InlineData(0x04, NeighborCaps.Telephone)]
    [InlineData(0x10, NeighborCaps.WlanAp)]
    [InlineData(0x01, NeighborCaps.Station)]
    [InlineData(0x80, NeighborCaps.Other)]
    [InlineData(0x00, NeighborCaps.None)]
    public void Capability_bits_follow_IEEE_802_1AB(byte b, NeighborCaps expected) => NeighborParser.Caps([b]).Should().Be(expected);

    [Fact]
    public void Parses_a_full_neighbor_with_mac_chassis_name_port_and_management_ip()
    {
        var rem = LldpRem(10101, 1, 4, MacBytes("AABBCCDDEE0F"), 5, T("Gi1/0/48"), "DIST-SW-02", "uplink", "Cisco IOS", 0x28);
        var n = NeighborParser.Lldp(rem, LldpMan(10101, 1, "192.0.2.20"), NeighborParser.LocalPorts([]), Ifs).Single();
        n.LocalIfIndex.Should().Be(10101); n.ChassisMac.Should().Be("AABBCCDDEE0F"); n.ChassisId.Should().Be("AA:BB:CC:DD:EE:0F");
        n.RemotePortId.Should().Be("Gi1/0/48"); n.SysName.Should().Be("DIST-SW-02"); n.MgmtIp.Should().Be("192.0.2.20");
        n.Caps.Should().Be(NeighborCaps.Bridge | NeighborCaps.Router); n.Protocol.Should().Be(NeighborProtocol.Lldp);
    }

    [Fact]
    public void Port_id_subtype_mac_is_shown_as_a_mac_and_network_address_chassis_as_ip()
    {
        var rem = LldpRem(10102, 1, 5, [1, 192, 0, 2, 9], 3, MacBytes("AABBCC000099"), "AP-1", caps: 0x10);
        var n = NeighborParser.Lldp(rem, [], NeighborParser.LocalPorts([]), Ifs).Single();
        n.ChassisId.Should().Be("192.0.2.9"); n.MgmtIp.Should().Be("192.0.2.9"); n.RemotePortId.Should().Be("AA:BB:CC:00:00:99");
    }

    [Fact]
    public void Local_port_number_is_mapped_by_interface_name_when_it_is_not_an_ifIndex()
    {
        // Some vendors number LLDP ports 1..N regardless of ifIndex; lldpLocPortId (subtype interfaceName) says which interface it is.
        var loc = NeighborParser.LocalPorts(LldpLocPort(2, 5, "Gi1/0/2"));
        NeighborParser.MapLocalPort(2, loc, Ifs).Should().Be(10102);
    }

    [Fact] public void Local_port_subtype_local_uses_the_number_as_ifIndex_when_it_exists() =>
        NeighborParser.MapLocalPort(7, NeighborParser.LocalPorts(LldpLocPort(7, 7, "10102")), Ifs).Should().Be(10102);

    [Fact] public void Local_port_can_be_matched_by_mac_subtype() =>
        NeighborParser.MapLocalPort(3, NeighborParser.LocalPorts([Vb($"{LldpLocPortTable}.2.3", Int(3)), Vb($"{LldpLocPortTable}.3.3", Bytes(MacBytes("AABBCC000101")))]), Ifs).Should().Be(10101);

    [Fact] public void Local_port_number_falls_back_to_ifIndex_and_unmappable_ports_are_dropped()
    {
        NeighborParser.MapLocalPort(10102, new Dictionary<int, LldpLocalPort>(), Ifs).Should().Be(10102);
        NeighborParser.MapLocalPort(55, new Dictionary<int, LldpLocalPort>(), Ifs).Should().BeNull();
        NeighborParser.Lldp(LldpRem(55, 1, 4, MacBytes("AABBCCDDEE0F"), 5, T("x"), "ghost", caps: 0x20), [], new Dictionary<int, LldpLocalPort>(), Ifs).Should().BeEmpty();
    }

    [Fact]
    public void Two_neighbors_on_one_port_are_both_returned()   // e.g. an IP phone with a PC behind it
    {
        var rem = LldpRem(10101, 1, 4, MacBytes("AABBCC000001"), 3, MacBytes("AABBCC000002"), "SEP001", caps: 0x24).Concat(LldpRem(10101, 2, 4, MacBytes("AABBCC000003"), 3, MacBytes("AABBCC000004"), "PC", caps: 0x01));
        NeighborParser.Lldp(rem, [], new Dictionary<int, LldpLocalPort>(), Ifs).Should().HaveCount(2);
    }
}

public class CdpParserTests
{
    private static readonly InterfaceInfo[] Ifs = [new(10, "Gi1/0/10", null, null, true, true, 1000, null, 1500, 6)];

    [Fact]
    public void Parses_device_id_port_ip_platform_and_capabilities()
    {
        var n = NeighborParser.Cdp(CdpRow(10, 1, "192.0.2.30", "DIST-SW-03.corp.example", "GigabitEthernet1/0/1", "cisco WS-C3850-48P", 0x28), Ifs).Single();   // 0x28 = router? no: 0x08 switch + 0x20 IGMP
        n.SysName.Should().Be("DIST-SW-03.corp.example"); n.RemotePortId.Should().Be("GigabitEthernet1/0/1"); n.MgmtIp.Should().Be("192.0.2.30");
        n.Platform.Should().Be("cisco WS-C3850-48P"); n.Caps.Should().Be(NeighborCaps.Bridge); n.Protocol.Should().Be(NeighborProtocol.Cdp);
    }

    [Theory]
    [InlineData(0x01u, NeighborCaps.Router)] [InlineData(0x08u, NeighborCaps.Bridge)] [InlineData(0x02u, NeighborCaps.Bridge)] [InlineData(0x10u, NeighborCaps.Station)]
    [InlineData(0x09u, NeighborCaps.Router | NeighborCaps.Bridge)]
    public void Cdp_capability_mapping(uint bits, NeighborCaps expected) => NeighborParser.CdpCaps([0, 0, 0, (byte)bits]).Should().Be(expected);

    [Fact] public void Cisco_ip_phones_are_flagged_as_telephones_even_if_they_advertise_switch_capability() =>
        NeighborParser.Cdp(CdpRow(10, 1, "192.0.2.77", "SEP00AABBCC", "Port 1", "Cisco IP Phone 8841", 0x08), Ifs).Single().Caps.Should().HaveFlag(NeighborCaps.Telephone);

    [Fact] public void Rows_for_unknown_interfaces_are_ignored() =>
        NeighborParser.Cdp(CdpRow(99, 1, null, "X", "p", "plat", 0x08), Ifs).Should().BeEmpty();
}

public class UplinkClassifierTests
{
    // Two registered switches: S1 (core) and S2 (access); ports: S1: p1,p2   S2: p3,p4
    private static readonly SwitchRef S1 = new(1, "CORE-SW-01", "10.0.0.1", "core-sw-01.corp.example", new HashSet<string> { "AAAAAAAA0001" });
    private static readonly SwitchRef S2 = new(2, "ACCESS-SW-01", "10.0.0.2", null, new HashSet<string> { "BBBBBBBB0001", "BBBBBBBB0018" });
    private static PortRef P(int id, int sw, bool up = true) => new(id, sw, id, $"Gi1/0/{id}", up);
    private static ObsRef O(int sw, int port, string mac) => new(sw, port, mac);
    private static NeighborRef N(long id, int sw, int port, NeighborCaps caps, string? ip = null, string? mac = null, string? name = null, NeighborProtocol pr = NeighborProtocol.Lldp) =>
        new(id, sw, port, pr, mac, ip, name, caps);
    private static PortVerdict V(ClassificationResult r, int port) => r.Ports.Single(p => p.PortId == port);
    private static ClassificationResult Run(IEnumerable<PortRef> ports, IEnumerable<NeighborRef>? nb = null, IEnumerable<ObsRef>? obs = null, params SwitchRef[] sw) =>
        UplinkClassifier.Classify(sw.Length == 0 ? [S1, S2] : sw, ports.ToList(), (nb ?? []).ToList(), (obs ?? []).ToList());

    // ---------- 1. LLDP/CDP bridge neighbor matching a registered switch → Confirmed (linked) ----------
    [Fact] public void Neighbor_matched_by_management_ip_is_a_confirmed_linked_uplink()
    {
        var v = V(Run([P(1, 1), P(3, 2)], [N(10, 1, 1, NeighborCaps.Bridge, ip: "10.0.0.2")]), 1);
        v.Should().Match<PortVerdict>(x => x.Type == PortType.SwitchUplink && x.Confidence == NeighborConfidence.ConfirmedSwitch && x.ConnectedSwitchId == 2 && x.NeighborId == 10 && !x.Inferred);
    }

    [Fact] public void Neighbor_matched_by_chassis_mac() =>
        V(Run([P(1, 1), P(3, 2)], [N(10, 1, 1, NeighborCaps.Bridge, mac: "BBBBBBBB0001")]), 1).ConnectedSwitchId.Should().Be(2);

    [Fact] public void Neighbor_matched_by_system_name_ignoring_domain_and_case() =>
        V(Run([P(1, 3 - 1 + 0, true), P(3, 2)], [N(10, 2, 3, NeighborCaps.Bridge | NeighborCaps.Router, name: "CORE-SW-01.Corp.Example")]), 3).ConnectedSwitchId.Should().Be(1);

    [Fact] public void A_neighbor_never_matches_the_switch_it_was_seen_on() =>
        V(Run([P(1, 1)], [N(10, 1, 1, NeighborCaps.Bridge, ip: "10.0.0.1")]), 1).ConnectedSwitchId.Should().BeNull("self-match is ignored");

    // ---------- 2. bridge/router neighbor that is not registered → Confirmed, external, no link ----------
    [Fact] public void Unregistered_bridge_neighbor_is_a_confirmed_external_switch()
    {
        var r = Run([P(1, 1)], [N(10, 1, 1, NeighborCaps.Bridge, ip: "10.9.9.9", name: "UNMANAGED-SW")]);
        V(r, 1).Should().Match<PortVerdict>(x => x.Type == PortType.SwitchUplink && x.Confidence == NeighborConfidence.ConfirmedSwitch && x.ConnectedSwitchId == null && x.NeighborId == 10);
        r.NeighborMatches[10].Should().BeNull();
    }

    [Fact] public void Router_only_neighbor_counts_as_a_switch_uplink() =>
        V(Run([P(1, 1)], [N(10, 1, 1, NeighborCaps.Router, ip: "10.9.9.9")]), 1).Type.Should().Be(PortType.SwitchUplink);

    // ---------- 5. phones / APs / stations → Endpoint ----------
    [Theory]
    [InlineData(NeighborCaps.Telephone)]
    [InlineData(NeighborCaps.Telephone | NeighborCaps.Bridge)]     // IP phones advertise a bridge for the PC port
    [InlineData(NeighborCaps.WlanAp)]
    [InlineData(NeighborCaps.WlanAp | NeighborCaps.Bridge)]        // access points commonly advertise bridge too
    [InlineData(NeighborCaps.Station)]
    public void Phone_ap_and_station_neighbors_are_endpoints_not_switches(NeighborCaps caps)
    {
        var v = V(Run([P(1, 1)], [N(10, 1, 1, caps, ip: "10.9.9.9")]), 1);
        v.Type.Should().Be(PortType.Endpoint); v.Confidence.Should().Be(NeighborConfidence.Endpoint); v.ConnectedSwitchId.Should().BeNull();
    }

    [Fact] public void LLDP_saying_endpoint_beats_a_heuristic_guess()
    {
        // The port has many MACs that would trigger the overlap heuristic, but LLDP says it is an AP: trust LLDP.
        var obs = Enumerable.Range(1, 5).SelectMany(i => new[] { O(1, 1, $"CC00000000{i:X2}"), O(2, 3, $"CC00000000{i:X2}") });
        V(Run([P(1, 1), P(3, 2)], [N(10, 1, 1, NeighborCaps.WlanAp | NeighborCaps.Bridge)], obs), 1).Type.Should().Be(PortType.Endpoint);
    }

    [Fact] public void Neighbor_without_capabilities_that_matches_nothing_proves_nothing() =>
        V(Run([P(1, 1)], [N(10, 1, 1, NeighborCaps.None, ip: "10.9.9.9")], [O(1, 1, "CC0000000001")]), 1).Type.Should().Be(PortType.Endpoint, "just a port with one MAC");

    // ---------- 3. FDB heuristic (LLDP/CDP disabled) ----------
    [Fact] public void Port_that_learned_another_registered_switchs_mac_is_a_likely_uplink_to_that_switch()
    {
        var v = V(Run([P(1, 1), P(3, 2)], obs: [O(1, 1, "BBBBBBBB0018"), O(1, 1, "CC0000000001")]), 1);
        v.Should().Match<PortVerdict>(x => x.Type == PortType.SwitchUplink && x.Confidence == NeighborConfidence.LikelySwitch && x.ConnectedSwitchId == 2 && x.Inferred && x.NeighborId == null);
        v.Evidence.Should().Contain("ACCESS-SW-01");
    }

    [Fact] public void A_switchs_own_macs_seen_on_its_own_ports_do_not_make_an_uplink() =>
        V(Run([P(3, 2)], obs: [O(2, 3, "BBBBBBBB0001")]), 3).Type.Should().NotBe(PortType.SwitchUplink);

    [Fact]
    public void Many_macs_that_are_also_edge_macs_on_other_switches_make_a_likely_uplink()
    {
        // S1.p1 learned 5 hosts; the same 5 hosts are each alone on an edge port of S2 (smaller ports) → p1 faces S2.
        var obs = new List<ObsRef>();
        for (var i = 1; i <= 5; i++) { obs.Add(O(1, 1, $"CC00000000{i:X2}")); obs.Add(O(2, 10 + i, $"CC00000000{i:X2}")); }
        var ports = new[] { P(1, 1) }.Concat(Enumerable.Range(11, 5).Select(i => P(i, 2)));
        var v = V(Run(ports, obs: obs), 1);
        v.Should().Match<PortVerdict>(x => x.Type == PortType.SwitchUplink && x.Confidence == NeighborConfidence.LikelySwitch && x.ConnectedSwitchId == 2 && x.Inferred);
    }

    [Fact]
    public void A_downstream_hub_with_many_hosts_is_NOT_mistaken_for_an_uplink()
    {
        // S2.p3 is a hub with 5 PCs. The upstream S1 sees the same 5 PCs on its (bigger) port toward S2: that bigger port must not make the hub an uplink.
        var obs = new List<ObsRef>();
        for (var i = 1; i <= 5; i++) { obs.Add(O(2, 3, $"CC00000000{i:X2}")); obs.Add(O(1, 1, $"CC00000000{i:X2}")); }
        obs.Add(O(1, 1, "DD0000000001")); obs.Add(O(1, 1, "DD0000000002"));    // S1.p1 also sees other hosts → it has more MACs than the hub
        var r = Run([P(1, 1), P(3, 2)], obs: obs);
        V(r, 3).Type.Should().Be(PortType.Endpoint, "the hub's MACs are only seen elsewhere on a LARGER (uplink-like) port");
    }

    [Fact] public void Weak_evidence_few_macs_no_overlap_never_creates_a_switch_link()
    {
        var r = Run([P(1, 1), P(3, 2)], obs: [O(1, 1, "CC0000000001"), O(1, 1, "CC0000000002"), O(2, 3, "CC0000000099")]);
        r.Ports.Should().OnlyContain(p => p.Type != PortType.SwitchUplink && p.ConnectedSwitchId == null);
    }

    [Fact] public void LLDP_confirmed_beats_the_heuristic()
    {
        var v = V(Run([P(1, 1), P(3, 2)], [N(10, 1, 1, NeighborCaps.Bridge, name: "UNMANAGED")], [O(1, 1, "BBBBBBBB0018")]), 1);
        v.Confidence.Should().Be(NeighborConfidence.ConfirmedSwitch); v.Inferred.Should().BeFalse(); v.ConnectedSwitchId.Should().BeNull();
    }

    // ---------- basic types ----------
    [Fact] public void Down_ports_are_down_even_with_stale_neighbors_and_macs() =>
        V(Run([P(1, 1, up: false)], [N(10, 1, 1, NeighborCaps.Bridge, ip: "10.0.0.2")], [O(1, 1, "BBBBBBBB0018")]), 1).Type.Should().Be(PortType.Down);

    [Fact] public void Up_port_without_any_macs_is_unknown_and_with_macs_is_endpoint()
    {
        var r = Run([P(1, 1), P(2, 1)], obs: [O(1, 2, "CC0000000001")]);
        V(r, 1).Type.Should().Be(PortType.Unknown); V(r, 2).Type.Should().Be(PortType.Endpoint);
    }

    [Theory]
    [InlineData("core-sw-01.corp.example", "core-sw-01")] [InlineData("CORE", "CORE")] [InlineData("10.0.0.1", "10.0.0.1")] [InlineData(null, null)] [InlineData(" ", null)]
    public void Short_name_normalisation(string? input, string? expected) => UplinkClassifier.Short(input).Should().Be(expected);
}

public class LocationSelectorTests
{
    private static LocationSelector.ObsCandidate C(int sw, int port, int minutesAgo = 0) => new(sw, port, 50, new DateTime(2026, 1, 1, 12, 0, 0).AddMinutes(-minutesAgo));
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    [Fact] public void Uplink_sightings_are_never_the_location()
    {
        var c = LocationSelector.Choose([C(1, 24), C(2, 15)], isUplink: p => p == 24, macCountOnPort: _ => 1, Window);
        c.Best!.PortId.Should().Be(15); c.LowConfidence.Should().BeFalse("only one edge candidate");
    }

    [Fact] public void Seen_only_behind_uplinks_has_no_provable_location() =>
        LocationSelector.Choose([C(1, 24)], p => true, _ => 1, Window).Best.Should().BeNull();

    [Fact] public void Several_edge_candidates_choose_the_port_with_the_fewest_macs_and_flag_low_confidence()
    {
        var c = LocationSelector.Choose([C(1, 5), C(2, 6)], _ => false, p => p == 5 ? 20 : 2, Window);
        c.Best!.PortId.Should().Be(6); c.LowConfidence.Should().BeTrue();
    }

    [Fact] public void A_stale_sighting_on_the_old_switch_does_not_compete_with_a_fresh_one()
    {
        var c = LocationSelector.Choose([C(1, 5, minutesAgo: 600), C(2, 6)], _ => false, p => p == 5 ? 1 : 50, Window);
        c.Best!.PortId.Should().Be(6, "the device moved; the old sighting is outside the recency window"); c.LowConfidence.Should().BeFalse();
    }

    [Fact] public void No_sightings_means_no_location() => LocationSelector.Choose([], _ => false, _ => 0, Window).Best.Should().BeNull();
}
