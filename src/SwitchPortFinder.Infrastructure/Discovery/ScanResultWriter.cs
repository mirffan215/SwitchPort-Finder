using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// Persists a scan: ports, per-switch observations, devices (authoritative location), the global ARP IP→MAC join and hostnames.
/// Incremental: unchanged rows are not rewritten (EF only issues UPDATEs for changed properties), LastSeen is refreshed.
/// </summary>
public class ScanResultWriter(AppDbContext db, IHostnameResolver dns, IOptions<DiscoveryOptions> opt)
{
    public record Counts(int Ports, int Devices);

    public async Task<Counts> WriteAsync(Switch sw, ScanResult r, CancellationToken ct)
    {
        var o = opt.Value; var now = DateTime.UtcNow;
        ApplySwitch(sw, r, now);

        // ---- ports ----
        var ports = await db.SwitchPorts.Where(p => p.SwitchId == sw.Id).ToDictionaryAsync(p => p.IfIndex, ct);
        foreach (var i in r.Interfaces)
        {
            if (!ports.TryGetValue(i.IfIndex, out var p)) { p = new SwitchPort { SwitchId = sw.Id, IfIndex = i.IfIndex }; db.SwitchPorts.Add(p); ports[i.IfIndex] = p; }
            p.Name = Trunc(i.Name, 100)!; p.Descr = Trunc(i.Descr, 255); p.Alias = Trunc(i.Alias, 255);
            p.AdminUp = i.AdminUp; p.OperUp = i.OperUp; p.SpeedMbps = i.SpeedMbps; p.PhysAddress = i.Mac; p.Mtu = i.Mtu;
            p.Vlan = r.PvidByIfIndex.TryGetValue(i.IfIndex, out var v) ? v : null;
            p.LastSeen = now;
        }
        await db.SaveChangesAsync(ct);   // port ids needed below

        // ---- observations (one row per switch+MAC) ----
        var fdb = r.Fdb.GroupBy(e => e.Mac).Select(g => g.First()).Where(e => ports.ContainsKey(e.IfIndex!.Value)).ToList();
        var obs = await db.Observations.Where(x => x.SwitchId == sw.Id).ToDictionaryAsync(x => x.Mac, ct);
        foreach (var e in fdb)
        {
            var port = ports[e.IfIndex!.Value];
            if (!obs.TryGetValue(e.Mac, out var ob)) { ob = new DeviceObservation { SwitchId = sw.Id, Mac = e.Mac, FirstSeen = now }; db.Observations.Add(ob); obs[e.Mac] = ob; }
            ob.PortId = port.Id; ob.Vlan = e.Vlan ?? port.Vlan; ob.IsStatic = e.Kind == FdbKind.Static; ob.LastSeen = now;
        }
        // Port types (Phase 3 adds uplink classification): Down / Endpoint (has learned MACs) / Unknown.
        var withMacs = fdb.Select(e => e.IfIndex!.Value).ToHashSet();
        foreach (var (ifx, p) in ports.Where(kv => r.Interfaces.Any(i => i.IfIndex == kv.Key)))
            p.PortType = !p.OperUp ? PortType.Down : withMacs.Contains(ifx) ? PortType.Endpoint : PortType.Unknown;
        await db.SaveChangesAsync(ct);

        // ---- ARP entries from this source ----
        var arpMacs = new HashSet<string>();
        if (r.Arp.Count > 0)
        {
            var existing = await db.ArpEntries.Where(a => a.SourceSwitchId == sw.Id).ToDictionaryAsync(a => a.Ip, ct);
            foreach (var a in r.Arp)
            {
                if (!existing.TryGetValue(a.Ip, out var row)) { row = new ArpEntry { SourceSwitchId = sw.Id, Ip = a.Ip }; db.ArpEntries.Add(row); existing[a.Ip] = row; }
                row.Mac = a.Mac; row.ObservedAt = now; arpMacs.Add(a.Mac);
            }
            await db.SaveChangesAsync(ct);
        }

        // ---- devices: create/refresh, choose authoritative location, global IP join ----
        var scanMacs = fdb.Select(e => e.Mac).ToHashSet();
        var touched = scanMacs.Union(arpMacs).ToList();
        var devices = new Dictionary<string, Device>();
        foreach (var chunk in touched.Chunk(500))
            foreach (var d in await db.Devices.Where(d => chunk.Contains(d.Mac)).ToListAsync(ct)) devices[d.Mac] = d;
        foreach (var mac in scanMacs)
            if (!devices.ContainsKey(mac)) { var d = new Device { Mac = mac, FirstSeen = now, LastSeen = now }; db.Devices.Add(d); devices[mac] = d; }
        foreach (var mac in scanMacs) devices[mac].LastSeen = now;

        await ChooseLocations(scanMacs, devices, o, now, ct);
        var ipChanged = await JoinIps(devices.Values.ToList(), ct);
        await db.SaveChangesAsync(ct);

        // ---- hostnames (reverse DNS; never fails the scan) ----
        var refresh = now - TimeSpan.FromHours(o.HostnameRefreshHours);
        var need = devices.Values.Where(d => d.IpAddress is not null &&
            (ipChanged.Contains(d.Mac) || (d.HostnameSource == HostnameSource.None && (d.HostnameCheckedAt is null || d.HostnameCheckedAt < refresh)))).ToList();
        using var gate = new SemaphoreSlim(8);
        var names = await Task.WhenAll(need.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try { return (d, name: await dns.ResolveAsync(d.IpAddress!, ct)); } finally { gate.Release(); }
        }));
        foreach (var (d, name) in names)
        {
            d.HostnameCheckedAt = now;
            if (name is not null) { d.Hostname = Trunc(name, 255)!; d.HostnameSource = HostnameSource.ReverseDns; }
            else if (d.HostnameSource != HostnameSource.ReverseDns || ipChanged.Contains(d.Mac)) { d.Hostname = "Unknown Device"; d.HostnameSource = HostnameSource.None; }
        }
        await db.SaveChangesAsync(ct);
        return new Counts(r.Interfaces.Count, scanMacs.Count);
    }

    private static void ApplySwitch(Switch sw, ScanResult r, DateTime now)
    {
        sw.Status = SwitchStatus.Reachable; sw.LastScanAt = now;
        if (r.System is { } s)
        {
            sw.SysName = Trunc(s.Name, 255); sw.SysDescr = Trunc(s.Descr, 1000); sw.SysObjectId = Trunc(s.ObjectId, 200);
            if (!string.IsNullOrWhiteSpace(s.Model) && string.IsNullOrWhiteSpace(sw.Model)) sw.Model = Trunc(s.Model, 100);
            if (!string.IsNullOrWhiteSpace(s.Serial)) sw.SerialNumber = Trunc(s.Serial, 100);
        }
        sw.DetectedManufacturer = r.DetectedManufacturer;
        sw.ArpAutoDetected = r.IsLayer3;
    }

    /// <summary>
    /// Authoritative location = where the MAC is learned. Phase 3 will exclude uplink ports; until then, when a MAC is seen on several
    /// switches the port with the fewest MACs wins (flagged lower confidence), per the edge-port selection rule.
    /// </summary>
    private async Task ChooseLocations(HashSet<string> macs, Dictionary<string, Device> devices, DiscoveryOptions o, DateTime now, CancellationToken ct)
    {
        foreach (var chunk in macs.Chunk(500))
        {
            var rows = await db.Observations.Include(x => x.Port).Where(x => chunk.Contains(x.Mac)).ToListAsync(ct);
            var portIds = rows.Select(x => x.PortId).Distinct().ToList();
            var counts = new Dictionary<int, int>();
            foreach (var pc in portIds.Chunk(500))
                foreach (var g in await db.Observations.Where(x => pc.Contains(x.PortId)).GroupBy(x => x.PortId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct))
                    counts[g.Key] = g.N;
            foreach (var g in rows.GroupBy(x => x.Mac))
            {
                var newest = g.Max(x => x.LastSeen);
                var cands = g.Where(x => newest - x.LastSeen <= TimeSpan.FromMinutes(o.OfflineAfterMinutes)).ToList();
                var best = cands.OrderBy(x => counts.GetValueOrDefault(x.PortId)).ThenByDescending(x => x.LastSeen).First();
                var d = devices[g.Key];
                d.SwitchId = best.SwitchId; d.PortId = best.PortId; d.Vlan = best.Vlan; d.LowConfidence = cands.Count > 1;
            }
        }
    }

    /// <summary>Global join: newest ARP observation for a MAC from ANY source switch (IPv4 preferred on ties). Returns MACs whose IP changed.</summary>
    private async Task<HashSet<string>> JoinIps(List<Device> devs, CancellationToken ct)
    {
        var changed = new HashSet<string>();
        foreach (var chunk in devs.Select(d => d.Mac).Chunk(500))
        {
            var arp = await db.ArpEntries.Where(a => chunk.Contains(a.Mac)).ToListAsync(ct);
            foreach (var g in arp.GroupBy(a => a.Mac))
            {
                var best = g.OrderByDescending(a => a.ObservedAt).ThenBy(a => a.Ip.Contains(':') ? 1 : 0).First();
                var d = devs.First(x => x.Mac == g.Key);
                if (d.IpAddress != best.Ip) changed.Add(d.Mac);
                d.IpAddress = best.Ip; d.IpSourceSwitchId = best.SourceSwitchId; d.IpObservedAt = best.ObservedAt;
            }
        }
        return changed;
    }

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
