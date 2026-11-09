using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Entities;
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
        var neighbors = await NeighborsFor(ports.Select(p => p.NeighborId).Where(i => i != null).Select(i => i!.Value).ToList(), ct);
        var now = DateTime.UtcNow;
        var rows = ports.OrderBy(p => NaturalKey(p.Name), StringComparer.Ordinal).Select(p => new PortRowDto(
            p.Id, p.IfIndex, p.Name, p.Alias, p.OperUp ? "UP" : "DOWN", p.PortType, p.SpeedMbps, p.Vlan, macCounts.GetValueOrDefault(p.Id), p.PhysAddress is null ? null : MacAddress.Format(p.PhysAddress),
            // Spec 5.8: an uplink never lists downstream devices (and a down port has none).
            (p.PortType is PortType.Down or PortType.SwitchUplink) ? new List<PortDeviceDto>() : devs[p.Id].OrderBy(d => d.Mac).Take(10).Select(d => new PortDeviceDto(
                d.Id, d.Hostname, d.IpAddress, MacAddress.Format(d.Mac), d.Vlan, d.LastSeen, DeviceStates.Compute(d.LastSeen, now, opt.Value), d.LowConfidence)).ToList(),
            p.NeighborId is { } nid && neighbors.TryGetValue(nid, out var nb) ? nb with { Confidence = p.Confidence } : null,
            SupportList(p))).ToList();
        var summary = new PortSummary(rows.Count, rows.Count(r => r.Status == "UP"), rows.Count(r => r.Status == "DOWN"), rows.Count(r => r.Type == PortType.SwitchUplink), sw.LastScanAt);
        var hasArp = await db.Switches.AnyAsync(s => s.UseAsArpSource || s.ArpAutoDetected, ct);
        return new PortsResponse(summary, rows, hasArp);
    }

    private static List<string> SupportList(SwitchPort p)
    {
        var l = new List<string>();
        if (p.Support.HasFlag(PortSupport.StpRootPort)) l.Add("Spanning-tree root port");
        if (p.Support.HasFlag(PortSupport.TaggedVlans)) l.Add($"Carries tagged VLANs ({p.VlanCount} memberships)");
        if (p.Support.HasFlag(PortSupport.DescriptionHint)) l.Add("Description suggests an uplink");
        return l;
    }

    /// <summary>All LLDP/CDP/inferred neighbors of a switch, one row per neighbor.</summary>
    public async Task<List<NeighborRowDto>> NeighborsAsync(int switchId, CancellationToken ct)
    {
        if (!await db.Switches.AnyAsync(s => s.Id == switchId, ct)) throw new NotFoundException($"Switch {switchId} not found.");
        var ports = await db.SwitchPorts.AsNoTracking().Where(p => p.SwitchId == switchId).ToDictionaryAsync(p => p.Id, ct);
        var rows = await db.Neighbors.AsNoTracking().Where(n => n.SwitchId == switchId).ToListAsync(ct);
        var map = await NeighborsFor(rows.Select(r => r.Id).ToList(), ct);
        return rows.Select(r => new NeighborRowDto(r.PortId, ports[r.PortId].IfIndex, ports[r.PortId].Name, map[r.Id] with { Confidence = ports[r.PortId].NeighborId == r.Id ? ports[r.PortId].Confidence : map[r.Id].Confidence }))
            .OrderBy(r => NaturalKey(r.PortName), StringComparer.Ordinal).ToList();
    }

    public async Task<Dictionary<long, NeighborDto>> NeighborsFor(List<long> ids, CancellationToken ct)
    {
        var result = new Dictionary<long, NeighborDto>();
        if (ids.Count == 0) return result;
        var rows = new List<SwitchNeighbor>();
        foreach (var chunk in ids.Chunk(500)) rows.AddRange(await db.Neighbors.AsNoTracking().Where(n => chunk.Contains(n.Id)).ToListAsync(ct));
        var swIds = rows.Where(r => r.MatchedSwitchId != null).Select(r => r.MatchedSwitchId!.Value).Distinct().ToList();
        var switches = await db.Switches.AsNoTracking().Where(s => swIds.Contains(s.Id)).Select(s => new { s.Id, s.Name, s.ManagementIp }).ToDictionaryAsync(s => s.Id, ct);
        var remotePorts = (await db.SwitchPorts.AsNoTracking().Where(p => swIds.Contains(p.SwitchId)).Select(p => new { p.SwitchId, p.IfIndex, p.Name, p.Descr, p.Alias }).ToListAsync(ct)).ToLookup(p => p.SwitchId);
        foreach (var r in rows)
        {
            var conn = r.MatchedSwitchId is { } id && switches.TryGetValue(id, out var c) ? c : null;
            int? remoteIf = null;
            if (conn is not null)
            {
                var rp = r.RemotePortId ?? r.RemotePortDescr;
                var cand = remotePorts[conn.Id];
                remoteIf = rp is null ? null : cand.FirstOrDefault(p => string.Equals(p.Name, rp, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Descr, rp, StringComparison.OrdinalIgnoreCase)
                                                        || (r.RemotePortDescr is not null && string.Equals(p.Name, r.RemotePortDescr, StringComparison.OrdinalIgnoreCase)))?.IfIndex
                           ?? (int.TryParse(rp, out var n) && cand.Any(p => p.IfIndex == n) ? n : null);
            }
            var isSwitch = r.Caps.HasFlag(NeighborCaps.Bridge) || r.Caps.HasFlag(NeighborCaps.Router);
            result[r.Id] = new NeighborDto(r.Id, r.Protocol, conn?.Id, conn?.Name, conn?.ManagementIp, r.SysName, r.ChassisId, r.RemotePortId ?? r.RemotePortDescr, remoteIf, r.MgmtIp, r.Platform,
                r.Protocol == NeighborProtocol.Inferred ? NeighborConfidence.LikelySwitch : isSwitch || conn is not null ? NeighborConfidence.ConfirmedSwitch : NeighborConfidence.Endpoint,
                r.Evidence, CapText(r.Caps), conn is null && r.Protocol != NeighborProtocol.Inferred && isSwitch);
        }
        return result;
    }

    private static string CapText(NeighborCaps c) => c == NeighborCaps.None ? "" : string.Join(", ", Enum.GetValues<NeighborCaps>().Where(f => f != NeighborCaps.None && c.HasFlag(f)).Select(f => f.ToString()));

    /// <summary>Sorts Gi1/0/2 before Gi1/0/10 by zero-padding digit runs.</summary>
    internal static string NaturalKey(string s) => System.Text.RegularExpressions.Regex.Replace(s, @"\d+", m => m.Value.PadLeft(6, '0'));
}

public class DeviceQueryService(AppDbContext db, IOptions<DiscoveryOptions> opt)
{
    /// <summary>Shared by the paged list and the export so "respect the current filters" is true by construction.</summary>
    private IQueryable<Device> Filter(DeviceQuery q, DateTime now)
    {
        var o = opt.Value;
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
            var t = q.Q.Trim(); var mac = ParsedQuery.Parse(t)?.MacKey;
            var pat = SqlText.Contains(t);
            query = query.Where(d => EF.Functions.Like(d.Hostname, pat, SqlText.Escape) || (d.IpAddress != null && EF.Functions.Like(d.IpAddress, pat, SqlText.Escape))
                || (mac != null && EF.Functions.Like(d.Mac, "%" + mac + "%")) || (d.Switch != null && EF.Functions.Like(d.Switch.Name, pat, SqlText.Escape))
                || (d.Port != null && EF.Functions.Like(d.Port.Name, pat, SqlText.Escape)));
        }
        return query;
    }

    private static IQueryable<Device> Sort(IQueryable<Device> query, DeviceQuery q) => (q.Sort?.ToLowerInvariant(), q.Desc) switch
    {
        ("hostname", false) => query.OrderBy(d => d.Hostname), ("hostname", true) => query.OrderByDescending(d => d.Hostname),
        ("ip", false) => query.OrderBy(d => d.IpAddress), ("ip", true) => query.OrderByDescending(d => d.IpAddress),
        ("mac", false) => query.OrderBy(d => d.Mac), ("mac", true) => query.OrderByDescending(d => d.Mac),
        ("switch", false) => query.OrderBy(d => d.Switch!.Name), ("switch", true) => query.OrderByDescending(d => d.Switch!.Name),
        ("vlan", false) => query.OrderBy(d => d.Vlan), ("vlan", true) => query.OrderByDescending(d => d.Vlan),
        ("lastseen", false) => query.OrderBy(d => d.LastSeen), _ => query.OrderByDescending(d => d.LastSeen),
    };

    private async Task<List<DeviceDto>> Project(IQueryable<Device> query, DateTime now, CancellationToken ct)
    {
        var o = opt.Value;
        var items = await query.Select(d => new
        { d.Id, d.Hostname, d.HostnameSource, d.IpAddress, d.Mac, d.SwitchId, SwitchName = d.Switch != null ? d.Switch.Name : null,
          d.PortId, IfIndex = d.Port != null ? (int?)d.Port.IfIndex : null, PortName = d.Port != null ? d.Port.Name : null, d.Vlan, d.FirstSeen, d.LastSeen, d.LowConfidence }).ToListAsync(ct);
        return items.Select(d => new DeviceDto(d.Id, d.Hostname, d.HostnameSource, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.SwitchName,
            d.PortId, d.IfIndex, d.PortName, d.Vlan, DeviceStates.Compute(d.LastSeen, now, o), d.FirstSeen, d.LastSeen, d.LowConfidence)).ToList();
    }

    public async Task<Paged<DeviceDto>> ListAsync(DeviceQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 200); var now = DateTime.UtcNow;
        var query = Filter(q, now);
        var total = await query.CountAsync(ct);
        return new Paged<DeviceDto>(await Project(Sort(query, q).Skip((page - 1) * size).Take(size), now, ct), total, page, size);
    }

    /// <summary>Every device matching the filters (no paging), same order as the list. Bounded by <paramref name="max"/>.</summary>
    public async Task<(List<DeviceDto> Rows, int Total)> ExportAsync(DeviceQuery q, int max, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var query = Filter(q, now);
        var total = await query.CountAsync(ct);
        if (total > max) throw new ValidationFailedException("export", $"{total} devices match; the export limit is {max}. Narrow the filters.");
        return (await Project(Sort(query, q), now, ct), total);
    }

    public async Task<List<DeviceDto>> RecentAsync(int take, CancellationToken ct) =>
        await Project(db.Devices.AsNoTracking().OrderByDescending(d => d.FirstSeen).ThenByDescending(d => d.Id).Take(take), DateTime.UtcNow, ct);

    public async Task<DeviceDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var o = opt.Value; var now = DateTime.UtcNow;
        var d = await db.Devices.AsNoTracking().Include(x => x.Switch).Include(x => x.Port).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException($"Device {id} not found.");
        var ipSource = d.IpSourceSwitchId is { } sid ? await db.Switches.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct) : null;
        var obs = await db.Observations.AsNoTracking().Where(x => x.Mac == d.Mac).Join(db.SwitchPorts.AsNoTracking(), x => x.PortId, p => p.Id, (x, p) => new { x.SwitchId, x.Vlan, x.LastSeen, p })
            .ToListAsync(ct);
        var names = await db.Switches.AsNoTracking().Where(s => obs.Select(x => x.SwitchId).Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var portIds = obs.Select(x => x.p.Id).ToList();
        var counts = await db.Observations.AsNoTracking().Where(x => portIds.Contains(x.PortId)).GroupBy(x => x.PortId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var sightings = obs.OrderByDescending(x => x.LastSeen).Select(x => new SightingDto(x.SwitchId, names.GetValueOrDefault(x.SwitchId, "?"), x.p.Id, x.p.IfIndex, x.p.Name,
            x.p.PortType == PortType.SwitchUplink, counts.GetValueOrDefault(x.p.Id), x.Vlan, x.LastSeen)).ToList();
        var dto = new DeviceDto(d.Id, d.Hostname, d.HostnameSource, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.Switch?.Name, d.PortId, d.Port?.IfIndex, d.Port?.Name, d.Vlan,
            DeviceStates.Compute(d.LastSeen, now, o), d.FirstSeen, d.LastSeen, d.LowConfidence);
        var note = d.SwitchId is null ? "Not seen on an edge port yet: it has only been learned behind switch uplinks. Scan the switch it connects to."
            : d.LowConfidence ? "Seen on more than one edge port; the port with the fewest MAC addresses was chosen." : "Learned on an edge port (not a switch uplink).";
        return new DeviceDetailDto(dto, ipSource, d.IpObservedAt, note, sightings);
    }
}
