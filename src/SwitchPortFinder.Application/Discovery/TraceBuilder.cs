using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public record TraceSwitch(int Id, string Name, string Ip, bool IsL3);

/// <summary>A proven switch-to-switch link seen from <see cref="FromSwitchId"/>'s uplink port.</summary>
public record TraceLink(int FromSwitchId, int FromPortId, string FromPortName, int FromIfIndex, int ToSwitchId, NeighborConfidence Confidence, string? RemotePortName, int? RemotePortIfIndex);

public record TraceDevice(int Id, string Hostname, string? Ip, string Mac, int? SwitchId, int? PortId, string? PortName, int? IfIndex, int? Vlan, bool LowConfidence);

public record TraceHop(int SwitchId, string SwitchName, string SwitchIp, int? PortId, string? PortName, int? IfIndex, NeighborConfidence? LinkConfidence, bool IsFinal);

public record FinalLocation(int DeviceId, string Hostname, string? Ip, string Mac, int SwitchId, string SwitchName, int? PortId, string? PortName, int? IfIndex, int? Vlan, bool LowConfidence);

public record TraceResult(IReadOnlyList<TraceHop> Hops, FinalLocation? Final, bool Partial, string? Message);

/// <summary>
/// Spec 5.9. From the device's authoritative edge location (S_n, P_n) walk the proven uplink graph from the root (ARP source / Layer 3 core, else the
/// best-connected switch) down to S_n. Links are never invented: if a hop cannot be proven the known hops are returned and the result is marked partial.
/// </summary>
public static class TraceBuilder
{
    public const string PartialMessage = "Path partially determined";

    public static TraceResult Build(TraceDevice device, IReadOnlyList<TraceSwitch> switches, IReadOnlyList<TraceLink> links)
    {
        if (device.SwitchId is not { } sn || switches.FirstOrDefault(s => s.Id == sn) is not { } target)
            return new TraceResult([], null, true, "This device has not been seen on an edge port yet, so its location cannot be traced. Scan the switch it connects to.");

        var final = new FinalLocation(device.Id, device.Hostname, device.Ip, device.Mac, target.Id, target.Name, device.PortId, device.PortName, device.IfIndex, device.Vlan, device.LowConfidence);
        TraceHop FinalHop() => new(target.Id, target.Name, target.Ip, device.PortId, device.PortName, device.IfIndex, null, true);

        // Undirected adjacency between registered switches; ignore self links.
        var byId = switches.ToDictionary(s => s.Id);
        var adj = new Dictionary<int, HashSet<int>>();
        foreach (var l in links.Where(l => l.FromSwitchId != l.ToSwitchId && byId.ContainsKey(l.FromSwitchId) && byId.ContainsKey(l.ToSwitchId)))
        {
            (adj.TryGetValue(l.FromSwitchId, out var a) ? a : adj[l.FromSwitchId] = []).Add(l.ToSwitchId);
            (adj.TryGetValue(l.ToSwitchId, out var b) ? b : adj[l.ToSwitchId] = []).Add(l.FromSwitchId);
        }

        // Connected component of the target.
        var comp = new HashSet<int> { sn }; var queue = new Queue<int>([sn]);
        while (queue.Count > 0) foreach (var n in adj.GetValueOrDefault(queue.Dequeue()) ?? []) if (comp.Add(n)) queue.Enqueue(n);

        // Root: an L3/ARP-source switch in the component (best connected), else the best-connected switch.
        int Degree(int id) => adj.GetValueOrDefault(id)?.Count ?? 0;
        var l3InComp = comp.Where(id => byId[id].IsL3).ToList();
        var root = (l3InComp.Count > 0 ? l3InComp : comp.ToList()).OrderByDescending(Degree).ThenBy(id => id).First();

        // Shortest path root → target (BFS; cycle-safe).
        var prev = new Dictionary<int, int> { [root] = root }; queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var n in (adj.GetValueOrDefault(cur) ?? []).OrderBy(x => x)) if (!prev.ContainsKey(n)) { prev[n] = cur; queue.Enqueue(n); }
        }
        var path = new List<int>();
        for (var at = sn; ; at = prev[at]) { path.Add(at); if (at == root) break; }
        path.Reverse();

        var hops = new List<TraceHop>(); var notes = new List<string>();
        for (var i = 0; i < path.Count - 1; i++)
        {
            var from = byId[path[i]]; var to = byId[path[i + 1]];
            var egress = Egress(links, from.Id, to.Id);
            if (egress.Port is null) notes.Add($"the port on {from.Name} toward {to.Name} could not be determined");
            hops.Add(new TraceHop(from.Id, from.Name, from.Ip, egress.PortId, egress.Port, egress.IfIndex, egress.Confidence, false));
        }
        hops.Add(FinalHop());

        // Partial if the chain stops short of the Layer 3 core, or an egress port is unknown.
        var anyL3 = switches.Any(s => s.IsL3);
        if (anyL3 && l3InComp.Count == 0)
        {
            var core = switches.First(s => s.IsL3);
            notes.Add($"no proven link between {byId[root].Name} and the core switch {core.Name}");
        }
        var partial = notes.Count > 0;
        return new TraceResult(hops, final, partial, partial ? $"{PartialMessage}: {string.Join("; ", notes)}." : null);
    }

    private static (int? PortId, string? Port, int? IfIndex, NeighborConfidence? Confidence) Egress(IReadOnlyList<TraceLink> links, int from, int to)
    {
        var own = links.Where(l => l.FromSwitchId == from && l.ToSwitchId == to).OrderByDescending(l => l.Confidence).ThenBy(l => l.FromPortId).FirstOrDefault();
        if (own is not null) return (own.FromPortId, own.FromPortName, own.FromIfIndex, own.Confidence);
        // Only the far side knows the link: its LLDP/CDP "remote port" is our egress port.
        var reverse = links.Where(l => l.FromSwitchId == to && l.ToSwitchId == from && l.RemotePortName is not null).OrderByDescending(l => l.Confidence).FirstOrDefault();
        return reverse is null ? (null, null, null, null) : (null, reverse.RemotePortName, reverse.RemotePortIfIndex, reverse.Confidence);
    }
}
