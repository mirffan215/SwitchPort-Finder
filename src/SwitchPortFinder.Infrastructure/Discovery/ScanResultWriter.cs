using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// Persists a scan: ports, per-switch observations, devices (authoritative location), the global ARP IP→MAC join and hostnames.
/// Incremental: unchanged rows are not rewritten (EF only issues UPDATEs for changed properties), LastSeen is refreshed.
/// </summary>
public class ScanResultWriter(AppDbContext db, IHostnameResolver dns, ClassificationService classification, DeviceCategoryService categories, IOptions<DiscoveryOptions> opt)
{
    public record Counts(int Ports, int Devices, int Neighbors);

    /// <summary>Serialised process-wide: parallel scans that see the same new MAC would otherwise race on the unique Device.Mac index.</summary>
    public async Task<Counts> WriteAsync(Switch sw, ScanResult r, CancellationToken ct)
    {
        await ClassificationService.Gate.WaitAsync(ct);
        try { return await WriteCoreAsync(sw, r, ct); }
        finally { ClassificationService.Gate.Release(); }
    }

    /// <summary>Keeps the previous scan's counters as the baseline and derives average rates from the last two scans.</summary>
    public static bool UpdateRates(SwitchPort p, PortCounters c, long upTicks, DateTime now)
    {
        var cur = new CounterSample(c.InOctets, c.OutOctets, c.InDiscards, c.OutDiscards, c.InErrors, c.OutErrors, c.Wide, now, upTicks);
        if (p.CtrAt is { } at && p.CtrInOctets is not null)
        {
            if ((now - at).TotalSeconds < PortRateCalculator.MinSeconds) return false;   // back-to-back scans: keep the older baseline
            var prev = new CounterSample(p.CtrInOctets.Value, p.CtrOutOctets ?? 0, p.CtrInDiscards ?? 0, p.CtrOutDiscards ?? 0, p.CtrInErrors ?? 0, p.CtrOutErrors ?? 0, p.CtrWide, at, p.CtrUpTicks);
            if (PortRateCalculator.Compute(prev, cur, p.SpeedMbps) is { } rt)
            {
                p.RateInBps = rt.InBps; p.RateOutBps = rt.OutBps; p.RateDiscardsPerSec = rt.DiscardsPerSec; p.RateErrorsPerSec = rt.ErrorsPerSec; p.RateSeconds = rt.Seconds; p.RateAt = now;
            }
            else { p.RateInBps = p.RateOutBps = p.RateDiscardsPerSec = p.RateErrorsPerSec = null; p.RateSeconds = null; p.RateAt = null; }   // reboot / reset: no trustworthy rate yet
        }
        p.CtrInOctets = c.InOctets; p.CtrOutOctets = c.OutOctets; p.CtrInDiscards = c.InDiscards; p.CtrOutDiscards = c.OutDiscards;
        p.CtrInErrors = c.InErrors; p.CtrOutErrors = c.OutErrors; p.CtrWide = c.Wide; p.CtrUpTicks = upTicks; p.CtrAt = now;
        return p.RateAt == now;   // true when this reading produced a new rate
    }

    private async Task<Counts> WriteCoreAsync(Switch sw, ScanResult r, CancellationToken ct)
    {
        var o = opt.Value; var now = DateTime.UtcNow;
        ApplySwitch(sw, r, now);

        // ---- ports ----
        var ports = await db.SwitchPorts.Where(p => p.SwitchId == sw.Id).ToDictionaryAsync(p => p.IfIndex, ct);
        var portSamples = new List<(SwitchPort Port, DateTime At)>();
        foreach (var i in r.Interfaces)
        {
            if (!ports.TryGetValue(i.IfIndex, out var p)) { p = new SwitchPort { SwitchId = sw.Id, IfIndex = i.IfIndex }; db.SwitchPorts.Add(p); ports[i.IfIndex] = p; }
            p.Name = Trunc(i.Name, 100)!; p.Descr = Trunc(i.Descr, 255); p.Alias = Trunc(i.Alias, 255);
            // an unreported status is unknown, never "down": keep the previous value
            if (i.OperKnown) { p.AdminUp = i.AdminUp; p.OperUp = i.OperUp; }
            p.SpeedMbps = i.SpeedMbps; p.PhysAddress = i.Mac; p.Mtu = i.Mtu;
            p.Vlan = r.PvidByIfIndex.TryGetValue(i.IfIndex, out var v) ? v : null;
            var sup = r.Support.GetValueOrDefault(i.IfIndex); p.Support = sup?.Flags ?? PortSupport.None; p.VlanCount = sup?.VlanCount ?? 0;
            if (i.Counters is { } c && UpdateRates(p, c, r.System?.UpTimeTicks ?? 0, now)) portSamples.Add((p, now));
            p.LastSeen = now;
        }
        await db.SaveChangesAsync(ct);   // port ids needed below
        AddSamples(db, sw, r.Health, portSamples, now);
        await db.SaveChangesAsync(ct);

        // ---- observations (one row per switch+MAC) ----
        var fdb = r.Fdb.GroupBy(e => e.Mac).Select(g => g.First()).Where(e => ports.ContainsKey(e.IfIndex!.Value)).ToList();
        var obs = await db.Observations.Where(x => x.SwitchId == sw.Id).ToDictionaryAsync(x => x.Mac, ct);
        foreach (var e in fdb)
        {
            var port = ports[e.IfIndex!.Value];
            if (!obs.TryGetValue(e.Mac, out var ob)) { ob = new DeviceObservation { SwitchId = sw.Id, Mac = e.Mac, FirstSeen = now }; db.Observations.Add(ob); obs[e.Mac] = ob; }
            ob.PortId = port.Id; ob.Vlan = e.Vlan ?? port.Vlan; ob.IsStatic = e.Kind == FdbKind.Static; ob.LastSeen = now;
        }
        await db.SaveChangesAsync(ct);

        // ---- the switch's own MACs (so other switches' FDBs can recognise it) ----
        var knownMacs = await db.SwitchMacs.Where(m => m.SwitchId == sw.Id).ToListAsync(ct);
        var ownNow = r.SwitchMacs.Union(r.Interfaces.Where(i => i.Mac is not null).Select(i => i.Mac!)).ToHashSet();
        db.SwitchMacs.RemoveRange(knownMacs.Where(m => !ownNow.Contains(m.Mac)));
        foreach (var m in ownNow.Where(m => knownMacs.All(k => k.Mac != m))) db.SwitchMacs.Add(new SwitchMac { SwitchId = sw.Id, Mac = m });

        // ---- LLDP / CDP neighbors: upsert what was seen; remove stale rows only for protocols that were actually collected ----
        var nbExisting = await db.Neighbors.Where(n => n.SwitchId == sw.Id && n.Protocol != NeighborProtocol.Inferred).ToListAsync(ct);
        var seenKeys = new HashSet<(int, NeighborProtocol, string)>();
        foreach (var n in r.Neighbors)
        {
            if (!ports.TryGetValue(n.LocalIfIndex, out var np)) continue;
            if (!seenKeys.Add((np.Id, n.Protocol, n.RemoteKey))) continue;
            var row = nbExisting.FirstOrDefault(x => x.PortId == np.Id && x.Protocol == n.Protocol && x.RemoteKey == Trunc(n.RemoteKey, 300));
            if (row is null) { row = new SwitchNeighbor { SwitchId = sw.Id, PortId = np.Id, Protocol = n.Protocol, RemoteKey = Trunc(n.RemoteKey, 300)!, FirstSeen = now }; db.Neighbors.Add(row); nbExisting.Add(row); }
            row.ChassisId = Trunc(n.ChassisId, 255); row.ChassisMac = n.ChassisMac; row.RemotePortId = Trunc(n.RemotePortId, 255); row.RemotePortDescr = Trunc(n.RemotePortDescr, 255);
            row.SysName = Trunc(n.SysName, 255); row.SysDescr = Trunc(n.SysDescr, 500); row.Platform = Trunc(n.Platform, 255); row.Caps = n.Caps; row.MgmtIp = n.MgmtIp; row.LastSeen = now;
        }
        foreach (var stale in nbExisting.Where(x => x.LastSeen != now && x.Id != 0 && ((x.Protocol == NeighborProtocol.Lldp && r.LldpCollected) || (x.Protocol == NeighborProtocol.Cdp && r.CdpCollected))))
            db.Neighbors.Remove(stale);
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
        var allSwitchMacs = (await db.SwitchMacs.Select(m => m.Mac).Distinct().ToListAsync(ct)).ToHashSet();
        var scanMacs = fdb.Select(e => e.Mac).Where(m => !allSwitchMacs.Contains(m)).ToHashSet();   // switches are not devices
        var touched = scanMacs.Union(arpMacs.Where(m => !allSwitchMacs.Contains(m))).ToList();
        var devices = new Dictionary<string, Device>();
        foreach (var chunk in touched.Chunk(500))
            foreach (var d in await db.Devices.Where(d => chunk.Contains(d.Mac)).ToListAsync(ct)) devices[d.Mac] = d;
        foreach (var mac in scanMacs)
            if (!devices.ContainsKey(mac)) { var d = new Device { Mac = mac, FirstSeen = now, LastSeen = now, Vendor = OuiLookup.Lookup(mac) ?? "" }; db.Devices.Add(d); devices[mac] = d; }
        foreach (var mac in scanMacs) devices[mac].LastSeen = now;

        await db.SaveChangesAsync(ct);
        await classification.RunAsync(scanMacs, ct);        // ports, links, edge-only locations (spec 5.8)
        await db.SaveChangesAsync(ct);
        var ipChanged = await JoinIps(devices.Values.Where(d => d.Id != 0).ToList(), ct);
        await db.SaveChangesAsync(ct);

        // ---- hostnames (reverse DNS; never fails the scan) ----
        var refresh = now - TimeSpan.FromHours(o.HostnameRefreshHours);
        var need = devices.Values.Where(d => d.IpAddress is not null &&
            (ipChanged.Contains(d.Mac) || (d.HostnameSource == HostnameSource.None && (d.HostnameCheckedAt is null || d.HostnameCheckedAt < refresh)))).ToList();
        using var gate = new SemaphoreSlim(8);
        var names = await Task.WhenAll(need.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try { return (d, name: await SafeResolve(d.IpAddress!, ct)); } finally { gate.Release(); }
        }));
        foreach (var (d, name) in names)
        {
            d.HostnameCheckedAt = now;
            if (d.HostnameSource == HostnameSource.Manual) continue;          // a name typed by an administrator is never overwritten
            if (name is not null) { d.Hostname = Trunc(name, 255)!; d.HostnameSource = HostnameSource.ReverseDns; }
            else if (d.HostnameSource != HostnameSource.ReverseDns || ipChanged.Contains(d.Mac)) { d.Hostname = "Unknown Device"; d.HostnameSource = HostnameSource.None; }
        }
        await db.SaveChangesAsync(ct);
        await categories.ApplyAsync(devices.Values.Where(d => d.Id != 0).ToList(), ct);   // access point / IP phone / ... from the rules (needs location, neighbors and names)
        var nbCount = await db.Neighbors.CountAsync(n => n.SwitchId == sw.Id && n.Protocol != NeighborProtocol.Inferred, ct);
        return new Counts(r.Interfaces.Count, scanMacs.Count, nbCount);
    }

    /// <summary>A DNS failure of any kind must never fail a scan.</summary>
    private async Task<string?> SafeResolve(string ip, CancellationToken ct)
    {
        try { return await dns.ResolveAsync(ip, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    /// <summary>History rows for the trend reports: one switch health point and one traffic point per port that produced a rate.</summary>
    public static void AddSamples(AppDbContext db, Switch sw, HealthReading? h, IEnumerable<(SwitchPort Port, DateTime At)> portSamples, DateTime now)
    {
        db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = sw.Id, At = now, Reachable = true, UptimeSeconds = sw.UptimeSeconds, CpuPct = h?.CpuPercent, MemPct = h?.MemoryPercent, LatencyMs = h?.LatencyMs });
        foreach (var (p, at) in portSamples)
        {
            var t = PortQueryService.TrafficOf(p); if (t is null) continue;
            db.PortMetrics.Add(new PortMetricSample { SwitchId = p.SwitchId, PortId = p.Id, At = at, InBps = t.InBps, OutBps = t.OutBps, UtilPct = t.UtilizationPct, DiscardsPerSec = t.DiscardsPerSec, ErrorsPerSec = t.ErrorsPerSec });
        }
    }

    private static void ApplySwitch(Switch sw, ScanResult r, DateTime now)
    {
        if (r.Health is { Reachable: true } h) { sw.CpuPct = h.CpuPercent; sw.MemPct = h.MemoryPercent; sw.LatencyMs = h.LatencyMs; sw.HealthAt = now; }
        sw.Status = SwitchStatus.Reachable; sw.LastScanAt = now;
        if (r.System is { } s)
        {
            if (s.UpTimeTicks > 0) { sw.UptimeSeconds = s.UpTimeTicks / 100; sw.UptimeAt = now; }
            sw.SysName = Trunc(s.Name, 255); sw.SysDescr = Trunc(s.Descr, 1000); sw.SysObjectId = Trunc(s.ObjectId, 200);
            if (!string.IsNullOrWhiteSpace(s.Model) && string.IsNullOrWhiteSpace(sw.Model)) sw.Model = Trunc(s.Model, 100);
            if (!string.IsNullOrWhiteSpace(s.Serial)) sw.SerialNumber = Trunc(s.Serial, 100);
        }
        sw.DetectedManufacturer = r.DetectedManufacturer;
        sw.ArpAutoDetected = r.IsLayer3;
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
