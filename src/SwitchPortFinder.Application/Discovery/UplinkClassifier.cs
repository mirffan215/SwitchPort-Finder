using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public record SwitchRef(int Id, string Name, string Ip, string? SysName, IReadOnlySet<string> Macs);
public record PortRef(int PortId, int SwitchId, int IfIndex, string Name, bool OperUp);
public record NeighborRef(long Id, int SwitchId, int PortId, NeighborProtocol Protocol, string? ChassisMac, string? MgmtIp, string? SysName, NeighborCaps Caps);
public record ObsRef(int SwitchId, int PortId, string Mac);

/// <summary>Outcome for one port. <see cref="InferredTo"/> is set when the link comes from the FDB heuristic (no LLDP/CDP row exists).</summary>
public record PortVerdict(
    int PortId, PortType Type, NeighborConfidence Confidence, string? Evidence, int? ConnectedSwitchId, long? NeighborId, bool Inferred);

public record ClassificationResult(IReadOnlyList<PortVerdict> Ports, IReadOnlyDictionary<long, int?> NeighborMatches);

/// <summary>
/// Pure switch-to-switch classification. Evidence, strongest first (spec 5.7):
///  1. LLDP/CDP neighbor with bridge/router capability that matches a registered switch → Confirmed Switch (linked)
///  2. LLDP/CDP bridge/router neighbor not (yet) registered → Confirmed Switch (external, no link)
///  3. FDB heuristic (works with LLDP/CDP off): the port learned another registered switch's own MAC, or most of its MACs are
///     also learned on edge-like ports of other switches → Likely Switch
///  5. phone / WLAN AP / station neighbors → Endpoint
/// Weak evidence never produces a switch link.
/// </summary>
public static class UplinkClassifier
{
    public static ClassificationResult Classify(
        IReadOnlyList<SwitchRef> switches, IReadOnlyList<PortRef> ports, IReadOnlyList<NeighborRef> neighbors, IReadOnlyList<ObsRef> observations,
        int minMacs = 3, double ratio = 0.6)
    {
        var byIp = switches.GroupBy(s => s.Ip).ToDictionary(g => g.Key, g => g.First());
        var byName = new Dictionary<string, SwitchRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in switches) { foreach (var n in new[] { s.Name, s.SysName }) if (Short(n) is { } k) byName.TryAdd(k, s); }
        var macOwner = new Dictionary<string, SwitchRef>();
        foreach (var s in switches) foreach (var m in s.Macs) macOwner.TryAdd(m, s);

        // ---- 1/2/5: LLDP & CDP neighbors ----
        var matches = new Dictionary<long, int?>();
        var verdicts = new Dictionary<int, PortVerdict>();
        foreach (var g in neighbors.Where(n => n.Protocol != NeighborProtocol.Inferred).GroupBy(n => n.PortId))
        {
            var port = ports.FirstOrDefault(p => p.PortId == g.Key);
            if (port is null) continue;
            PortVerdict? best = null;
            foreach (var n in g)
            {
                var match = Match(n, port.SwitchId);
                matches[n.Id] = match?.Id;
                var switchCaps = (n.Caps & (NeighborCaps.Bridge | NeighborCaps.Router)) != 0;
                var endpointCaps = (n.Caps & (NeighborCaps.Telephone | NeighborCaps.WlanAp)) != 0;
                PortVerdict? v = null;
                if (match is not null)
                    v = new PortVerdict(port.PortId, PortType.SwitchUplink, NeighborConfidence.ConfirmedSwitch,
                        $"{n.Protocol} neighbor {n.SysName ?? n.MgmtIp ?? n.ChassisMac} matches registered switch {match.Name}", match.Id, n.Id, false);
                else if (switchCaps && !endpointCaps)
                    v = new PortVerdict(port.PortId, PortType.SwitchUplink, NeighborConfidence.ConfirmedSwitch,
                        $"{n.Protocol} neighbor {n.SysName ?? n.MgmtIp ?? n.ChassisMac} advertises bridge/router capability (not registered)", null, n.Id, false);
                else if (endpointCaps || n.Caps.HasFlag(NeighborCaps.Station))
                    v = new PortVerdict(port.PortId, PortType.Endpoint, NeighborConfidence.Endpoint, $"{n.Protocol} neighbor is a phone / access point / station", null, n.Id, false);
                if (v is not null && (best is null || Rank(v) > Rank(best))) best = v;
            }
            if (best is not null) verdicts[port.PortId] = best;
        }

        // ---- 3: FDB heuristic for ports without LLDP/CDP proof ----
        var macsByPort = observations.GroupBy(o => o.PortId).ToDictionary(g => g.Key, g => g.Select(o => o.Mac).ToHashSet());
        var obsByMac = observations.ToLookup(o => o.Mac);
        foreach (var p in ports)
        {
            if (!p.OperUp || verdicts.TryGetValue(p.PortId, out var existing) && existing.Confidence != NeighborConfidence.Endpoint) continue;
            if (!macsByPort.TryGetValue(p.PortId, out var macs) || macs.Count == 0) continue;
            if (verdicts.ContainsKey(p.PortId)) continue;      // LLDP says phone/AP: trust it, do not guess a switch

            // 3a: this port learned the MAC of another registered switch.
            var owners = macs.Select(m => macOwner.GetValueOrDefault(m)).Where(o => o is not null && o.Id != p.SwitchId).GroupBy(o => o!.Id).OrderByDescending(g => g.Count()).ToList();
            if (owners.Count > 0)
            {
                var x = owners[0].First()!;
                verdicts[p.PortId] = new PortVerdict(p.PortId, PortType.SwitchUplink, NeighborConfidence.LikelySwitch,
                    $"MAC table: this port learned {owners[0].Count()} MAC address(es) belonging to switch {x.Name}", x.Id, null, true);
                continue;
            }

            // 3b: unusually many MACs that are ALSO learned on edge-like ports (fewer MACs) of other switches.
            if (macs.Count < minMacs) continue;
            var elsewhere = new List<ObsRef>();
            foreach (var m in macs)
            {
                var edge = obsByMac[m].Where(o => o.SwitchId != p.SwitchId && macsByPort[o.PortId].Count < macs.Count).OrderBy(o => macsByPort[o.PortId].Count).FirstOrDefault();
                if (edge is not null) elsewhere.Add(edge);
            }
            if (elsewhere.Count >= minMacs - 1 && elsewhere.Count >= ratio * macs.Count)
            {
                var top = elsewhere.GroupBy(o => o.SwitchId).OrderByDescending(g => g.Count()).First();
                int? linked = top.Count() >= ratio * elsewhere.Count ? top.Key : null;
                verdicts[p.PortId] = new PortVerdict(p.PortId, PortType.SwitchUplink, NeighborConfidence.LikelySwitch,
                    $"MAC table: {elsewhere.Count} of {macs.Count} MACs on this port are also learned on edge ports of other switches", linked, null, true);
            }
        }

        // ---- final per-port result ----
        var result = new List<PortVerdict>();
        foreach (var p in ports)
        {
            if (!p.OperUp) { result.Add(new PortVerdict(p.PortId, PortType.Down, NeighborConfidence.Unknown, null, null, null, false)); continue; }
            if (verdicts.TryGetValue(p.PortId, out var v)) { result.Add(v); continue; }
            var hasMacs = macsByPort.ContainsKey(p.PortId);
            result.Add(new PortVerdict(p.PortId, hasMacs ? PortType.Endpoint : PortType.Unknown, NeighborConfidence.Unknown, null, null, null, false));
        }
        return new ClassificationResult(result, matches);

        SwitchRef? Match(NeighborRef n, int localSwitchId)
        {
            SwitchRef? m = null;
            if (n.MgmtIp is not null && byIp.TryGetValue(n.MgmtIp, out var a)) m = a;
            else if (n.ChassisMac is not null && macOwner.TryGetValue(n.ChassisMac, out var b)) m = b;
            else if (Short(n.SysName) is { } k && byName.TryGetValue(k, out var c)) m = c;
            return m is not null && m.Id != localSwitchId ? m : null;
        }
    }

    private static int Rank(PortVerdict v) => v.Type == PortType.SwitchUplink ? 3 : v.Type == PortType.Endpoint ? 2 : 1;

    /// <summary>"core-sw-01.corp.example" → "core-sw-01" so FQDN and short names compare equal.</summary>
    public static string? Short(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var t = name.Trim();
        var i = t.IndexOf('.');
        // Do not shorten IP-looking strings.
        return i > 0 && !char.IsDigit(t[0]) ? t[..i] : t;
    }
}

/// <summary>
/// Authoritative device location (spec 5.8): only ports that are NOT switch uplinks count. If several edge candidates remain, the one with the
/// fewest MACs on its port wins and the result is flagged lower confidence.
/// </summary>
public static class LocationSelector
{
    public record Choice(ObsCandidate? Best, bool LowConfidence);
    public record ObsCandidate(int SwitchId, int PortId, int? Vlan, DateTime LastSeen);

    public static Choice Choose(IEnumerable<ObsCandidate> sightings, Func<int, bool> isUplink, Func<int, int> macCountOnPort, TimeSpan recencyWindow)
    {
        var all = sightings.ToList();
        if (all.Count == 0) return new Choice(null, false);
        var newest = all.Max(o => o.LastSeen);
        var edge = all.Where(o => !isUplink(o.PortId) && newest - o.LastSeen <= recencyWindow).ToList();
        if (edge.Count == 0) return new Choice(null, false);   // only ever seen behind uplinks → location not provable yet
        var best = edge.OrderBy(o => macCountOnPort(o.PortId)).ThenByDescending(o => o.LastSeen).First();
        return new Choice(best, edge.Count > 1);
    }
}
