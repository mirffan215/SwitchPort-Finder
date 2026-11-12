using FluentAssertions;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Discovery.Parsing;
using static SwitchPortFinder.TestSupport.ScriptedSnmp;

namespace SwitchPortFinder.UnitTests;

public class WirelessApParserTests
{
    [Theory]
    [InlineData("0.17.34.51.68.85", "001122334455")]
    [InlineData("170.187.204.221.238.255", "AABBCCDDEEFF")]
    [InlineData("1.2.3", null)]
    [InlineData("1.2.3.4.5.256", null)]
    public void The_mac_is_read_from_the_six_part_index(string index, string? mac) => WirelessApParser.MacFromIndex(index).Should().Be(mac);

    private static Dictionary<int, IReadOnlyList<SnmpVarBind>> Cols(params (int Col, SnmpVarBind[] Rows)[] c) => c.ToDictionary(x => x.Col, x => (IReadOnlyList<SnmpVarBind>)x.Rows);

    [Fact]
    public void Lwapp_rows_become_access_points_with_name_uptime_clients_and_status()
    {
        string R(int c, string i) => $"{Oids.LwappApEntry}.{c}.{i}";
        var cols = Cols(
            (2, [Vb(R(2, "0.17.34.51.68.85"), new SnmpValue(SnmpValueKind.OctetString, 0, [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 1]))]),
            (5, [Vb(R(5, "0.17.34.51.68.85"), Txt("AP-Lobby"))]),
            (6, [Vb(R(6, "0.17.34.51.68.85"), new SnmpValue(SnmpValueKind.TimeTicks, 360_000))]),
            (38, [Vb(R(38, "0.17.34.51.68.85"), Int(2))]),
            (54, [Vb(R(54, "0.17.34.51.68.85"), new SnmpValue(SnmpValueKind.Gauge, 7))]));
        var ap = WirelessApParser.Lwapp(cols).Should().ContainSingle().Subject;
        (ap.SysMac, ap.EthMac, ap.Name, ap.UptimeSeconds, ap.Clients, ap.AdminEnabled).Should().Be(("001122334455", "AABBCCDDEE01", "AP-Lobby", 3600L, 7, false));
    }

    [Fact]
    public void A_zero_ethernet_mac_and_missing_columns_are_tolerated()
    {
        string R(int c) => $"{Oids.LwappApEntry}.{c}.0.17.34.51.68.85";
        var cols = Cols((2, [Vb(R(2), new SnmpValue(SnmpValueKind.OctetString, 0, new byte[6]))]), (5, [Vb(R(5), Txt("  "))]));
        var ap = WirelessApParser.Lwapp(cols).Single();
        (ap.EthMac, ap.Name, ap.Clients, ap.UptimeSeconds).Should().Be((null, null, null, null));
        WirelessApParser.Lwapp(Cols()).Should().BeEmpty();
    }

    [Fact]
    public void Bsn_rows_carry_model_serial_and_ip()
    {
        string R(int c) => $"{Oids.BsnApEntry}.{c}.0.17.34.51.68.85";
        var cols = Cols((3, [Vb(R(3), Txt("AP-Old"))]), (6, [Vb(R(6), Int(1))]), (16, [Vb(R(16), Txt("AIR-AP2802I"))]), (17, [Vb(R(17), Txt("FCW123"))]));
        var ap = WirelessApParser.Bsn(cols).Single();
        (ap.Name, ap.Model, ap.Serial, ap.Associated).Should().Be(("AP-Old", "AIR-AP2802I", "FCW123", true));
    }

    [Theory]
    [InlineData("Cisco IOS XE Software, Catalyst 9800-L Wireless Controller", null, true)]
    [InlineData("Cisco Controller", null, true)]
    [InlineData("Cisco AireOS 8.10 AIR-CT5520-K9", null, true)]
    [InlineData("anything", "1.3.6.1.4.1.14179.1.1.4.3", true)]
    [InlineData("Cisco IOS Software, C3750E Software", "1.3.6.1.4.1.9.1.516", false)]
    [InlineData("Ruijie switch", null, false)]
    [InlineData(null, null, false)]
    public void Well_known_wireless_controllers_are_recognised(string? descr, string? oid, bool expected) => WirelessApParser.LooksLikeController(descr, oid).Should().Be(expected);
}
