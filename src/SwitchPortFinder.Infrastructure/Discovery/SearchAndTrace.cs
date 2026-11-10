using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>Portable, injection-safe substring matching: SQL LIKE with the wildcard characters escaped (case-insensitive on SQL Server and SQLite).</summary>
public static class SqlText
{
    public const string Escape = "\\";
    public static string Contains(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";
}

public class SearchService(AppDbContext db, IOptions<DiscoveryOptions> opt)
{
    private const int DeviceLimit = 25, SwitchLimit = 10, PortLimit = 25;

    public async Task<SearchResponse> SearchAsync(string? q, CancellationToken ct)
    {
        var pq = ParsedQuery.Parse(q);
        if (pq is null) return new SearchResponse(q?.Trim() ?? "", [], [], [], false);
        var o = opt.Value; var now = DateTime.UtcNow;
        var pat = SqlText.Contains(pq.Text);
        var macLike = pq.MacKey is null ? null : "%" + pq.MacKey + "%";
        var macExact = MacAddress.Normalize(pq.Text);
        var ipExact = pq.IpExact; var text = pq.Text;
        // "50" or "vlan 50" finds everything on VLAN 50
        int? vlan = int.TryParse(System.Text.RegularExpressions.Regex.Replace(text, @"^(?i:vlan)\s*", ""), out var vn) && vn is >= 1 and <= 4094 ? vn : null;

        // ---- devices: MAC (any format / partial), IP, hostname, switch name/IP, port name ----
        var dq = db.Devices.AsNoTracking().Where(d =>
            EF.Functions.Like(d.Hostname, pat, SqlText.Escape)
            || (d.IpAddress != null && EF.Functions.Like(d.IpAddress, pat, SqlText.Escape))
            || (d.Vendor != null && EF.Functions.Like(d.Vendor, pat, SqlText.Escape))
            || (macLike != null && EF.Functions.Like(d.Mac, macLike))
            || (vlan != null && d.Vlan == vlan)
            || (d.Switch != null && (EF.Functions.Like(d.Switch.Name, pat, SqlText.Escape) || EF.Functions.Like(d.Switch.ManagementIp, pat, SqlText.Escape)))
            || (d.Port != null && EF.Functions.Like(d.Port.Name, pat, SqlText.Escape)));
        var rows = await dq
            .OrderBy(d => d.Mac == macExact || d.IpAddress == ipExact || d.Hostname == text ? 0 : 1)   // exact hits first
            .ThenByDescending(d => d.LastSeen)
            .Take(DeviceLimit + 1)
            .Select(d => new
            {
                d.Id, d.Hostname, d.HostnameSource, d.IpAddress, d.Mac, d.SwitchId, SwitchName = d.Switch != null ? d.Switch.Name : null, d.PortId,
                IfIndex = d.Port != null ? (int?)d.Port.IfIndex : null, PortName = d.Port != null ? d.Port.Name : null, d.Vlan, d.FirstSeen, d.LastSeen, d.LowConfidence, d.Vendor
            }).ToListAsync(ct);
        var truncated = rows.Count > DeviceLimit;
        var devices = rows.Take(DeviceLimit).Select(d => new DeviceDto(d.Id, d.Hostname, d.HostnameSource, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.SwitchName, d.PortId, d.IfIndex,
            d.PortName, d.Vlan, DeviceStates.Compute(d.LastSeen, now, o), d.FirstSeen, d.LastSeen, d.LowConfidence, VendorText.Of(d.Vendor, d.Mac))).ToList();

        // ---- switches: name, management IP, sysName ----
        var switches = (await db.Switches.AsNoTracking().Where(s => EF.Functions.Like(s.Name, pat, SqlText.Escape) || EF.Functions.Like(s.ManagementIp, pat, SqlText.Escape)
                || (s.SysName != null && EF.Functions.Like(s.SysName, pat, SqlText.Escape))
                || (s.Location != null && EF.Functions.Like(s.Location, pat, SqlText.Escape)) || (s.Model != null && EF.Functions.Like(s.Model, pat, SqlText.Escape))
                || (s.SerialNumber != null && EF.Functions.Like(s.SerialNumber, pat, SqlText.Escape)) || (s.Notes != null && EF.Functions.Like(s.Notes, pat, SqlText.Escape)))
            .OrderBy(s => s.Name).Take(SwitchLimit).ToListAsync(ct))
            .Select(s => new SwitchHit(s.Id, s.Name, s.ManagementIp, s.Manufacturer == Manufacturer.AutoDetect && s.DetectedManufacturer is { } dm ? Application.Snmp.VendorDetector.DisplayName(dm) : Application.Snmp.VendorDetector.DisplayName(s.Manufacturer),
                s.Status.ToString(), s.Model)).ToList();

        // ---- ports: name or description ----
        var ports = (await db.SwitchPorts.AsNoTracking().Where(p => EF.Functions.Like(p.Name, pat, SqlText.Escape) || (p.Alias != null && EF.Functions.Like(p.Alias, pat, SqlText.Escape)) || (p.Descr != null && EF.Functions.Like(p.Descr, pat, SqlText.Escape)) || (vlan != null && p.Vlan == vlan))
            .Join(db.Switches, p => p.SwitchId, s => s.Id, (p, s) => new { p, s.Name, s.ManagementIp }).OrderBy(x => x.Name).ThenBy(x => x.p.IfIndex).Take(PortLimit + 1).ToListAsync(ct));
        truncated |= ports.Count > PortLimit;
        var portHits = ports.Take(PortLimit).Select(x => new PortHit(x.p.SwitchId, x.Name, x.ManagementIp, x.p.Id, x.p.IfIndex, x.p.Name, x.p.Alias, x.p.OperUp ? "UP" : "DOWN", x.p.PortType)).ToList();
        return new SearchResponse(pq.Text, devices, switches, portHits, truncated);
    }
}

public class TraceService(AppDbContext db, PortQueryService ports)
{
    public async Task<TraceResult> TraceAsync(int deviceId, CancellationToken ct)
    {
        var d = await db.Devices.AsNoTracking().Include(x => x.Port).FirstOrDefaultAsync(x => x.Id == deviceId, ct) ?? throw new NotFoundException($"Device {deviceId} not found.");
        var switches = (await db.Switches.AsNoTracking().Select(s => new { s.Id, s.Name, s.ManagementIp, s.UseAsArpSource, s.ArpAutoDetected }).ToListAsync(ct))
            .Select(s => new TraceSwitch(s.Id, s.Name, s.ManagementIp, s.UseAsArpSource || s.ArpAutoDetected)).ToList();

        // Proven links: uplink ports whose explaining neighbor resolved to a registered switch.
        var uplinks = await db.SwitchPorts.AsNoTracking().Where(p => p.PortType == PortType.SwitchUplink && p.NeighborId != null).ToListAsync(ct);
        var dtos = await ports.NeighborsFor(uplinks.Select(p => p.NeighborId!.Value).ToList(), ct);
        var links = uplinks.Where(p => dtos.TryGetValue(p.NeighborId!.Value, out var n) && n.ConnectedSwitchId != null)
            .Select(p => { var n = dtos[p.NeighborId!.Value]; return new TraceLink(p.SwitchId, p.Id, p.Name, p.IfIndex, n.ConnectedSwitchId!.Value, p.Confidence, n.RemotePort, n.RemotePortIfIndex); }).ToList();

        var dev = new TraceDevice(d.Id, d.Hostname, d.IpAddress, MacAddress.Format(d.Mac), d.SwitchId, d.PortId, d.Port?.Name, d.Port?.IfIndex, d.Vlan, d.LowConfidence);
        return TraceBuilder.Build(dev, switches, links);
    }
}
