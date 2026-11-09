using System.Net;
using System.Text;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery.Parsing;

public record LldpLocalPort(int Subtype, string? Id, string? IdMac, string? Descr);

/// <summary>LLDP-MIB and Cisco CDP-MIB parsing. Pure functions over walked varbinds.</summary>
public static class NeighborParser
{
    // ------------------------------------------------------------------ LLDP ------------------------------------------------------------------

    /// <summary>lldpLocPortTable: localPortNum → how this switch names its own port.</summary>
    public static Dictionary<int, LldpLocalPort> LocalPorts(IEnumerable<SnmpVarBind> vbs)
    {
        var rows = new Dictionary<int, (int sub, byte[]? id, string? desc)>();
        foreach (var c in OidTable.Cells(vbs, Oids.LldpLocPortTable).Where(c => c.Index.Length == 1))
        {
            rows.TryGetValue(c.Index[0], out var r);
            if (c.Column == 2) r.sub = (int)c.Value.Number;
            else if (c.Column == 3) r.id = c.Value.Bytes;
            else if (c.Column == 4) r.desc = c.Value.Text;
            rows[c.Index[0]] = r;
        }
        return rows.ToDictionary(kv => kv.Key, kv =>
        {
            var (text, mac) = DecodePortId(kv.Value.sub, kv.Value.id);
            return new LldpLocalPort(kv.Value.sub, text, mac, kv.Value.desc);
        });
    }

    /// <summary>Maps lldpRemLocalPortNum to an ifIndex using the local port's id subtype (name / MAC / local number), description, or the number itself.</summary>
    public static int? MapLocalPort(int localPortNum, IReadOnlyDictionary<int, LldpLocalPort> locPorts, IReadOnlyList<InterfaceInfo> ifaces)
    {
        if (locPorts.TryGetValue(localPortNum, out var lp))
        {
            if (lp.IdMac is not null && ifaces.FirstOrDefault(i => i.Mac == lp.IdMac) is { } byMac) return byMac.IfIndex;
            if (lp.Id is not null)
            {
                if (lp.Subtype == 7 && int.TryParse(lp.Id, out var n) && ifaces.Any(i => i.IfIndex == n)) return n;
                if (ifaces.FirstOrDefault(i => string.Equals(i.Name, lp.Id, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Descr, lp.Id, StringComparison.OrdinalIgnoreCase)) is { } byName) return byName.IfIndex;
            }
            if (lp.Descr is { Length: > 0 } && ifaces.FirstOrDefault(i => string.Equals(i.Name, lp.Descr, StringComparison.OrdinalIgnoreCase) || string.Equals(i.Descr, lp.Descr, StringComparison.OrdinalIgnoreCase)) is { } byDesc) return byDesc.IfIndex;
        }
        return ifaces.Any(i => i.IfIndex == localPortNum) ? localPortNum : null;
    }

    /// <summary>lldpRemTable (+ management addresses). Index = timeMark.localPortNum.remIndex.</summary>
    public static List<NeighborInfo> Lldp(IEnumerable<SnmpVarBind> remTable, IEnumerable<SnmpVarBind> manAddrTable,
        IReadOnlyDictionary<int, LldpLocalPort> locPorts, IReadOnlyList<InterfaceInfo> ifaces)
    {
        var mgmt = new Dictionary<(int, int), string>();
        foreach (var c in OidTable.Cells(manAddrTable, Oids.LldpRemManAddrTable))
        {
            // index: timeMark.localPort.remIndex.addrSubtype.addrLen.addr…  (IPv4 = subtype 1, len 4)
            if (c.Index.Length >= 9 && c.Index[3] == 1 && c.Index[4] == 4)
                mgmt.TryAdd((c.Index[1], c.Index[2]), string.Join('.', c.Index[5..9]));
        }
        var rows = new Dictionary<(int, int), Dictionary<int, SnmpValue>>();
        foreach (var c in OidTable.Cells(remTable, Oids.LldpRemTable).Where(c => c.Index.Length == 3))
        {
            var key = (c.Index[1], c.Index[2]);
            if (!rows.TryGetValue(key, out var d)) rows[key] = d = [];
            d[c.Column] = c.Value;
        }
        var result = new List<NeighborInfo>();
        foreach (var ((local, remIdx), d) in rows.OrderBy(r => r.Key))
        {
            var ifIndex = MapLocalPort(local, locPorts, ifaces);
            if (ifIndex is null) continue;
            var (chassis, chassisMac, chassisIp) = DecodeChassisId((int)(d.GetValueOrDefault(4)?.Number ?? 0), d.GetValueOrDefault(5)?.Bytes);
            var (port, portMac) = DecodePortId((int)(d.GetValueOrDefault(6)?.Number ?? 0), d.GetValueOrDefault(7)?.Bytes);
            // Capabilities: prefer the *enabled* set (col 12); fall back to *supported* (col 11).
            var caps = Caps(d.GetValueOrDefault(12)?.Bytes is { Length: > 0 } en ? en : d.GetValueOrDefault(11)?.Bytes);
            result.Add(new NeighborInfo(NeighborProtocol.Lldp, ifIndex.Value, chassis, chassisMac, portMac is not null ? Domain.MacAddress.Format(portMac) : port,
                Clean(d.GetValueOrDefault(8)), Clean(d.GetValueOrDefault(9)), Clean(d.GetValueOrDefault(10)), null, caps,
                mgmt.GetValueOrDefault((local, remIdx)) ?? chassisIp));
        }
        return result;
    }

    /// <summary>LLDP "bits": bit0 is the MSB of the first octet. bit2 = bridge, bit4 = router (IEEE 802.1AB).</summary>
    public static NeighborCaps Caps(byte[]? bits)
    {
        if (bits is not { Length: > 0 }) return NeighborCaps.None;
        var caps = NeighborCaps.None;
        for (var i = 0; i < 8; i++) if ((bits[0] & (0x80 >> i)) != 0) caps |= (NeighborCaps)(1 << i);
        return caps;
    }

    /// <summary>Chassis ID subtypes: 4 = MAC, 5 = network address, 1/2/3/6/7 = text-ish.</summary>
    public static (string? Display, string? Mac, string? Ip) DecodeChassisId(int subtype, byte[]? b)
    {
        if (b is null || b.Length == 0) return (null, null, null);
        switch (subtype)
        {
            case 4 when b.Length == 6: var m = Domain.MacAddress.FromBytes(b); return (Domain.MacAddress.Format(m!), m, null);
            case 5 when b.Length == 5 && b[0] == 1: var ip = new IPAddress(b[1..]).ToString(); return (ip, null, ip);
            default:
                var t = Text(b);
                return (t, Domain.MacAddress.Normalize(t) is { } nm && !t.Contains(' ') && t.Length is 12 or 14 or 17 ? nm : null, null);
        }
    }

    /// <summary>Port ID subtypes: 3 = MAC, 4 = network address, 1/5/7 = alias / interface name / locally assigned.</summary>
    public static (string? Display, string? Mac) DecodePortId(int subtype, byte[]? b)
    {
        if (b is null || b.Length == 0) return (null, null);
        if (subtype == 3 && b.Length == 6) { var m = Domain.MacAddress.FromBytes(b); return (Domain.MacAddress.Format(m!), m); }
        if (subtype == 4 && b.Length == 5 && b[0] == 1) return (new IPAddress(b[1..]).ToString(), null);
        return (Text(b), null);
    }

    // ------------------------------------------------------------------ CDP -------------------------------------------------------------------

    /// <summary>cdpCacheTable. Index = ifIndex.deviceIndex. Capabilities are a big-endian bitmask mapped onto the LLDP flags.</summary>
    public static List<NeighborInfo> Cdp(IEnumerable<SnmpVarBind> vbs, IReadOnlyList<InterfaceInfo> ifaces)
    {
        var rows = new Dictionary<(int, int), Dictionary<int, SnmpValue>>();
        foreach (var c in OidTable.Cells(vbs, Oids.CdpCacheTable).Where(c => c.Index.Length == 2))
        {
            if (!rows.TryGetValue((c.Index[0], c.Index[1]), out var d)) rows[(c.Index[0], c.Index[1])] = d = [];
            d[c.Column] = c.Value;
        }
        var result = new List<NeighborInfo>();
        foreach (var ((ifIndex, _), d) in rows.OrderBy(r => r.Key))
        {
            if (!ifaces.Any(i => i.IfIndex == ifIndex)) continue;
            string? ip = d.GetValueOrDefault(4)?.Bytes is { Length: 4 } a ? new IPAddress(a).ToString() : null;
            var platform = Clean(d.GetValueOrDefault(8));
            var caps = CdpCaps(d.GetValueOrDefault(9)?.Bytes);
            if (platform?.Contains("Phone", StringComparison.OrdinalIgnoreCase) == true) caps |= NeighborCaps.Telephone;
            var deviceId = Clean(d.GetValueOrDefault(6));
            result.Add(new NeighborInfo(NeighborProtocol.Cdp, ifIndex, deviceId, null, Clean(d.GetValueOrDefault(7)), null, deviceId, Clean(d.GetValueOrDefault(5)), platform, caps, ip));
        }
        return result;
    }

    public static NeighborCaps CdpCaps(byte[]? b)
    {
        if (b is not { Length: > 0 }) return NeighborCaps.None;
        long v = 0; foreach (var x in b) v = (v << 8) | x;
        var caps = NeighborCaps.None;
        if ((v & 0x01) != 0) caps |= NeighborCaps.Router;
        if ((v & 0x02) != 0 || (v & 0x04) != 0 || (v & 0x08) != 0) caps |= NeighborCaps.Bridge;   // transparent / source-route bridge / switch
        if ((v & 0x10) != 0) caps |= NeighborCaps.Station;                                          // host
        if ((v & 0x40) != 0) caps |= NeighborCaps.Repeater;
        return caps;
    }

    private static string? Clean(SnmpValue? v) => v is { IsMissing: false } && v.Text is { } t && !string.IsNullOrWhiteSpace(t) ? t.Trim().TrimEnd('\0') : null;
    private static string Text(byte[] b) => Encoding.UTF8.GetString(b).TrimEnd('\0');
}
