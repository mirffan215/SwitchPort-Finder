using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Discovery.Parsing;

public static class InterfaceParser
{
    // ifType values treated as switch ports: ethernetCsmacd, fastEther, fastEtherFX, gigabitEthernet, ieee8023adLag
    private static readonly HashSet<int> PortTypes = [6, 62, 69, 117, 161];

    public static List<InterfaceInfo> Parse(IEnumerable<SnmpVarBind> ifTable, IEnumerable<SnmpVarBind> ifXTable)
    {
        var x = OidTable.Cells(ifXTable, Oids.IfXTable).Where(c => c.Index.Length == 1)
            .GroupBy(c => c.Index[0]).ToDictionary(g => g.Key, g => g.ToDictionary(c => c.Column, c => c.Value));
        var result = new List<InterfaceInfo>();
        foreach (var row in OidTable.Cells(ifTable, Oids.IfTable).Where(c => c.Index.Length == 1).GroupBy(c => c.Index[0]).OrderBy(g => g.Key))
        {
            var col = row.ToDictionary(c => c.Column, c => c.Value);
            var type = (int)(col.GetValueOrDefault(3)?.Number ?? 0);
            if (!PortTypes.Contains(type)) continue;
            x.TryGetValue(row.Key, out var xc);
            var descr = Text(col.GetValueOrDefault(2));
            var name = Text(xc?.GetValueOrDefault(1)) ?? descr ?? $"if{row.Key}";   // ifXTable name preferred
            var speed = xc?.GetValueOrDefault(15) is { IsMissing: false } hs && hs.Number > 0 ? hs.Number
                : (col.GetValueOrDefault(5)?.Number ?? 0) / 1_000_000;                // fall back to ifSpeed (bits/s)
            string? mac = col.GetValueOrDefault(6)?.Bytes is { Length: 6 } b ? Domain.MacAddress.FromBytes(b) : null;
            result.Add(new InterfaceInfo(row.Key, name, descr, Text(xc?.GetValueOrDefault(18)),
                col.GetValueOrDefault(7)?.Number == 1, col.GetValueOrDefault(8)?.Number == 1, speed, mac,
                (int)(col.GetValueOrDefault(4)?.Number ?? 0), type));
        }
        return result;
    }

    private static string? Text(SnmpValue? v) => v is { IsMissing: false } && !string.IsNullOrWhiteSpace(v.ToString()) ? v.ToString().Trim() : null;
}
