using System.Net;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain;

namespace SwitchPortFinder.Discovery.Parsing;

public static class ArpParser
{
    /// <summary>IP-MIB ipNetToPhysicalTable (preferred). Index = ifIndex.addrType.addrLen.addr…; skips invalid(5)/incomplete(7).</summary>
    public static List<ArpInfo> ParsePhysical(IEnumerable<SnmpVarBind> vbs)
    {
        var phys = new Dictionary<string, (int ifIndex, string ip, byte[] mac)>(); var state = new Dictionary<string, int>();
        foreach (var c in OidTable.Cells(vbs, Oids.IpNetToPhysicalTable))
        {
            if (c.Index.Length < 4) continue;
            var type = c.Index[1]; var len = c.Index[2];
            if (c.Index.Length != 3 + len || (type == 1 && len != 4) || (type == 2 && len != 16) || type is not (1 or 2)) continue;
            var addr = c.Index[3..].Select(i => (byte)i).ToArray();
            var key = string.Join('.', c.Index);
            if (c.Column == 4 && c.Value.Bytes is { Length: 6 } mac) phys[key] = (c.Index[0], new IPAddress(addr).ToString(), mac);
            else if (c.Column == 7) state[key] = (int)c.Value.Number;
        }
        return phys.Where(p => state.GetValueOrDefault(p.Key, 1) is not (5 or 7))
            .Select(p => new ArpInfo(p.Value.ip, MacAddress.FromBytes(p.Value.mac)!, p.Value.ifIndex))
            .Where(Valid).ToList();
    }

    /// <summary>IP-MIB ipNetToMediaTable (fallback, IPv4 only). Index = ifIndex.a.b.c.d; skips invalid(2).</summary>
    public static List<ArpInfo> ParseMedia(IEnumerable<SnmpVarBind> vbs)
    {
        var phys = new Dictionary<string, (int ifIndex, string ip, byte[] mac)>(); var type = new Dictionary<string, int>();
        foreach (var c in OidTable.Cells(vbs, Oids.IpNetToMediaTable))
        {
            if (c.Index.Length != 5) continue;
            var key = string.Join('.', c.Index);
            if (c.Column == 2 && c.Value.Bytes is { Length: 6 } mac) phys[key] = (c.Index[0], string.Join('.', c.Index[1..]), mac);
            else if (c.Column == 4) type[key] = (int)c.Value.Number;
        }
        return phys.Where(p => type.GetValueOrDefault(p.Key, 3) != 2)
            .Select(p => new ArpInfo(p.Value.ip, MacAddress.FromBytes(p.Value.mac)!, p.Value.ifIndex))
            .Where(Valid).ToList();
    }

    private static bool Valid(ArpInfo a) => !MacAddress.IsZero(a.Mac) && !MacAddress.IsMulticastOrBroadcast(a.Mac);
}
