using FluentAssertions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.UnitTests;

public class ParsedQueryTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:01")] [InlineData("aa-bb-cc-dd-ee-01")] [InlineData("aabb.ccdd.ee01")] [InlineData("AABBCCDDEE01")]
    public void Full_macs_in_any_format_are_exact_and_normalised(string q)
    {
        var p = ParsedQuery.Parse(q)!;
        p.MacExact.Should().BeTrue(); p.MacKey.Should().Be("AABBCCDDEE01"); p.LooksLikeIp.Should().BeFalse();
    }

    [Theory]
    [InlineData("aabb", "AABB")] [InlineData("AA:BB:CC", "AABBCC")] [InlineData("aabb.ccdd", "AABBCCDD")]
    public void Partial_macs_need_at_least_four_hex_chars(string q, string key)
    {
        var p = ParsedQuery.Parse(q)!; p.MacKey.Should().Be(key); p.MacExact.Should().BeFalse();
    }

    [Theory] [InlineData("aab")] [InlineData("PC-001")] [InlineData("core-sw")] [InlineData("Gi1/0/15")]
    public void Non_mac_text_has_no_mac_key(string q) => ParsedQuery.Parse(q)!.MacKey.Should().BeNull();

    [Fact] public void Full_ipv4_and_ipv6_are_recognised_and_never_treated_as_a_mac()
    {
        var p = ParsedQuery.Parse("10.10.50.101")!; p.IpExact.Should().Be("10.10.50.101"); p.LooksLikeIp.Should().BeTrue(); p.MacKey.Should().BeNull();
        ParsedQuery.Parse("2001:DB8::1")!.IpExact.Should().Be("2001:db8::1");
    }

    [Theory] [InlineData("10.10.50")] [InlineData("10.10")] [InlineData("192.168.1.")]
    public void Ip_fragments_are_not_mac_fragments(string q)
    {
        var p = ParsedQuery.Parse(q)!; p.LooksLikeIp.Should().BeTrue(); p.MacKey.Should().BeNull(); p.IpExact.Should().BeNull();
    }

    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")] [InlineData("a")]
    public void Empty_or_one_character_queries_are_not_searched(string? q) => ParsedQuery.Parse(q).Should().BeNull();

    [Fact] public void Text_is_trimmed() => ParsedQuery.Parse("  PC-001 ")!.Text.Should().Be("PC-001");
}

public class SqlTextTests
{
    [Theory]
    [InlineData("abc", "%abc%")] [InlineData("100%", "%100\\%%")] [InlineData("a_b", "%a\\_b%")] [InlineData("[x]", "%\\[x]%")] [InlineData("a\\b", "%a\\\\b%")]
    public void Wildcards_are_escaped_so_user_input_is_always_literal(string input, string expected) => SqlText.Contains(input).Should().Be(expected);
}

public class TraceBuilderTests
{
    private static TraceSwitch Sw(int id, string name, bool l3 = false) => new(id, name, $"10.0.0.{id}", l3);
    private static TraceLink L(int from, int to, string port, NeighborConfidence c = NeighborConfidence.ConfirmedSwitch, string? remote = null, int? remoteIf = null) =>
        new(from, from * 100 + to, port, to, to, c, remote, remoteIf);
    private static TraceDevice Dev(int? sw, string port = "Gi1/0/15") => new(1, "PC-001", "10.0.0.101", "AA:BB:CC:DD:EE:01", sw, sw is null ? null : 7, sw is null ? null : port, 15, 50, false);
    private static string Path(TraceResult r) => string.Join(" → ", r.Hops.Select(h => $"{h.SwitchName}:{h.PortName}"));

    [Fact]
    public void Acceptance_path_core_to_access_to_device()
    {
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE-SW-01", true), Sw(2, "ACCESS-SW-01")], [L(1, 2, "Gi1/0/24"), L(2, 1, "Gi1/0/24")]);
        Path(r).Should().Be("CORE-SW-01:Gi1/0/24 → ACCESS-SW-01:Gi1/0/15");
        r.Partial.Should().BeFalse(); r.Message.Should().BeNull();
        r.Hops[^1].IsFinal.Should().BeTrue(); r.Hops[0].IsFinal.Should().BeFalse();
        r.Final.Should().Match<FinalLocation>(f => f.SwitchName == "ACCESS-SW-01" && f.PortName == "Gi1/0/15" && f.Hostname == "PC-001" && f.Vlan == 50);
    }

    [Fact] public void Three_level_chain_walks_every_hop_in_order()
    {
        var sw = new[] { Sw(1, "CORE", true), Sw(2, "DIST"), Sw(3, "ACCESS") };
        var links = new[] { L(1, 2, "Te1/1/1"), L(2, 1, "Te1/0/1"), L(2, 3, "Gi1/0/48"), L(3, 2, "Gi1/0/24") };
        Path(TraceBuilder.Build(Dev(3), sw, links)).Should().Be("CORE:Te1/1/1 → DIST:Gi1/0/48 → ACCESS:Gi1/0/15");
    }

    [Fact] public void Device_on_the_root_switch_is_a_single_final_hop_and_not_partial()
    {
        var r = TraceBuilder.Build(Dev(1), [Sw(1, "CORE", true), Sw(2, "ACCESS")], [L(1, 2, "Gi1/0/24")]);
        Path(r).Should().Be("CORE:Gi1/0/15"); r.Partial.Should().BeFalse();
    }

    [Fact] public void Branching_tree_only_follows_the_branch_to_the_device()
    {
        var sw = new[] { Sw(1, "CORE", true), Sw(2, "A"), Sw(3, "B") };
        var links = new[] { L(1, 2, "p2"), L(2, 1, "u"), L(1, 3, "p3"), L(3, 1, "u") };
        Path(TraceBuilder.Build(Dev(3), sw, links)).Should().Be("CORE:p3 → B:Gi1/0/15");
    }

    [Fact] public void A_link_known_only_from_the_far_side_uses_its_LLDP_remote_port_as_the_egress_port()
    {
        // ACCESS knows it uplinks to CORE (remote port Gi1/0/24); CORE has no row for it.
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "ACCESS")], [L(2, 1, "Gi1/0/1", remote: "Gi1/0/24", remoteIf: 24)]);
        Path(r).Should().Be("CORE:Gi1/0/24 → ACCESS:Gi1/0/15"); r.Hops[0].IfIndex.Should().Be(24); r.Partial.Should().BeFalse();
    }

    [Fact] public void Unknown_egress_port_is_reported_as_partial_but_the_known_hops_are_returned()
    {
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "ACCESS")], [L(2, 1, "Gi1/0/1")]);   // no remote port info either
        r.Partial.Should().BeTrue(); r.Message.Should().StartWith(TraceBuilder.PartialMessage).And.Contain("CORE").And.Contain("ACCESS");
        r.Hops.Should().HaveCount(2); r.Hops[0].PortName.Should().BeNull(); r.Hops[1].IsFinal.Should().BeTrue();
    }

    [Fact] public void No_proven_link_to_the_core_is_partial_and_never_invents_a_link()
    {
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "LONELY")], []);
        r.Partial.Should().BeTrue(); r.Message.Should().Contain(TraceBuilder.PartialMessage).And.Contain("CORE");
        Path(r).Should().Be("LONELY:Gi1/0/15", "only the final hop is known");
    }

    [Fact] public void Component_without_any_layer3_switch_still_traces_from_its_best_connected_switch()
    {
        var sw = new[] { Sw(1, "HUB"), Sw(2, "A"), Sw(3, "B") };
        var links = new[] { L(1, 2, "p2"), L(1, 3, "p3"), L(2, 1, "u"), L(3, 1, "u") };
        var r = TraceBuilder.Build(Dev(3), sw, links);
        Path(r).Should().Be("HUB:p3 → B:Gi1/0/15"); r.Partial.Should().BeFalse("no Layer 3 switch is registered at all, so nothing is missing");
    }

    [Fact] public void Parallel_links_prefer_the_confirmed_one()
    {
        var links = new[] { L(1, 2, "Gi1/0/23", NeighborConfidence.LikelySwitch), new TraceLink(1, 150, "Gi1/0/24", 24, 2, NeighborConfidence.ConfirmedSwitch, null, null) };
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "A")], links);
        r.Hops[0].PortName.Should().Be("Gi1/0/24"); r.Hops[0].LinkConfidence.Should().Be(NeighborConfidence.ConfirmedSwitch);
    }

    [Fact] public void Link_confidence_is_carried_on_each_hop() =>
        TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "A")], [L(1, 2, "p", NeighborConfidence.LikelySwitch)]).Hops[0].LinkConfidence.Should().Be(NeighborConfidence.LikelySwitch);

    [Fact] public void A_cycle_in_the_link_graph_terminates()
    {
        var sw = new[] { Sw(1, "CORE", true), Sw(2, "A"), Sw(3, "B") };
        var links = new[] { L(1, 2, "p12"), L(2, 3, "p23"), L(3, 1, "p31"), L(2, 1, "q21"), L(3, 2, "q32"), L(1, 3, "q13") };
        var r = TraceBuilder.Build(Dev(3), sw, links);
        r.Hops.Should().HaveCountGreaterThan(1).And.HaveCountLessThanOrEqualTo(3);
    }

    [Fact] public void Self_links_and_links_to_unregistered_switches_are_ignored()
    {
        var r = TraceBuilder.Build(Dev(2), [Sw(1, "CORE", true), Sw(2, "A")], [L(2, 2, "loop"), L(2, 99, "ghost"), L(1, 2, "p"), L(2, 1, "u")]);
        Path(r).Should().Be("CORE:p → A:Gi1/0/15");
    }

    [Fact] public void Device_without_an_edge_location_cannot_be_traced()
    {
        var r = TraceBuilder.Build(Dev(null), [Sw(1, "CORE", true)], []);
        r.Partial.Should().BeTrue(); r.Final.Should().BeNull(); r.Hops.Should().BeEmpty(); r.Message.Should().Contain("not been seen on an edge port");
    }
}
