using ClosedXML.Excel;
using FluentAssertions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.UnitTests;

public class TopologyBuilderTests
{
    private static TopoSwitch Sw(int id, string name, bool l3 = false) => new(id, name, $"10.0.0.{id}", "Cisco", "Reachable", l3, 48, 1, 10);
    private static TopoLink L(int from, int to, string port, int ifx, string? remotePort = null, NeighborConfidence c = NeighborConfidence.ConfirmedSwitch, NeighborProtocol pr = NeighborProtocol.Lldp) =>
        new(from, from * 1000 + ifx, port, ifx, to, null, null, null, remotePort, null, c, pr);
    private static TopoLink Ext(int from, string port, int ifx, string key, string? name = null) =>
        new(from, from * 1000 + ifx, port, ifx, null, key, name, key, "Gi0/1", null, NeighborConfidence.ConfirmedSwitch, NeighborProtocol.Lldp);

    [Fact]
    public void Reverse_links_are_paired_into_one_edge_with_both_port_names()
    {
        var t = TopologyBuilder.Build([Sw(1, "CORE", true), Sw(2, "ACCESS")], [L(1, 2, "Gi1/0/24", 24), L(2, 1, "Gi1/0/48", 48)]);
        var e = t.Edges.Should().ContainSingle().Subject;
        (e.SourcePort, e.TargetPort, e.SourceSwitchId, e.TargetSwitchId).Should().Be(("Gi1/0/24", "Gi1/0/48", 1, 2));
    }

    [Fact] public void A_one_sided_link_still_draws_an_edge_using_the_LLDP_remote_port_name()
    {
        var e = TopologyBuilder.Build([Sw(1, "CORE", true), Sw(2, "ACCESS")], [L(1, 2, "Gi1/0/24", 24, remotePort: "Gi1/0/1")]).Edges.Single();
        e.TargetPort.Should().Be("Gi1/0/1");
    }

    [Fact] public void Parallel_links_between_the_same_switches_stay_two_edges_paired_by_remote_port()
    {
        var links = new[] { L(1, 2, "Gi1/0/23", 23, "Gi1/0/47"), L(1, 2, "Gi1/0/24", 24, "Gi1/0/48"), L(2, 1, "Gi1/0/47", 47, "Gi1/0/23"), L(2, 1, "Gi1/0/48", 48, "Gi1/0/24") };
        var t = TopologyBuilder.Build([Sw(1, "CORE", true), Sw(2, "ACCESS")], links);
        t.Edges.Select(e => (e.SourcePort, e.TargetPort)).Should().BeEquivalentTo(new[] { ("Gi1/0/23", "Gi1/0/47"), ("Gi1/0/24", "Gi1/0/48") });
    }

    [Fact] public void Edge_confidence_is_the_weaker_of_the_two_directions()
    {
        var t = TopologyBuilder.Build([Sw(1, "A"), Sw(2, "B")], [L(1, 2, "p", 1, c: NeighborConfidence.ConfirmedSwitch), L(2, 1, "q", 2, c: NeighborConfidence.LikelySwitch, pr: NeighborProtocol.Inferred)]);
        t.Edges.Single().Confidence.Should().Be(NeighborConfidence.LikelySwitch);
    }

    [Fact] public void Unregistered_bridge_neighbors_appear_as_external_nodes_shared_by_key()
    {
        var t = TopologyBuilder.Build([Sw(1, "A"), Sw(2, "B")], [Ext(1, "p1", 1, "10.9.9.9", "UNMANAGED"), Ext(2, "p2", 2, "10.9.9.9")]);
        var ghost = t.Nodes.Should().ContainSingle(n => n.External).Subject;
        ghost.Name.Should().Be("UNMANAGED"); ghost.SwitchId.Should().BeNull();
        t.Edges.Should().HaveCount(2).And.OnlyContain(e => e.Target == ghost.Id);
    }

    [Fact] public void Layer3_switch_is_the_root_and_levels_follow_distance()
    {
        var sw = new[] { Sw(1, "ACCESS-A"), Sw(2, "CORE", true), Sw(3, "DIST"), Sw(4, "ACCESS-B") };
        var links = new[] { L(2, 3, "a", 1), L(3, 2, "b", 2), L(3, 1, "c", 3), L(1, 3, "d", 4), L(3, 4, "e", 5), L(4, 3, "f", 6) };
        var t = TopologyBuilder.Build(sw, links);
        t.Nodes.Single(n => n.IsRoot).Name.Should().Be("CORE");
        t.Nodes.ToDictionary(n => n.Name, n => n.Level).Should().Equal(new Dictionary<string, int> { ["CORE"] = 0, ["DIST"] = 1, ["ACCESS-A"] = 2, ["ACCESS-B"] = 2 });
        t.Nodes.Where(n => n.Level == 2).Select(n => n.Order).Should().BeEquivalentTo(new[] { 0, 1 }, "siblings get distinct order slots");
    }

    [Fact] public void Without_a_layer3_switch_the_best_connected_switch_is_the_root() =>
        TopologyBuilder.Build([Sw(1, "A"), Sw(2, "HUB"), Sw(3, "C")], [L(2, 1, "x", 1), L(2, 3, "y", 2), L(1, 2, "z", 3)]).Nodes.Single(n => n.IsRoot).Name.Should().Be("HUB");

    [Fact] public void Isolated_switches_are_still_nodes_and_are_not_flagged_as_roots()
    {
        var t = TopologyBuilder.Build([Sw(1, "LONELY")], []);
        t.Nodes.Should().ContainSingle().Which.Should().Match<TopoNode>(n => n.Level == 0 && !n.IsRoot);
        t.Edges.Should().BeEmpty();
    }

    [Fact] public void Cycles_and_self_links_and_links_from_unknown_switches_do_not_break_the_layout()
    {
        var sw = new[] { Sw(1, "A", true), Sw(2, "B"), Sw(3, "C") };
        var t = TopologyBuilder.Build(sw, [L(1, 2, "a", 1), L(2, 3, "b", 2), L(3, 1, "c", 3), L(2, 2, "self", 9), L(99, 1, "ghost", 1)]);
        t.Nodes.Should().HaveCount(3).And.OnlyContain(n => n.Level <= 1);
        t.Edges.Should().HaveCount(3);
    }

    [Fact] public void Counts_are_carried_to_the_nodes() =>
        TopologyBuilder.Build([Sw(1, "A")], []).Nodes.Single().Should().Match<TopoNode>(n => n.Ports == 48 && n.Devices == 10 && n.Vendor == "Cisco" && n.Status == "Reachable");
}

public class DeviceExportBuilderTests
{
    private static DeviceDto D(string host, string? sw = "SW-1", string? port = "Gi1/0/1", int? vlan = 50, string? ip = "10.0.0.5", DeviceState st = DeviceState.Active) =>
        new(1, host, HostnameSource.ReverseDns, ip, "AA:BB:CC:DD:EE:01", sw is null ? null : 1, sw, 1, 1, port, vlan, st, new DateTime(2026, 1, 1, 9, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 2, 10, 30, 5, DateTimeKind.Utc), false);

    [Fact]
    public void Csv_has_the_required_columns_in_order_and_round_trips()
    {
        var text = System.Text.Encoding.UTF8.GetString(DeviceExportBuilder.Csv([D("PC-001"), D("Unknown Device", sw: null, port: null, vlan: null, ip: null, st: DeviceState.Stale)])).TrimStart('﻿');
        var rows = SwitchPortFinder.Application.Import.Csv.Parse(text, ',');
        rows[0].Should().Equal("Switch", "Port", "MAC", "IP", "Hostname", "VLAN", "Status", "Last Seen", "Manufacturer");
        rows[1].Should().Equal("SW-1", "Gi1/0/1", "AA:BB:CC:DD:EE:01", "10.0.0.5", "PC-001", "50", "Active", "2026-01-02 10:30:05Z", "");
        rows[2].Should().Equal("", "", "AA:BB:CC:DD:EE:01", "", "Unknown Device", "", "Stale", "2026-01-02 10:30:05Z", "");
    }

    [Theory] [InlineData("=1+1")] [InlineData("+cmd")] [InlineData("@SUM(A1)")] [InlineData("-2+3")]
    public void Csv_neutralises_spreadsheet_formula_injection_from_hostnames(string host)
    {
        var text = System.Text.Encoding.UTF8.GetString(DeviceExportBuilder.Csv([D(host)]));
        SwitchPortFinder.Application.Import.Csv.Parse(text.TrimStart('﻿'), ',')[1][4].Should().StartWith("'", "a leading formula character must not reach Excel as a formula");
    }

    [Fact] public void Csv_quotes_commas_and_quotes_in_values()
    {
        var text = System.Text.Encoding.UTF8.GetString(DeviceExportBuilder.Csv([D("lab, \"main\" PC")]));
        SwitchPortFinder.Application.Import.Csv.Parse(text.TrimStart('﻿'), ',')[1][4].Should().Be("lab, \"main\" PC");
    }

    [Fact]
    public void Xlsx_has_header_filter_frozen_row_and_text_only_cells()
    {
        using var wb = new XLWorkbook(new MemoryStream(DeviceExportBuilder.Xlsx([D("PC-001"), D("=HYPERLINK(\"http://x\")")])));
        var ws = wb.Worksheet(1);
        ws.Row(1).Cells(1, 9).Select(c => c.GetString()).Should().Equal(DeviceExportBuilder.Columns);
        ws.Cell(2, 5).GetString().Should().Be("PC-001"); ws.Cell(2, 3).GetString().Should().Be("AA:BB:CC:DD:EE:01");
        ws.Cell(3, 5).HasFormula.Should().BeFalse("exported text is never evaluated as a formula"); ws.Cell(3, 5).GetString().Should().StartWith("=HYPERLINK");
        ws.Cell(3, 5).DataType.Should().Be(XLDataType.Text);
        ws.AutoFilter.IsEnabled.Should().BeTrue(); ws.SheetView.SplitRow.Should().Be(1);
    }

    [Fact] public void Large_exports_complete_quickly()
    {
        var rows = Enumerable.Range(0, 50_000).Select(i => D($"host-{i}")).ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var csv = DeviceExportBuilder.Csv(rows); var xlsx = DeviceExportBuilder.Xlsx(rows);
        sw.Stop();
        csv.Length.Should().BeGreaterThan(1_000_000); xlsx.Length.Should().BeGreaterThan(100_000);
        sw.Elapsed.TotalSeconds.Should().BeLessThan(30, "50,000 rows to CSV and XLSX (measured, see report)");
        Console.WriteLine($"EXPORT 50k rows: {sw.ElapsedMilliseconds} ms (csv {csv.Length / 1024} KB, xlsx {xlsx.Length / 1024} KB)");
    }
}
