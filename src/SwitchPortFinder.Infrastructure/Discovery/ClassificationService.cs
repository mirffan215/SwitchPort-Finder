using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// Global post-scan step: classify every port (uplink / endpoint / unknown / down), persist neighbor matches and inferred links, drop
/// switches' own MACs from the device list, and re-locate devices using ONLY edge (non-uplink) ports.
/// Global on purpose: adding or scanning one switch can upgrade the classification of ports on others.
/// </summary>
public class ClassificationService(AppDbContext db, IOptions<DiscoveryOptions> opt, ILogger<ClassificationService> log)
{
    /// <summary>One writer at a time (scan persistence and classification share state; also avoids duplicate-MAC insert races).</summary>
    public static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task RunExclusiveAsync(IEnumerable<string>? extraMacs, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try { await RunAsync(extraMacs ?? [], ct); await db.SaveChangesAsync(ct); }
        finally { Gate.Release(); }
    }

    private sealed record PRow(int Id, int SwitchId, int IfIndex, string Name, bool OperUp, string? PhysAddress, PortType Type, NeighborConfidence Confidence, long? NeighborId, PortSupport Support, int VlanCount);
    private sealed record NRow(long Id, int SwitchId, int PortId, NeighborProtocol Protocol, string? ChassisMac, string? MgmtIp, string? SysName, NeighborCaps Caps, int? MatchedSwitchId, string? Evidence);

    /// <summary>
    /// Caller must hold <see cref="Gate"/>. Reads the fleet as untracked projections and only loads/tracks the few rows whose classification
    /// actually changed, so a run stays cheap at 500 switches / 24k ports / 50k sightings.
    /// </summary>
    public async Task RunAsync(IEnumerable<string> scanMacs, CancellationToken ct)
    {
        var o = opt.Value; var now = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);   // make this unit of work's pending changes visible to the reads below
        var switches = await db.Switches.AsNoTracking().Select(s => new { s.Id, s.Name, s.ManagementIp, s.SysName }).ToListAsync(ct);
        var macsBySwitch = (await db.SwitchMacs.AsNoTracking().Select(m => new { m.SwitchId, m.Mac }).ToListAsync(ct)).GroupBy(m => m.SwitchId).ToDictionary(g => g.Key, g => g.Select(m => m.Mac).ToHashSet());
        var ports = await db.SwitchPorts.AsNoTracking().Select(p => new PRow(p.Id, p.SwitchId, p.IfIndex, p.Name, p.OperUp, p.PhysAddress, p.PortType, p.Confidence, p.NeighborId, p.Support, p.VlanCount)).ToListAsync(ct);
        foreach (var p in ports.Where(p => p.PhysAddress is not null)) { if (!macsBySwitch.TryGetValue(p.SwitchId, out var set)) macsBySwitch[p.SwitchId] = set = []; set.Add(p.PhysAddress!); }
        var neighbors = await db.Neighbors.AsNoTracking().Select(n => new NRow(n.Id, n.SwitchId, n.PortId, n.Protocol, n.ChassisMac, n.MgmtIp, n.SysName, n.Caps, n.MatchedSwitchId, n.Evidence)).ToListAsync(ct);
        var obs = await db.Observations.AsNoTracking().Select(x => new { x.SwitchId, x.PortId, x.Mac, x.Vlan, x.LastSeen }).ToListAsync(ct);

        var swRefs = switches.Select(s => new SwitchRef(s.Id, s.Name, s.ManagementIp, s.SysName, macsBySwitch.GetValueOrDefault(s.Id) ?? [])).ToList();
        var portRefs = ports.Select(p => new PortRef(p.Id, p.SwitchId, p.IfIndex, p.Name, p.OperUp, p.Support, p.VlanCount)).ToList();
        var nbRefs = neighbors.Where(n => n.Protocol != NeighborProtocol.Inferred).Select(n => new NeighborRef(n.Id, n.SwitchId, n.PortId, n.Protocol, n.ChassisMac, n.MgmtIp, n.SysName, n.Caps)).ToList();
        var obsRefs = obs.Select(x => new ObsRef(x.SwitchId, x.PortId, x.Mac)).ToList();
        var result = UplinkClassifier.Classify(swRefs, portRefs, nbRefs, obsRefs, o.UplinkHeuristicMinMacs, o.UplinkHeuristicRatio);

        // ---- neighbors: resolved registered-switch matches (touch only rows that changed) ----
        var changedMatches = neighbors.Where(n => n.Protocol != NeighborProtocol.Inferred && result.NeighborMatches.TryGetValue(n.Id, out var m) && m != n.MatchedSwitchId).Select(n => n.Id).ToList();
        foreach (var chunk in changedMatches.Chunk(500))
            foreach (var n in await db.Neighbors.Where(n => chunk.Contains(n.Id)).ToListAsync(ct)) n.MatchedSwitchId = result.NeighborMatches[n.Id];

        // ---- inferred links (FDB heuristic) ----
        var swById = switches.ToDictionary(s => s.Id);
        var portById = ports.ToDictionary(p => p.Id);
        var inferredNow = result.Ports.Where(v => v.Inferred).ToDictionary(v => v.PortId);
        var inferredOld = neighbors.Where(n => n.Protocol == NeighborProtocol.Inferred).GroupBy(n => n.PortId).ToDictionary(g => g.Key, g => g.First());
        foreach (var (pid, old) in inferredOld.Where(kv => !inferredNow.ContainsKey(kv.Key))) db.Neighbors.Remove(new SwitchNeighbor { Id = old.Id });
        var added = new Dictionary<int, SwitchNeighbor>(); var toUpdate = new List<(long Id, PortVerdict V)>();
        foreach (var (pid, v) in inferredNow)
        {
            var evidence = Trunc(v.Evidence, 500);
            if (inferredOld.TryGetValue(pid, out var old)) { if (old.MatchedSwitchId != v.ConnectedSwitchId || old.Evidence != evidence) toUpdate.Add((old.Id, v)); continue; }
            var row = new SwitchNeighbor { SwitchId = portById[pid].SwitchId, PortId = pid, Protocol = NeighborProtocol.Inferred, RemoteKey = "inferred", FirstSeen = now, Caps = NeighborCaps.Bridge };
            Fill(row, v, evidence); db.Neighbors.Add(row); added[pid] = row;
        }
        foreach (var chunk in toUpdate.Chunk(500)) { var ids = chunk.Select(c => c.Id).ToList(); var rows = await db.Neighbors.Where(n => ids.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct); foreach (var (id, v) in chunk) Fill(rows[id], v, Trunc(v.Evidence, 500)); }
        await db.SaveChangesAsync(ct);   // new inferred rows get their ids

        void Fill(SwitchNeighbor row, PortVerdict v, string? evidence)
        {
            row.MatchedSwitchId = v.ConnectedSwitchId; row.Evidence = evidence; row.LastSeen = now;
            row.SysName = v.ConnectedSwitchId is { } id && swById.TryGetValue(id, out var cs) ? cs.Name : null;
        }

        // ---- apply verdicts to ports: only rows whose classification changed are loaded and tracked ----
        long? NeighborFor(PortVerdict v) => v.Inferred ? (added.TryGetValue(v.PortId, out var a) ? a.Id : inferredOld.GetValueOrDefault(v.PortId)?.Id) : v.NeighborId;
        var flipped = new HashSet<int>(); var portChanges = new List<(PortVerdict V, long? Neighbor)>();
        foreach (var v in result.Ports)
        {
            var cur = portById[v.PortId]; var nid = NeighborFor(v);
            if ((cur.Type == PortType.SwitchUplink) != (v.Type == PortType.SwitchUplink)) flipped.Add(cur.Id);
            if (cur.Type != v.Type || cur.Confidence != v.Confidence || cur.NeighborId != nid) portChanges.Add((v, nid));
        }
        foreach (var chunk in portChanges.Chunk(500))
        {
            var ids = chunk.Select(c => c.V.PortId).ToList();
            var rows = await db.SwitchPorts.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            foreach (var (v, nid) in chunk) { var p = rows[v.PortId]; p.PortType = v.Type; p.Confidence = v.Confidence; p.NeighborId = nid; }
        }
        await db.SaveChangesAsync(ct);

        // ---- a switch's own MACs are not "devices" (one set-based statement; SwitchMacs holds every interface MAC) ----
        await db.Devices.Where(d => db.SwitchMacs.Any(m => m.Mac == d.Mac)).ExecuteDeleteAsync(ct);
        var ownMacs = macsBySwitch.Values.SelectMany(x => x).ToHashSet();

        // ---- authoritative locations: edge ports only ----
        var uplinkPorts = result.Ports.Where(v => v.Type == PortType.SwitchUplink).Select(v => v.PortId).ToHashSet();
        var macCount = obs.GroupBy(x => x.PortId).ToDictionary(g => g.Key, g => g.Count());
        var affected = scanMacs.Where(m => !ownMacs.Contains(m)).ToHashSet();
        if (flipped.Count > 0) foreach (var x in obs.Where(x => flipped.Contains(x.PortId))) if (!ownMacs.Contains(x.Mac)) affected.Add(x.Mac);
        var obsByMac = obs.Where(x => affected.Contains(x.Mac)).ToLookup(x => x.Mac);
        var window = TimeSpan.FromMinutes(o.OfflineAfterMinutes);
        var devs = new Dictionary<string, Device>();
        foreach (var chunk in affected.Chunk(500)) foreach (var d in await db.Devices.Where(d => chunk.Contains(d.Mac)).ToListAsync(ct)) devs[d.Mac] = d;
        foreach (var (mac, d) in devs)
        {
            var choice = LocationSelector.Choose(obsByMac[mac].Select(x => new LocationSelector.ObsCandidate(x.SwitchId, x.PortId, x.Vlan, x.LastSeen)),
                uplinkPorts.Contains, pid => macCount.GetValueOrDefault(pid), window);
            d.SwitchId = choice.Best?.SwitchId; d.PortId = choice.Best?.PortId; d.Vlan = choice.Best?.Vlan; d.LowConfidence = choice.LowConfidence;
            // A location set by hand wins over discovery (and is dropped only when its port no longer exists).
            if (d.PinnedPortId is { } pp)
            {
                if (portById.TryGetValue(pp, out var pinned)) { d.SwitchId = pinned.SwitchId; d.PortId = pp; d.Vlan = d.PinnedVlan ?? d.Vlan; d.LowConfidence = false; }
                else { d.PinnedSwitchId = null; d.PinnedPortId = null; d.PinnedVlan = null; }
            }
        }
        log.LogInformation("Classification: {Uplinks} uplink ports, {Changed} ports changed, {Devices} devices re-located", uplinkPorts.Count, portChanges.Count, devs.Count);
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
