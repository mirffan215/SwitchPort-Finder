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
                d.Id, d.Hostname, d.IpAddress, MacAddress.Format(d.Mac), d.Vlan, d.LastSeen, DeviceStates.Compute(d.LastSeen, DeviceStates.Reference(d.LastSeen, sw.LastScanAt, null), opt.Value), d.LowConfidence)).ToList(),
            p.NeighborId is { } nid && neighbors.TryGetValue(nid, out var nb) ? nb with { Confidence = p.Confidence } : null,
            SupportList(p), TrafficOf(p))).ToList();
        var summary = new PortSummary(rows.Count, rows.Count(r => r.Status == "UP"), rows.Count(r => r.Status == "DOWN"), rows.Count(r => r.Type == PortType.SwitchUplink), sw.LastScanAt);
        var hasArp = await db.Switches.AnyAsync(s => s.UseAsArpSource || s.ArpAutoDetected, ct);
        return new PortsResponse(summary, rows, hasArp);
    }

    /// <summary>Every port of every switch, filterable and pageable (the page behind the dashboard's Connected Ports / Switch Uplinks cards).</summary>
    public async Task<Paged<PortListItem>> ListAllAsync(PortListQuery q, CancellationToken ct)
    {
        var size = Math.Clamp(q.PageSize, 1, 500); var page = Math.Max(1, q.Page);
        var query = db.SwitchPorts.AsNoTracking().AsQueryable();
        if (q.SwitchId is { } sid) query = query.Where(p => p.SwitchId == sid);
        if (string.Equals(q.Status, "up", StringComparison.OrdinalIgnoreCase)) query = query.Where(p => p.OperUp);
        else if (string.Equals(q.Status, "down", StringComparison.OrdinalIgnoreCase)) query = query.Where(p => !p.OperUp);
        if (q.Type is { } t) query = query.Where(p => p.PortType == t);
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var text = q.Q.Trim(); if (text.Length > ParsedQuery.MaxLength) text = text[..ParsedQuery.MaxLength];
            var pat = SqlText.Contains(text); int? vlan = int.TryParse(text, out var vn) && vn is >= 1 and <= 4094 ? vn : null;
            query = query.Where(p => EF.Functions.Like(p.Name, pat, SqlText.Escape) || (p.Alias != null && EF.Functions.Like(p.Alias, pat, SqlText.Escape))
                || EF.Functions.Like(p.Switch!.Name, pat, SqlText.Escape) || (vlan != null && p.Vlan == vlan));
        }
        var total = await query.CountAsync(ct);
        var ordered = (q.Sort?.ToLowerInvariant(), q.Desc) switch
        {
            ("switch", false) => query.OrderBy(p => p.Switch!.Name).ThenBy(p => p.IfIndex), ("switch", true) => query.OrderByDescending(p => p.Switch!.Name).ThenBy(p => p.IfIndex),
            ("speed", false) => query.OrderBy(p => p.SpeedMbps), ("speed", true) => query.OrderByDescending(p => p.SpeedMbps),
            ("vlan", false) => query.OrderBy(p => p.Vlan), ("vlan", true) => query.OrderByDescending(p => p.Vlan),
            ("util", false) => query.OrderBy(p => p.RateInBps), ("util", true) => query.OrderByDescending(p => p.RateInBps),
            ("name", true) => query.OrderByDescending(p => p.Name), ("name", false) => query.OrderBy(p => p.Name),
            _ => query.OrderBy(p => p.Switch!.Name).ThenBy(p => p.IfIndex),
        };
        var rows = await ordered.Skip((page - 1) * size).Take(size).Select(p => new { p, SwitchName = p.Switch!.Name }).ToListAsync(ct);
        var ids = rows.Select(r => r.p.Id).ToList();
        var macCounts = await db.Observations.AsNoTracking().Where(o => ids.Contains(o.PortId)).GroupBy(o => o.PortId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);
        var devs = await db.Devices.AsNoTracking().Where(d => d.PortId != null && ids.Contains(d.PortId!.Value)).OrderBy(d => d.Mac).Select(d => new { PortId = d.PortId!.Value, d.Hostname, d.IpAddress, d.Mac }).ToListAsync(ct);
        var first = devs.GroupBy(d => d.PortId).ToDictionary(g => g.Key, g => g.First());
        var items = rows.Select(r => new PortListItem(r.p.SwitchId, r.SwitchName, r.p.Id, r.p.IfIndex, r.p.Name, r.p.Alias, r.p.OperUp ? "UP" : "DOWN", r.p.PortType, r.p.SpeedMbps, r.p.Vlan,
            macCounts.GetValueOrDefault(r.p.Id), r.p.PortType is PortType.Down or PortType.SwitchUplink ? null : first.TryGetValue(r.p.Id, out var d) ? (d.Hostname == "Unknown Device" ? (d.IpAddress ?? MacAddress.Format(d.Mac)) : d.Hostname) : null,
            TrafficOf(r.p))).ToList();
        return new Paged<PortListItem>(items, total, page, size);
    }

    /// <summary>Null until two scans have produced a trustworthy rate.</summary>
    public static PortTrafficDto? TrafficOf(SwitchPort p)
    {
        if (p.RateAt is not { } at || p.RateSeconds is not { } secs) return null;
        double? util = null;
        if (p.SpeedMbps > 0 && (p.RateInBps is not null || p.RateOutBps is not null))
            util = Math.Min(100, Math.Max(p.RateInBps ?? 0, p.RateOutBps ?? 0) / (p.SpeedMbps * 1_000_000d) * 100);
        return new PortTrafficDto(p.RateInBps, p.RateOutBps, util, p.RateDiscardsPerSec ?? 0, p.RateErrorsPerSec ?? 0, secs, at, p.CtrWide);
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

public static class VendorText
{
    /// <summary>Stored vendor, or computed from the MAC when the row predates the Vendor column; null when unknown.</summary>
    public static string? CategoryName(DeviceCategory c) => c == DeviceCategory.Unclassified ? null : c.ToString();
    public static string? Of(string? stored, string mac) => stored is null ? OuiLookup.Lookup(mac) : stored.Length == 0 ? null : stored;
}

public class DeviceQueryService(AppDbContext db, IOptions<DiscoveryOptions> opt)
{
    /// <summary>Shared by the paged list and the export so "respect the current filters" is true by construction.</summary>
    private async Task<DateTime?> LatestScanAsync(CancellationToken ct) => await db.Switches.AsNoTracking().MaxAsync(s => s.LastScanAt, ct);

    private IQueryable<Device> Filter(DeviceQuery q, DateTime? latest)
    {
        var o = opt.Value;
        var query = db.Devices.AsNoTracking().AsQueryable();
        if (q.SwitchId is { } sid) query = query.Where(d => d.SwitchId == sid);
        if (q.Category is { } cat) query = query.Where(d => d.Category == cat);
        if (q.State is { } st)
        {
            // reference = the last scan of the device's switch (else the newest scan anywhere): see DeviceStates
            var days = o.StaleAfterDays;
            query = st switch
            {
                DeviceState.Active => query.Where(d => d.LastSeen >= ((d.Switch != null && d.Switch.LastScanAt != null ? d.Switch.LastScanAt.Value : (latest ?? d.LastSeen))).AddMinutes(-DeviceStates.ToleranceMinutes)),
                DeviceState.Offline => query.Where(d => d.LastSeen < ((d.Switch != null && d.Switch.LastScanAt != null ? d.Switch.LastScanAt.Value : (latest ?? d.LastSeen))).AddMinutes(-DeviceStates.ToleranceMinutes)
                    && d.LastSeen >= ((d.Switch != null && d.Switch.LastScanAt != null ? d.Switch.LastScanAt.Value : (latest ?? d.LastSeen))).AddDays(-days)),
                _ => query.Where(d => d.LastSeen < ((d.Switch != null && d.Switch.LastScanAt != null ? d.Switch.LastScanAt.Value : (latest ?? d.LastSeen))).AddDays(-days))
            };
        }
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var t = q.Q.Trim(); if (t.Length > ParsedQuery.MaxLength) t = t[..ParsedQuery.MaxLength]; var mac = ParsedQuery.Parse(t)?.MacKey;
            var pat = SqlText.Contains(t);
            query = query.Where(d => EF.Functions.Like(d.Hostname, pat, SqlText.Escape) || (d.IpAddress != null && EF.Functions.Like(d.IpAddress, pat, SqlText.Escape))
                || (d.Vendor != null && EF.Functions.Like(d.Vendor, pat, SqlText.Escape)) || (d.Owner != null && EF.Functions.Like(d.Owner, pat, SqlText.Escape)) || (d.Notes != null && EF.Functions.Like(d.Notes, pat, SqlText.Escape)) || (mac != null && EF.Functions.Like(d.Mac, "%" + mac + "%")) || (d.Switch != null && EF.Functions.Like(d.Switch.Name, pat, SqlText.Escape))
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
        ("vendor", false) => query.OrderBy(d => d.Vendor), ("vendor", true) => query.OrderByDescending(d => d.Vendor),
        ("vlan", false) => query.OrderBy(d => d.Vlan), ("vlan", true) => query.OrderByDescending(d => d.Vlan),
        ("lastseen", false) => query.OrderBy(d => d.LastSeen), _ => query.OrderByDescending(d => d.LastSeen),
    };

    private async Task<List<DeviceDto>> Project(IQueryable<Device> query, DateTime? latest, CancellationToken ct)
    {
        var o = opt.Value;
        var items = await query.Select(d => new
        { d.Id, d.Hostname, d.HostnameSource, d.IpAddress, d.Mac, d.SwitchId, SwitchName = d.Switch != null ? d.Switch.Name : null,
          d.PortId, IfIndex = d.Port != null ? (int?)d.Port.IfIndex : null, PortName = d.Port != null ? d.Port.Name : null, d.Vlan, d.FirstSeen, d.LastSeen, d.LowConfidence, d.Vendor, d.IsManual, d.PinnedPortId, d.Owner, d.Notes, d.Category, d.CategoryManual, d.CategoryReason, SwitchScan = d.Switch != null ? d.Switch.LastScanAt : null }).ToListAsync(ct);
        return items.Select(d => new DeviceDto(d.Id, d.Hostname, d.HostnameSource, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.SwitchName,
            d.PortId, d.IfIndex, d.PortName, d.Vlan, DeviceStates.Compute(d.LastSeen, DeviceStates.Reference(d.LastSeen, d.SwitchScan, latest), o), d.FirstSeen, d.LastSeen, d.LowConfidence, VendorText.Of(d.Vendor, d.Mac), d.IsManual, d.PinnedPortId != null, d.Owner, d.Notes, VendorText.CategoryName(d.Category), d.CategoryManual, d.CategoryReason)).ToList();
    }

    public async Task<Paged<DeviceDto>> ListAsync(DeviceQuery q, CancellationToken ct)
    {
        var page = Math.Max(1, q.Page); var size = Math.Clamp(q.PageSize, 1, 200); var latest = await LatestScanAsync(ct);
        var query = Filter(q, latest);
        var total = await query.CountAsync(ct);
        return new Paged<DeviceDto>(await Project(Sort(query, q).Skip((page - 1) * size).Take(size), latest, ct), total, page, size);
    }

    /// <summary>Every device matching the filters (no paging), same order as the list. Bounded by <paramref name="max"/>.</summary>
    /// <summary>All access points with their location, status and the number of MAC addresses learned on their port (a rough "clients behind this AP"; wireless clients that a controller tunnels never appear here).</summary>
    public async Task<AccessPointsResponse> AccessPointsAsync(CancellationToken ct)
    {
        var latest = await LatestScanAsync(ct);
        var aps = await Project(db.Devices.AsNoTracking().Where(d => d.Category == DeviceCategory.AccessPoint).OrderBy(d => d.Switch!.Name).ThenBy(d => d.Port!.Name), latest, ct);
        var portIds = aps.Where(a => a.PortId != null).Select(a => a.PortId!.Value).Distinct().ToList();
        var macs = new Dictionary<int, int>(); var neigh = new Dictionary<int, string>();
        foreach (var chunk in portIds.Chunk(800))
        {
            foreach (var g in await db.Observations.AsNoTracking().Where(o => chunk.Contains(o.PortId)).GroupBy(o => o.PortId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)) macs[g.Key] = g.N;
            foreach (var n in await db.Neighbors.AsNoTracking().Where(n => chunk.Contains(n.PortId) && n.Protocol != NeighborProtocol.Inferred).Select(n => new { n.PortId, n.SysName, n.Platform }).ToListAsync(ct))
                neigh[n.PortId] = string.Join(" · ", new[] { n.SysName, n.Platform }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        var items = aps.Select(a => new AccessPointDto(a, a.PortId is { } p ? Math.Max(0, macs.GetValueOrDefault(p) - 1) : 0, a.PortId is { } p2 ? neigh.GetValueOrDefault(p2) : null)).ToList();
        var online = aps.Count(a => a.State == DeviceState.Active);
        KeyValuePair<string, int>[] Top(IEnumerable<string> keys) => keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToArray();
        return new AccessPointsResponse(new AccessPointSummary(aps.Count, online, aps.Count - online, Top(aps.Select(a => a.SwitchName ?? "Not located")), Top(aps.Select(a => a.Vendor ?? "Unknown"))), items);
    }

    public async Task<(List<DeviceDto> Rows, int Total)> ExportAsync(DeviceQuery q, int max, CancellationToken ct)
    {
        var latest = await LatestScanAsync(ct); var query = Filter(q, latest);
        var total = await query.CountAsync(ct);
        if (total > max) throw new ValidationFailedException("export", $"{total} devices match; the export limit is {max}. Narrow the filters.");
        return (await Project(Sort(query, q), latest, ct), total);
    }

    public async Task<List<DeviceDto>> RecentAsync(int take, CancellationToken ct) =>
        await Project(db.Devices.AsNoTracking().OrderByDescending(d => d.FirstSeen).ThenByDescending(d => d.Id).Take(take), await LatestScanAsync(ct), ct);

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
            DeviceStates.Compute(d.LastSeen, DeviceStates.Reference(d.LastSeen, d.Switch?.LastScanAt, await LatestScanAsync(ct)), o), d.FirstSeen, d.LastSeen, d.LowConfidence, VendorText.Of(d.Vendor, d.Mac), d.IsManual, d.PinnedPortId != null, d.Owner, d.Notes, VendorText.CategoryName(d.Category), d.CategoryManual, d.CategoryReason);
        var note = d.PinnedPortId != null ? "Location set manually by an administrator (kept across scans)." : d.SwitchId is null ? "Not seen on an edge port yet: it has only been learned behind switch uplinks. Scan the switch it connects to."
            : d.LowConfidence ? "Seen on more than one edge port; the port with the fewest MAC addresses was chosen." : "Learned on an edge port (not a switch uplink).";
        return new DeviceDetailDto(dto, ipSource, d.IpObservedAt, note, sightings);
    }
}
