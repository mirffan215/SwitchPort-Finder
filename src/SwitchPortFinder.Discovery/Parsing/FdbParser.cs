using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain;

namespace SwitchPortFinder.Discovery.Parsing;

/// <summary>Parses BRIDGE-MIB and Q-BRIDGE-MIB forwarding tables. FDB rows reference a BRIDGE PORT, never an ifIndex.</summary>
public static class FdbParser
{
    /// <summary>dot1dBasePortIfIndex: bridge port → ifIndex.</summary>
    public static Dictionary<int, int> BasePortMap(IEnumerable<SnmpVarBind> vbs) =>
        OidTable.Column(vbs, Oids.Dot1dBasePortIfIndex).GroupBy(c => c.Index).ToDictionary(g => g.Key, g => (int)g.First().Value.Number);

    /// <summary>dot1qVlanFdbId: fdbId → VLAN id. Index is timeMark.vlanId.</summary>
    public static Dictionary<int, int> FdbToVlan(IEnumerable<SnmpVarBind> vbs)
    {
        var map = new Dictionary<int, int>();
        foreach (var (vlan, v) in OidTable.Column(vbs, Oids.Dot1qVlanFdbId)) map[(int)v.Number] = vlan;
        return map;
    }

    /// <summary>dot1dTpFdbTable (index = 6 MAC octets). <paramref name="vlan"/> is set for per-VLAN (Cisco) instances.</summary>
    public static List<FdbEntry> ParseDot1d(IEnumerable<SnmpVarBind> vbs, IReadOnlyDictionary<int, int> portToIf, int? vlan)
    {
        var ports = new Dictionary<string, int>(); var status = new Dictionary<string, int>();
        foreach (var c in OidTable.Cells(vbs, Oids.Dot1dTpFdbTable))
        {
            if (c.Index.Length != 6) continue;
            var mac = MacFromIndex(c.Index);
            if (c.Column == 2) ports[mac] = (int)c.Value.Number;
            else if (c.Column == 3) status[mac] = (int)c.Value.Number;
        }
        return Build(ports, status, portToIf, _ => vlan);
    }

    /// <summary>dot1qTpFdbTable (index = fdbId + 6 MAC octets). fdbId is mapped to a VLAN where dot1qVlanFdbId is known, else used as the VLAN id.</summary>
    public static List<FdbEntry> ParseDot1q(IEnumerable<SnmpVarBind> vbs, IReadOnlyDictionary<int, int> portToIf, IReadOnlyDictionary<int, int> fdbToVlan)
    {
        var ports = new Dictionary<string, int>(); var status = new Dictionary<string, int>(); var fdbOf = new Dictionary<string, int>();
        foreach (var c in OidTable.Cells(vbs, Oids.Dot1qTpFdbTable))
        {
            if (c.Index.Length != 7) continue;
            var key = $"{c.Index[0]}|{MacFromIndex(c.Index[1..])}";
            fdbOf[key] = c.Index[0];
            if (c.Column == 2) ports[key] = (int)c.Value.Number;
            else if (c.Column == 3) status[key] = (int)c.Value.Number;
        }
        return Build(ports, status, portToIf, key =>
        {
            var fdb = fdbOf[key];
            return fdbToVlan.TryGetValue(fdb, out var v) ? v : fdb;
        }, key => key[(key.IndexOf('|') + 1)..]);
    }

    private static List<FdbEntry> Build(Dictionary<string, int> ports, Dictionary<string, int> status, IReadOnlyDictionary<int, int> portToIf,
        Func<string, int?> vlanOf, Func<string, string>? macOf = null)
    {
        var list = new List<FdbEntry>();
        foreach (var (key, port) in ports)
        {
            var mac = macOf?.Invoke(key) ?? key;
            var kind = status.GetValueOrDefault(key, 3) switch { 3 => FdbKind.Learned, 4 => FdbKind.Self, 5 => FdbKind.Static, 2 => FdbKind.Invalid, _ => FdbKind.Other };
            int? ifIndex = portToIf.TryGetValue(port, out var i) ? i : null;
            list.Add(new FdbEntry(mac, vlanOf(key), port, ifIndex, kind));
        }
        return list;
    }

    private static string MacFromIndex(int[] octets) => string.Concat(octets.Select(o => ((byte)o).ToString("X2")));

    /// <summary>Keeps only usable rows: learned (or static/mgmt, flagged) unicast MACs on a mapped port, excluding the switch's own MACs.</summary>
    public static List<FdbEntry> Usable(IEnumerable<FdbEntry> all, ISet<string> ownMacs)
    {
        var own = new HashSet<string>(ownMacs);
        foreach (var e in all.Where(e => e.Kind == FdbKind.Self)) own.Add(e.Mac);
        return all.Where(e => e.Kind is FdbKind.Learned or FdbKind.Static && e.IfIndex is not null && e.BridgePort != 0
                              && !own.Contains(e.Mac) && !MacAddress.IsMulticastOrBroadcast(e.Mac) && !MacAddress.IsZero(e.Mac))
            .GroupBy(e => (e.Mac, e.Vlan)).Select(g => g.First()).ToList();
    }
}
