using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public record TopoSwitch(int Id, string Name, string Ip, string Vendor, string Status, bool IsL3, int Ports, int Uplinks, int Devices);

/// <summary>An uplink port with the neighbor it leads to. Either <see cref="ToSwitchId"/> (registered) or <see cref="ExternalKey"/> (unregistered bridge) is set.</summary>
public record TopoLink(
    int FromSwitchId, int FromPortId, string FromPort, int FromIfIndex, int? ToSwitchId, string? ExternalKey, string? ExternalName, string? ExternalIp,
    string? RemotePort, int? RemotePortIfIndex, NeighborConfidence Confidence, NeighborProtocol Protocol);

public record TopoNode(string Id, int? SwitchId, string Name, string? Ip, string? Vendor, string? Status, bool External, bool IsRoot, int Level, int Order, int Ports, int Uplinks, int Devices);

public record TopoEdge(
    string Id, string Source, string Target, int SourceSwitchId, int SourcePortId, string SourcePort, int SourceIfIndex,
    int? TargetSwitchId, string? TargetPort, int? TargetIfIndex, NeighborConfidence Confidence, NeighborProtocol Protocol);

public record TopologyResponse(IReadOnlyList<TopoNode> Nodes, IReadOnlyList<TopoEdge> Edges);

/// <summary>
/// Builds the simple switches-and-uplinks graph. Reverse links (A→B and B→A) are paired into ONE edge carrying both port names. Unregistered
/// bridge neighbors appear as external nodes. Levels (for layout) come from a BFS from each component's root (Layer 3 / ARP source first).
/// </summary>
public static class TopologyBuilder
{
    public static TopologyResponse Build(IReadOnlyList<TopoSwitch> switches, IReadOnlyList<TopoLink> links)
    {
        var nodes = new Dictionary<string, TopoNode>();
        foreach (var s in switches) nodes[$"s{s.Id}"] = new TopoNode($"s{s.Id}", s.Id, s.Name, s.Ip, s.Vendor, s.Status, false, false, 0, 0, s.Ports, s.Uplinks, s.Devices);
        var known = switches.ToDictionary(s => s.Id);

        // Resolve the far end of each link to a node id (registered switch or external ghost).
        static string Ghost(TopoLink l) => $"x:{l.ExternalKey}";
        var resolved = new List<(TopoLink L, string Target)>();
        foreach (var l in links.Where(l => known.ContainsKey(l.FromSwitchId)))
        {
            if (l.ToSwitchId is { } t && known.ContainsKey(t) && t != l.FromSwitchId) resolved.Add((l, $"s{t}"));
            else if (l.ExternalKey is not null && l.ToSwitchId is null)
            {
                resolved.Add((l, Ghost(l)));
                if (!nodes.ContainsKey(Ghost(l))) nodes[Ghost(l)] = new TopoNode(Ghost(l), null, l.ExternalName ?? l.ExternalIp ?? "Unmanaged switch", l.ExternalIp, null, null, true, false, 0, 0, 0, 0, 0);
            }
        }

        // Pair reverse links between the same two registered switches into one edge.
        var edges = new List<TopoEdge>(); var used = new HashSet<TopoLink>();
        foreach (var (l, target) in resolved.OrderBy(r => r.L.FromSwitchId).ThenBy(r => r.L.FromIfIndex))
        {
            if (used.Contains(l)) continue;
            used.Add(l);
            TopoLink? back = null;
            if (l.ToSwitchId is { } to)
            {
                back = resolved.Where(r => !used.Contains(r.L) && r.L.FromSwitchId == to && r.L.ToSwitchId == l.FromSwitchId)
                    // prefer the reverse link that names this very port; otherwise the first remaining one
                    .OrderByDescending(r => string.Equals(r.L.RemotePort, l.FromPort, StringComparison.OrdinalIgnoreCase) || r.L.RemotePortIfIndex == l.FromIfIndex || l.RemotePortIfIndex == r.L.FromIfIndex)
                    .ThenBy(r => r.L.FromIfIndex).Select(r => r.L).FirstOrDefault();
                if (back is not null) used.Add(back);
            }
            var targetPort = back?.FromPort ?? l.RemotePort; var targetIf = back?.FromIfIndex ?? l.RemotePortIfIndex;
            edges.Add(new TopoEdge($"e{edges.Count}", $"s{l.FromSwitchId}", target, l.FromSwitchId, l.FromPortId, l.FromPort, l.FromIfIndex, l.ToSwitchId, targetPort, targetIf,
                back is not null && back.Confidence < l.Confidence ? back.Confidence : l.Confidence, l.Protocol));
        }

        // Levels: BFS from each component's root (L3 first, then best connected).
        var adj = nodes.Keys.ToDictionary(k => k, _ => new HashSet<string>());
        foreach (var e in edges) { adj[e.Source].Add(e.Target); adj[e.Target].Add(e.Source); }
        var level = new Dictionary<string, int>(); var roots = new HashSet<string>(); var order = new Dictionary<int, int>(); var nextBand = 0;
        foreach (var start in nodes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (level.ContainsKey(start)) continue;
            var comp = new List<string>(); var q = new Queue<string>([start]); var seen = new HashSet<string> { start };
            while (q.Count > 0) { var c = q.Dequeue(); comp.Add(c); foreach (var n in adj[c]) if (seen.Add(n)) q.Enqueue(n); }
            var root = comp.OrderByDescending(k => nodes[k].SwitchId is { } id && known[id].IsL3 ? 1 : 0).ThenByDescending(k => adj[k].Count).ThenBy(k => k, StringComparer.Ordinal).First();
            roots.Add(root); level[root] = 0; q = new Queue<string>([root]);
            while (q.Count > 0) { var c = q.Dequeue(); foreach (var n in adj[c].OrderBy(x => x, StringComparer.Ordinal)) if (!level.ContainsKey(n)) { level[n] = level[c] + 1; q.Enqueue(n); } }
            nextBand++;
        }
        var result = new List<TopoNode>(); var perLevel = new Dictionary<int, int>();
        foreach (var n in nodes.Values.OrderBy(n => level[n.Id]).ThenBy(n => n.Name, StringComparer.OrdinalIgnoreCase))
        {
            perLevel[level[n.Id]] = perLevel.GetValueOrDefault(level[n.Id]);
            result.Add(n with { Level = level[n.Id], Order = perLevel[level[n.Id]]++, IsRoot = roots.Contains(n.Id) && adj[n.Id].Count > 0 });
        }
        return new TopologyResponse(result, edges);
    }
}

public record DashboardDto(
    int Switches, int Devices, int ConnectedPorts, int SwitchUplinks, int OfflineDevices, JobSummaryDto? LastDiscovery,
    IReadOnlyList<JobSummaryDto> RecentJobs, IReadOnlyList<DeviceDto> RecentDevices, int TotalPorts = 0, int DownPorts = 0);
