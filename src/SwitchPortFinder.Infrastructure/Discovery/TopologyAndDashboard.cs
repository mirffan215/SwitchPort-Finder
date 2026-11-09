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
        var o = opt.Value; var activeSince = DateTime.UtcNow.AddMinutes(-o.OfflineAfterMinutes);
        var jobs = await discovery.ListAsync(10, null, ct);
        return new DashboardDto(
            await db.Switches.CountAsync(ct), await db.Devices.CountAsync(ct),
            await db.SwitchPorts.CountAsync(p => p.OperUp, ct), await db.SwitchPorts.CountAsync(p => p.PortType == PortType.SwitchUplink, ct),
            await db.Devices.CountAsync(d => d.LastSeen < activeSince, ct),
            jobs.FirstOrDefault(j => j.Status is JobStatus.Completed or JobStatus.CompletedWithWarnings or JobStatus.Failed), jobs, await devices.RecentAsync(10, ct));
    }
}
