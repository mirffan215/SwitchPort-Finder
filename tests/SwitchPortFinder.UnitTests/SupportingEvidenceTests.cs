using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery;
using SwitchPortFinder.Discovery.Parsing;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.TestSupport;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.UnitTests;

public class SupportParserTests
{
    [Fact] public void PortList_msb_of_the_first_octet_is_bridge_port_1() =>
        SupportParser.PortListBits([0b1010_0000, 0b0000_0001]).Should().Equal(1, 3, 16);

    [Fact] public void Empty_or_null_portlist_has_no_ports() { SupportParser.PortListBits(null).Should().BeEmpty(); SupportParser.PortListBits([]).Should().BeEmpty(); }

    private static IEnumerable<SnmpVarBind> Egress(params (int vlan, byte[] ports)[] rows) => rows.Select(r => Vb($"{Oids.Dot1qVlanStaticEgressPorts}.{r.vlan}", Bytes(r.ports)));
    private static IEnumerable<SnmpVarBind> Untagged(params (int vlan, byte[] ports)[] rows) => rows.Select(r => Vb($"{Oids.Dot1qVlanStaticUntaggedPorts}.{r.vlan}", Bytes(r.ports)));

    [Fact] public void A_port_that_is_tagged_in_several_vlans_is_trunk_like_while_an_access_port_is_not()
    {
        // bridge port 1 = access (untagged in VLAN 10 only); bridge port 2 = trunk (tagged in 10, 20, 30)
        var map = new Dictionary<int, int> { [1] = 101, [2] = 102 };
        var m = SupportParser.VlanMembership(
            Egress((10, [0b1100_0000]), (20, [0b0100_0000]), (30, [0b0100_0000])),
            Untagged((10, [0b1000_0000]), (20, []), (30, [])), map);
        m[101].Should().Be((1, 0)); m[102].Should().Be((3, 3));
        var ifaces = new[] { new InterfaceInfo(101, "Gi1/0/1", null, null, true, true, 1000, null, 1500, 6), new InterfaceInfo(102, "Gi1/0/2", null, null, true, true, 1000, null, 1500, 6) };
        var s = SupportParser.Build(ifaces, null, map, m);
        s[102].Flags.Should().HaveFlag(PortSupport.TaggedVlans); s[102].VlanCount.Should().Be(3);
        s[101].Flags.Should().Be(PortSupport.None);
    }

    [Fact] public void Stp_root_port_is_mapped_through_the_bridge_port()
    {
        var map = new Dictionary<int, int> { [7] = 107 };
        var s = SupportParser.Build([new InterfaceInfo(107, "Gi1/0/7", null, null, true, true, 1000, null, 1500, 6), new InterfaceInfo(108, "Gi1/0/8", null, null, true, true, 1000, null, 1500, 6)], 7, map, new Dictionary<int, (int, int)>());
        s.Should().ContainKey(107); s[107].Flags.Should().Be(PortSupport.StpRootPort); s.Should().NotContainKey(108);
        SupportParser.Build([], 0, map, new Dictionary<int, (int, int)>()).Should().BeEmpty("root port 0 means 'this bridge is the root'");
    }

    [Theory] [InlineData("Uplink to CORE-SW-01", true)] [InlineData("TRUNK to dist", true)] [InlineData("Po1", true)] [InlineData("backbone", true)]
    [InlineData("PC-001 wall jack", false)] [InlineData("", false)] [InlineData("flagship printer", false)] [InlineData("Gi1/0/24", false)]
    public void Description_hints(string alias, bool expected) => SupportParser.DescriptionHint(alias, null).Should().Be(expected);
}

public class SupportClassifierTests
{
    private static readonly SwitchRef S1 = new(1, "CORE", "10.0.0.1", null, new HashSet<string>());
    private static readonly SwitchRef S2 = new(2, "ACCESS", "10.0.0.2", null, new HashSet<string>());
    private static PortRef P(int id, int sw, PortSupport sup = PortSupport.None, int vlans = 0) => new(id, sw, id, $"Gi1/0/{id}", true, sup, vlans);
    private static ObsRef O(int sw, int port, string mac) => new(sw, port, mac);
    private static PortVerdict V(ClassificationResult r, int port) => r.Ports.Single(p => p.PortId == port);
    private static ClassificationResult Run(IEnumerable<PortRef> ports, IEnumerable<ObsRef> obs) => UplinkClassifier.Classify([S1, S2], ports.ToList(), [], obs.ToList());

    [Fact] public void Supporting_evidence_alone_never_makes_an_uplink()
    {
        // STP root port + trunk + "uplink" description, but only ordinary MACs and no overlap: still just an endpoint port.
        var r = Run([P(1, 1, PortSupport.StpRootPort | PortSupport.TaggedVlans | PortSupport.DescriptionHint, 5)], [O(1, 1, "CC0000000001"), O(1, 1, "CC0000000002")]);
        V(r, 1).Type.Should().NotBe(PortType.SwitchUplink);
        Run([P(1, 1, PortSupport.StpRootPort)], []).Ports.Single().Type.Should().Be(PortType.Unknown, "no MACs and no neighbor: nothing proves a link");
    }

    [Fact] public void Supporting_evidence_lowers_the_bar_by_one_mac_when_the_mac_table_already_overlaps()
    {
        // 2 MACs on S1.p1 that are each alone on an edge port of S2. minMacs = 3, so without support this is NOT enough.
        var obs = new[] { O(1, 1, "CC0000000001"), O(2, 11, "CC0000000001"), O(1, 1, "CC0000000002"), O(2, 12, "CC0000000002") };
        V(Run([P(1, 1), P(11, 2), P(12, 2)], obs), 1).Type.Should().NotBe(PortType.SwitchUplink, "below the MAC threshold without supporting evidence");
        var with = V(Run([P(1, 1, PortSupport.StpRootPort), P(11, 2), P(12, 2)], obs), 1);
        with.Type.Should().Be(PortType.SwitchUplink); with.Confidence.Should().Be(NeighborConfidence.LikelySwitch);
        with.Evidence.Should().Contain("spanning-tree root port");
    }

    [Fact] public void One_overlapping_mac_is_never_enough_even_with_every_supporting_signal()
    {
        var obs = new[] { O(1, 1, "CC0000000001"), O(2, 11, "CC0000000001"), O(1, 1, "CC0000000002") };
        V(Run([P(1, 1, PortSupport.StpRootPort | PortSupport.TaggedVlans | PortSupport.DescriptionHint, 4), P(11, 2)], obs), 1).Type.Should().NotBe(PortType.SwitchUplink);
    }

    [Fact] public void Supporting_text_is_appended_to_mac_table_evidence()
    {
        var s2 = new SwitchRef(2, "ACCESS", "10.0.0.2", null, new HashSet<string> { "BBBBBBBB0018" });
        var r = UplinkClassifier.Classify([S1, s2], [P(1, 1, PortSupport.TaggedVlans, 3), P(3, 2)], [], [O(1, 1, "BBBBBBBB0018")]);
        V(r, 1).Evidence.Should().Contain("ACCESS").And.Contain("tagged VLANs (3 VLAN memberships)");
    }
}

public class SupportScannerTests
{
    [Fact]
    public async Task Scanner_collects_stp_root_port_tagged_vlans_and_description_hints()
    {
        const string ip = "192.0.2.10";
        var s = new ScriptedSnmp().WithSystem(ip, "SW", "1.3.6.1.4.1.8072");
        s.Walk(ip, "pub", Oids.IfTable, IfTable((1, "Gi1/0/1", 6, true, null), (2, "Gi1/0/2", 6, true, null)));
        s.Walk(ip, "pub", Oids.IfXTable, IfXTable((1, "Gi1/0/1", 1000, "PC wall jack"), (2, "Gi1/0/2", 1000, "Uplink to core")));
        s.Walk(ip, "pub", Oids.Dot1dBasePortIfIndex, BasePortMap((1, 1), (2, 2)));
        s.Gets[$"{ip}|{Oids.Dot1dStpRootPort}"] = Int(2);
        s.Walk(ip, "pub", Oids.Dot1qVlanStaticEgressPorts, [Vb($"{Oids.Dot1qVlanStaticEgressPorts}.10", Bytes([0b1100_0000])), Vb($"{Oids.Dot1qVlanStaticEgressPorts}.20", Bytes([0b0100_0000]))]);
        s.Walk(ip, "pub", Oids.Dot1qVlanStaticUntaggedPorts, [Vb($"{Oids.Dot1qVlanStaticUntaggedPorts}.10", Bytes([0b1000_0000])), Vb($"{Oids.Dot1qVlanStaticUntaggedPorts}.20", Bytes([]))]);
        var scanner = new SnmpSwitchScanner(s, Options.Create(new SnmpOptions()), Options.Create(new DiscoveryOptions()), NullLogger<SnmpSwitchScanner>.Instance);
        var r = await scanner.ScanAsync(new ScanRequest(ip, new SnmpCredentials { Version = SnmpVersion.V2c, Community = "pub" }, Manufacturer.AutoDetect, false), _ => Task.CompletedTask, default);
        r.Support[1].Flags.Should().Be(PortSupport.None, "an access port: one VLAN, untagged, ordinary description"); r.Support[1].VlanCount.Should().Be(1);
        r.Support[2].Flags.Should().HaveFlag(PortSupport.StpRootPort).And.HaveFlag(PortSupport.TaggedVlans).And.HaveFlag(PortSupport.DescriptionHint);
        r.Support[2].VlanCount.Should().Be(2);
        r.Steps.Single(x => x.Name == Steps.Vlans).State.Should().BeOneOf(StepState.Ok, StepState.Warning);
    }
}
