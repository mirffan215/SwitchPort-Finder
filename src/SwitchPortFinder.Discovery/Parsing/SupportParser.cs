using System.Text.RegularExpressions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery.Parsing;

/// <summary>Supporting evidence for uplinks. It can NEVER classify a port on its own (spec 5.7 #4); the classifier only uses it to lower a threshold.</summary>
public static class SupportParser
{
    private static readonly Regex Hint = new(@"\b(uplink|up-link|trunk|backbone|downlink|inter-?switch|isl|po\d+|port-?channel|lag)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool DescriptionHint(string? alias, string? descr) =>
        (alias is { Length: > 0 } && Hint.IsMatch(alias)) || (descr is { Length: > 0 } && Hint.IsMatch(descr) && !Regex.IsMatch(descr, @"^\w+\d+/\d+(/\d+)?$"));

    /// <summary>Q-BRIDGE PortList: the most significant bit of the first octet is bridge port 1.</summary>
    public static IEnumerable<int> PortListBits(byte[]? bytes)
    {
        if (bytes is null) yield break;
        for (var i = 0; i < bytes.Length; i++)
            for (var bit = 0; bit < 8; bit++)
                if ((bytes[i] & (0x80 >> bit)) != 0) yield return i * 8 + bit + 1;
    }

    /// <summary>
    /// Per ifIndex: how many VLANs the port is a member of (static egress ports), and whether it is TAGGED in at least one
    /// (egress but not untagged) — the hallmark of a trunk.
    /// </summary>
    public static Dictionary<int, (int Members, int Tagged)> VlanMembership(IEnumerable<SnmpVarBind> egress, IEnumerable<SnmpVarBind> untagged, IReadOnlyDictionary<int, int> bridgeToIf)
    {
        var untaggedByVlan = OidTable.Column(untagged, Oids.Dot1qVlanStaticUntaggedPorts).ToDictionary(c => c.Index, c => PortListBits(c.Value.Bytes).ToHashSet());
        var result = new Dictionary<int, (int, int)>();
        foreach (var (vlan, v) in OidTable.Column(egress, Oids.Dot1qVlanStaticEgressPorts))
        {
            var un = untaggedByVlan.GetValueOrDefault(vlan) ?? [];
            foreach (var bp in PortListBits(v.Bytes))
            {
                if (!bridgeToIf.TryGetValue(bp, out var ifx)) continue;
                var (m, t) = result.GetValueOrDefault(ifx);
                result[ifx] = (m + 1, t + (un.Contains(bp) ? 0 : 1));
            }
        }
        return result;
    }

    public static Dictionary<int, SupportInfo> Build(IReadOnlyList<InterfaceInfo> ifaces, int? stpRootBridgePort, IReadOnlyDictionary<int, int> bridgeToIf,
        IReadOnlyDictionary<int, (int Members, int Tagged)> vlans)
    {
        var result = new Dictionary<int, SupportInfo>();
        int? rootIf = stpRootBridgePort is { } rp && rp > 0 && bridgeToIf.TryGetValue(rp, out var ri) ? ri : null;
        foreach (var i in ifaces)
        {
            var flags = PortSupport.None; var members = 0;
            if (rootIf == i.IfIndex) flags |= PortSupport.StpRootPort;
            if (vlans.TryGetValue(i.IfIndex, out var v)) { members = v.Members; if (v.Tagged >= 1 && v.Members >= 2) flags |= PortSupport.TaggedVlans; }
            if (DescriptionHint(i.Alias, i.Descr)) flags |= PortSupport.DescriptionHint;
            if (flags != PortSupport.None || members > 0) result[i.IfIndex] = new SupportInfo(flags, members);
        }
        return result;
    }
}
