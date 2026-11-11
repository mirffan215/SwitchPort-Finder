using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

public class TopologyService(AppDbContext db, PortQueryService ports)
{
    public async Task<TopologyResponse> GetAsync(CancellationToken ct)
    {
        var sw = await db.Switches.AsNoTracking().ToListAsync(ct);
        var portCounts = await db.SwitchPorts.AsNoTracking().GroupBy(p => p.SwitchId).Select(g => new { g.Key, Total = g.Count(), Up = g.Count(p => p.OperUp), Uplinks = g.Count(p => p.PortType == PortType.SwitchUplink) }).ToDictionaryAsync(g => g.Key, ct);
        var devCounts = await db.Devices.AsNoTracking().Where(d => d.SwitchId != null).GroupBy(d => d.SwitchId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var switches = sw.Select(s => new TopoSwitch(s.Id, s.Name, s.ManagementIp,
            VendorDetector.DisplayName(s.Manufacturer == Manufacturer.AutoDetect && s.DetectedManufacturer is { } dm ? dm : s.Manufacturer), s.Status.ToString(), s.UseAsArpSource || s.ArpAutoDetected,
            portCounts.TryGetValue(s.Id, out var pc) ? pc.Total : 0, portCounts.TryGetValue(s.Id, out var pc2) ? pc2.Uplinks : 0, devCounts.GetValueOrDefault(s.Id))).ToList();

        var uplinks = await db.SwitchPorts.AsNoTracking().Where(p => p.PortType == PortType.SwitchUplink && p.NeighborId != null).ToListAsync(ct);
        var dtos = await ports.NeighborsFor(uplinks.Select(p => p.NeighborId!.Value).ToList(), ct);
        var links = new List<TopoLink>();
        foreach (var p in uplinks)
        {
            if (!dtos.TryGetValue(p.NeighborId!.Value, out var n)) continue;
            if (n.ConnectedSwitchId is null && n.Protocol == NeighborProtocol.Inferred) continue;   // an inferred uplink with no identified switch has nothing to draw
            var key = n.RemoteIp ?? n.RemoteName ?? n.RemoteChassis;
            links.Add(new TopoLink(p.SwitchId, p.Id, p.Name, p.IfIndex, n.ConnectedSwitchId, key, n.RemoteName, n.RemoteIp, n.RemotePort, n.RemotePortIfIndex, p.Confidence, n.Protocol));
        }
        return TopologyBuilder.Build(switches, links);
    }
}

public class DashboardService(AppDbContext db, DiscoveryService discovery, DeviceQueryService devices, IOptions<DiscoveryOptions> opt)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var latest = await db.Switches.AsNoTracking().MaxAsync(s => s.LastScanAt, ct);
        var jobs = await discovery.ListAsync(10, null, ct);
        return new DashboardDto(
            await db.Switches.CountAsync(ct), await db.Devices.CountAsync(ct),
            await db.SwitchPorts.CountAsync(p => p.OperUp, ct), await db.SwitchPorts.CountAsync(p => p.PortType == PortType.SwitchUplink, ct),
            await db.Devices.CountAsync(d => d.LastSeen < ((d.Switch != null && d.Switch.LastScanAt != null ? d.Switch.LastScanAt.Value : (latest ?? d.LastSeen))).AddMinutes(-DeviceStates.ToleranceMinutes), ct),
            jobs.FirstOrDefault(j => j.Status is JobStatus.Completed or JobStatus.CompletedWithWarnings or JobStatus.Failed), jobs, await devices.RecentAsync(10, ct),
            await db.SwitchPorts.CountAsync(ct), await db.SwitchPorts.CountAsync(p => !p.OperUp, ct));
    }

    /// <summary>Top-N ports across all switches by utilization (busier direction vs link speed) or by discarded packets per second.</summary>
    public async Task<TopPortsResponse> TopPortsAsync(string metric, int n, CancellationToken ct)
    {
        var rows = await db.SwitchPorts.AsNoTracking().Where(p => p.RateAt != null)
            .Select(p => new { Port = p, SwitchName = p.Switch!.Name }).ToListAsync(ct);
        var total = await db.SwitchPorts.CountAsync(ct);
        var items = rows.Select(r => (r.Port, r.SwitchName, T: PortQueryService.TrafficOf(r.Port)!)).ToList();
        var ranked = metric == "drops"
            ? items.Where(x => x.T.DiscardsPerSec > 0).OrderByDescending(x => x.T.DiscardsPerSec).ThenByDescending(x => x.T.ErrorsPerSec)
            : items.Where(x => x.T.UtilizationPct is not null).OrderByDescending(x => x.T.UtilizationPct);
        var top = ranked.Take(n).Select(x => new TopPortDto(x.Port.SwitchId, x.SwitchName, x.Port.Id, x.Port.IfIndex, x.Port.Name, x.Port.Alias, x.Port.SpeedMbps, x.T)).ToList();
        return new TopPortsResponse(metric, top, items.Count, total);
    }

    /// <summary>
    /// Uptime of up to <paramref name="n"/> switches for the dashboard: core (routing / ARP-source) switches first, then the switches that
    /// restarted most recently. The value is the last scan's sysUpTime plus the time since, but only while the switch is reachable.
    /// </summary>
    public async Task<SwitchUptimeResponse> SwitchUptimeAsync(int n, CancellationToken ct)
    {
        var all = await db.Switches.AsNoTracking().Select(s => new { s.Id, s.Name, s.ManagementIp, s.Model, s.UseAsArpSource, s.ArpAutoDetected, s.Status, s.UptimeSeconds, s.UptimeAt }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        var rows = all.Where(s => s.UptimeSeconds != null && s.UptimeAt != null).Select(s =>
        {
            long? up = s.Status == SwitchStatus.Reachable ? s.UptimeSeconds + (long)Math.Max(0, (now - s.UptimeAt!.Value).TotalSeconds) : null;
            return new SwitchUptimeDto(s.Id, s.Name, s.ManagementIp, s.Model, s.UseAsArpSource || s.ArpAutoDetected, s.Status.ToString(), up, s.UptimeAt);
        }).ToList();
        var top = rows.OrderByDescending(r => r.IsCore).ThenBy(r => r.UptimeSeconds ?? long.MaxValue).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Take(n).ToList();
        return new SwitchUptimeResponse(top, all.Count, rows.Count);
    }
}
