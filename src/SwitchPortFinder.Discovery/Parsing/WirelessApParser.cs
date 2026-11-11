using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Discovery.Parsing;

/// <summary>
/// Reads a Cisco wireless controller's access-point tables. Both tables are indexed by the AP's MAC address written as six decimal sub-identifiers.
/// Pure functions over varbind lists: tested without a network.
/// </summary>
public static class WirelessApParser
{
    /// <summary>"AA:BB.." from the index "170.187.204.0.17.34" (six sub-identifiers 0-255) or null.</summary>
    public static string? MacFromIndex(string index)
    {
        var p = index.Split('.'); if (p.Length != 6) return null;
        var bytes = new byte[6];
        for (var i = 0; i < 6; i++) if (!byte.TryParse(p[i], out bytes[i])) return null;
        return Convert.ToHexString(bytes);
    }

    /// <summary>index (the part of the OID after the column root) -> value, for one column walk.</summary>
    public static Dictionary<string, SnmpValue> ByIndex(IEnumerable<SnmpVarBind> walk, string columnRoot)
    {
        var prefix = columnRoot.TrimStart('.') + ".";
        var d = new Dictionary<string, SnmpValue>();
        foreach (var vb in walk) if (!vb.Value.IsMissing && vb.Oid.StartsWith(prefix, StringComparison.Ordinal)) d[vb.Oid[prefix.Length..]] = vb.Value;
        return d;
    }

    private static string? Mac(SnmpValue? v) => v?.Bytes is { Length: 6 } b ? Convert.ToHexString(b) : null;
    private static string? Text(SnmpValue? v) => v is { IsMissing: false } && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString().Trim() : null;

    /// <summary>CISCO-LWAPP-AP-MIB (Catalyst 9800 and AireOS 8.x). Columns: 2 Ethernet MAC, 5 name, 6 uptime (TimeTicks), 38 admin status (1 = true), 54 associated clients.</summary>
    public static List<WirelessApInfo> Lwapp(IReadOnlyDictionary<int, IReadOnlyList<SnmpVarBind>> columns)
    {
        IReadOnlyList<SnmpVarBind> Col(int c) => columns.TryGetValue(c, out var l) ? l : [];
        string R(int c) => $"{Oids.LwappApEntry}.{c}";
        var eth = ByIndex(Col(2), R(2)); var name = ByIndex(Col(5), R(5)); var up = ByIndex(Col(6), R(6)); var adm = ByIndex(Col(38), R(38)); var cli = ByIndex(Col(54), R(54));
        var indexes = eth.Keys.Concat(name.Keys).Distinct().ToList();
        var list = new List<WirelessApInfo>();
        foreach (var idx in indexes)
        {
            if (MacFromIndex(idx) is not { } sys) continue;
            var ethMac = Mac(eth.GetValueOrDefault(idx));
            list.Add(new WirelessApInfo(sys, ethMac is null || ethMac == "000000000000" ? null : ethMac, Text(name.GetValueOrDefault(idx)), null, null, null,
                up.TryGetValue(idx, out var u) ? u.Number / 100 : null, cli.TryGetValue(idx, out var c) ? (int)c.Number : null, adm.TryGetValue(idx, out var a) ? a.Number == 1 : null, null));
        }
        return list;
    }

    /// <summary>AIRESPACE-WIRELESS-MIB bsnAPTable (older AireOS). Columns: 3 name, 6 operation status (1 = associated), 16 model, 17 serial, 19 IP address, 33 Ethernet MAC.</summary>
    public static List<WirelessApInfo> Bsn(IReadOnlyDictionary<int, IReadOnlyList<SnmpVarBind>> columns)
    {
        IReadOnlyList<SnmpVarBind> Col(int c) => columns.TryGetValue(c, out var l) ? l : [];
        string R(int c) => $"{Oids.BsnApEntry}.{c}";
        var name = ByIndex(Col(3), R(3)); var op = ByIndex(Col(6), R(6)); var model = ByIndex(Col(16), R(16)); var serial = ByIndex(Col(17), R(17)); var ip = ByIndex(Col(19), R(19)); var eth = ByIndex(Col(33), R(33));
        var list = new List<WirelessApInfo>();
        foreach (var idx in name.Keys.Concat(op.Keys).Concat(eth.Keys).Distinct().ToList())
        {
            if (MacFromIndex(idx) is not { } sys) continue;
            var ethMac = Mac(eth.GetValueOrDefault(idx)); var addr = Text(ip.GetValueOrDefault(idx));
            list.Add(new WirelessApInfo(sys, ethMac is null || ethMac == "000000000000" ? null : ethMac, Text(name.GetValueOrDefault(idx)), Text(model.GetValueOrDefault(idx)), Text(serial.GetValueOrDefault(idx)),
                addr is "0.0.0.0" ? null : addr, null, null, null, op.TryGetValue(idx, out var o) ? o.Number == 1 : null));
        }
        return list;
    }
}
