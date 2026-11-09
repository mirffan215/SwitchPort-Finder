using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

public class PortQueryService(AppDbContext db, IOptions<DiscoveryOptions> opt)
{
    public async Task<PortsResponse> GetAsync(int switchId, CancellationToken ct)
    {
        var sw = await db.Switches.AsNoTracking().FirstOrDefaultAsync(s => s.Id == switchId, ct) ?? throw new NotFoundException($"Switch {switchId} not found.");
        var ports = await db.SwitchPorts.AsNoTracking().Where(p => p.SwitchId == switchId).ToListAsync(ct);
        var macCounts = await db.Observations.Where(o => o.SwitchId == switchId).GroupBy(o => o.PortId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var devs = (await db.Devices.AsNoTracking().Where(d => d.SwitchId == switchId).ToListAsync(ct)).ToLookup(d => d.PortId ?? 0);
        var now = DateTime.UtcNow;
        var rows = ports.OrderBy(p => NaturalKey(p.Name), StringComparer.Ordinal).Select(p => new PortRowDto(
            p.Id, p.IfIndex, p.Name, p.Alias, p.OperUp ? "UP" : "DOWN", p.PortType, p.SpeedMbps, p.Vlan, macCounts.GetValueOrDefault(p.Id), p.PhysAddress is null ? null : MacAddress.Format(p.PhysAddress),
            p.PortType == PortType.Down ? [] : devs[p.Id].OrderBy(d => d.Mac).Take(10).Select(d => new PortDeviceDto(
                d.Id, d.Hostname, d.IpAddress, MacAddress.Format(d.Mac), d.Vlan, d.LastSeen, DeviceStates.Compute(d.LastSeen, now, opt.Value), d.LowConfidence)).ToList())).ToList();
        var summary = new PortSummary(rows.Count, rows.Count(r => r.Status == "UP"), rows.Count(r => r.Status == "DOWN"), rows.Count(r => r.Type == PortType.SwitchUplink), sw.LastScanAt);
        var hasArp = await db.Switches.AnyAsync(s => s.UseAsArpSource || s.ArpAutoDetected, ct);
        return new PortsResponse(summary, rows, hasArp);
    }

    /// <summary>Sorts Gi1/0/2 before Gi1/0/10 by zero-padding digit runs.</summary>
    internal static string NaturalKey(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\d+", m => m.Value.PadLeft(6, '0'));
}

public class DeviceQueryService(AppDbContext db, IOptions<DiscoveryOptions> opt)
{
    public async Task<Paged<DeviceDto>> ListAsync(DeviceQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 200);
        var o = opt.Value; var now = DateTime.UtcNow;
        var query = db.Devices.AsNoTracking().AsQueryable();
        if (q.SwitchId is { } sid) query = query.Where(d => d.SwitchId == sid);
        if (q.State is { } st)
        {
            var offline = now.AddMinutes(-o.OfflineAfterMinutes); var stale = now.AddDays(-o.StaleAfterDays);
            query = st switch
            {
                DeviceState.Active => query.Where(d => d.LastSeen >= offline),
                DeviceState.Offline => query.Where(d => d.LastSeen < offline && d.LastSeen >= stale),
                _ => query.Where(d => d.LastSeen < stale)
            };
        }
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var t = q.Q.Trim(); var mac = MacAddress.PartialKey(t);
            query = query.Where(d => (d.Hostname != null && d.Hostname.Contains(t)) || (d.IpAddress != null && d.IpAddress.Contains(t))
                || (mac != null && d.Mac.Contains(mac)) || (d.Switch != null && d.Switch.Name.Contains(t)) || (d.Port != null && d.Port.Name.Contains(t)));
        }
        var total = await query.CountAsync(ct);
        query = (q.Sort?.ToLowerInvariant(), q.Desc) switch
        {
            ("hostname", false) => query.OrderBy(d => d.Hostname), ("hostname", true) => query.OrderByDescending(d => d.Hostname),
            ("ip", false) => query.OrderBy(d => d.IpAddress), ("ip", true) => query.OrderByDescending(d => d.IpAddress),
            ("mac", false) => query.OrderBy(d => d.Mac), ("mac", true) => query.OrderByDescending(d => d.Mac),
            ("switch", false) => query.OrderBy(d => d.Switch!.Name), ("switch", true) => query.OrderByDescending(d => d.Switch!.Name),
            ("vlan", false) => query.OrderBy(d => d.Vlan), ("vlan", true) => query.OrderByDescending(d => d.Vlan),
            ("lastseen", false) => query.OrderBy(d => d.LastSeen), _ => query.OrderByDescending(d => d.LastSeen),
        };
        var items = await query.Skip((page - 1) * size).Take(size).Select(d => new
        { d.Id, d.Hostname, d.HostnameSource, d.IpAddress, d.Mac, d.SwitchId, SwitchName = d.Switch != null ? d.Switch.Name : null,
          d.PortId, IfIndex = d.Port != null ? (int?)d.Port.IfIndex : null, PortName = d.Port != null ? d.Port.Name : null, d.Vlan, d.FirstSeen, d.LastSeen, d.LowConfidence }).ToListAsync(ct);
        return new Paged<DeviceDto>(items.Select(d => new DeviceDto(d.Id, d.Hostname, d.HostnameSource, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.SwitchName,
            d.PortId, d.IfIndex, d.PortName, d.Vlan, DeviceStates.Compute(d.LastSeen, now, o), d.FirstSeen, d.LastSeen, d.LowConfidence)).ToList(), total, page, size);
    }
}
