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

    /// <summary>Caller must hold <see cref="Gate"/>. Does not call SaveChanges for the final device updates beyond what it needs.</summary>
    public async Task RunAsync(IEnumerable<string> scanMacs, CancellationToken ct)
    {
        var o = opt.Value; var now = DateTime.UtcNow;
        var switches = await db.Switches.AsNoTracking().Select(s => new { s.Id, s.Name, s.ManagementIp, s.SysName }).ToListAsync(ct);
        var macsBySwitch = (await db.SwitchMacs.AsNoTracking().ToListAsync(ct)).GroupBy(m => m.SwitchId).ToDictionary(g => g.Key, g => g.Select(m => m.Mac).ToHashSet());
        var ports = await db.SwitchPorts.ToListAsync(ct);
        foreach (var p in ports.Where(p => p.PhysAddress is not null)) { if (!macsBySwitch.TryGetValue(p.SwitchId, out var set)) macsBySwitch[p.SwitchId] = set = []; set.Add(p.PhysAddress!); }
        var neighbors = await db.Neighbors.ToListAsync(ct);
        var obs = await db.Observations.AsNoTracking().Select(x => new { x.SwitchId, x.PortId, x.Mac, x.Vlan, x.LastSeen }).ToListAsync(ct);

        var swRefs = switches.Select(s => new SwitchRef(s.Id, s.Name, s.ManagementIp, s.SysName, macsBySwitch.GetValueOrDefault(s.Id) ?? [])).ToList();
        var portRefs = ports.Select(p => new PortRef(p.Id, p.SwitchId, p.IfIndex, p.Name, p.OperUp)).ToList();
        var nbRefs = neighbors.Where(n => n.Protocol != NeighborProtocol.Inferred)
            .Select(n => new NeighborRef(n.Id, n.SwitchId, n.PortId, n.Protocol, n.ChassisMac, n.MgmtIp, n.SysName, n.Caps)).ToList();
        var obsRefs = obs.Select(x => new ObsRef(x.SwitchId, x.PortId, x.Mac)).ToList();

        var result = UplinkClassifier.Classify(swRefs, portRefs, nbRefs, obsRefs, o.UplinkHeuristicMinMacs, o.UplinkHeuristicRatio);

        // ---- neighbors: resolved registered-switch matches ----
        foreach (var n in neighbors.Where(n => n.Protocol != NeighborProtocol.Inferred))
            if (result.NeighborMatches.TryGetValue(n.Id, out var m)) n.MatchedSwitchId = m;

        // ---- inferred links (FDB heuristic) ----
        var swById = switches.ToDictionary(s => s.Id);
        var inferred = result.Ports.Where(v => v.Inferred).ToDictionary(v => v.PortId);
        foreach (var old in neighbors.Where(n => n.Protocol == NeighborProtocol.Inferred && !inferred.ContainsKey(n.PortId))) db.Neighbors.Remove(old);
        foreach (var (portId, v) in inferred)
        {
            var port = ports.First(p => p.Id == portId);
            var row = neighbors.FirstOrDefault(n => n.Protocol == NeighborProtocol.Inferred && n.PortId == portId);
            if (row is null) { row = new SwitchNeighbor { SwitchId = port.SwitchId, PortId = portId, Protocol = NeighborProtocol.Inferred, RemoteKey = "inferred", FirstSeen = now }; db.Neighbors.Add(row); }
            row.MatchedSwitchId = v.ConnectedSwitchId; row.Evidence = Trunc(v.Evidence, 500); row.LastSeen = now;
            row.SysName = v.ConnectedSwitchId is { } id && swById.TryGetValue(id, out var cs) ? cs.Name : null;
            row.Caps = NeighborCaps.Bridge;
        }
        await db.SaveChangesAsync(ct);   // inferred rows need ids

        // ---- apply verdicts to ports; remember which ports flipped uplink-ness ----
        var inferredIds = db.ChangeTracker.Entries<SwitchNeighbor>().Where(e => e.Entity.Protocol == NeighborProtocol.Inferred && e.State != EntityState.Deleted).ToDictionary(e => e.Entity.PortId, e => e.Entity.Id);
        var flipped = new HashSet<int>();
        foreach (var v in result.Ports)
        {
            var p = ports.First(x => x.Id == v.PortId);
            if ((p.PortType == PortType.SwitchUplink) != (v.Type == PortType.SwitchUplink)) flipped.Add(p.Id);
            p.PortType = v.Type; p.Confidence = v.Confidence;
            p.NeighborId = v.Inferred ? inferredIds.GetValueOrDefault(v.PortId) : v.NeighborId;
        }

        // ---- a switch's own MACs are not "devices" ----
        var ownMacs = macsBySwitch.Values.SelectMany(x => x).ToHashSet();
        foreach (var chunk in ownMacs.Chunk(500))
            db.Devices.RemoveRange(await db.Devices.Where(d => chunk.Contains(d.Mac)).ToListAsync(ct));
        foreach (var d in db.ChangeTracker.Entries<Device>().Where(e => e.State == EntityState.Added && ownMacs.Contains(e.Entity.Mac)).ToList()) d.State = EntityState.Detached;

        // ---- authoritative locations: edge ports only ----
        var uplinkPorts = result.Ports.Where(v => v.Type == PortType.SwitchUplink).Select(v => v.PortId).ToHashSet();
        var macCount = obs.GroupBy(x => x.PortId).ToDictionary(g => g.Key, g => g.Count());
        var affected = scanMacs.Where(m => !ownMacs.Contains(m)).ToHashSet();
        if (flipped.Count > 0) foreach (var x in obs.Where(x => flipped.Contains(x.PortId))) if (!ownMacs.Contains(x.Mac)) affected.Add(x.Mac);
        var obsByMac = obs.Where(x => affected.Contains(x.Mac)).ToLookup(x => x.Mac);
        var window = TimeSpan.FromMinutes(o.OfflineAfterMinutes);
        var devs = new Dictionary<string, Device>();
        foreach (var chunk in affected.Chunk(500)) foreach (var d in await db.Devices.Where(d => chunk.Contains(d.Mac)).ToListAsync(ct)) devs[d.Mac] = d;
        // include devices created in this unit of work but not yet flushed
        foreach (var e in db.ChangeTracker.Entries<Device>().Where(e => e.State == EntityState.Added)) devs[e.Entity.Mac] = e.Entity;
        foreach (var (mac, d) in devs)
        {
            var choice = LocationSelector.Choose(obsByMac[mac].Select(x => new LocationSelector.ObsCandidate(x.SwitchId, x.PortId, x.Vlan, x.LastSeen)),
                uplinkPorts.Contains, pid => macCount.GetValueOrDefault(pid), window);
            d.SwitchId = choice.Best?.SwitchId; d.PortId = choice.Best?.PortId; d.Vlan = choice.Best?.Vlan; d.LowConfidence = choice.LowConfidence;
        }
        log.LogInformation("Classification: {Uplinks} uplink ports, {Flipped} ports changed, {Devices} devices re-located", uplinkPorts.Count, flipped.Count, devs.Count);
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
