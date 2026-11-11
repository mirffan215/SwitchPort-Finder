using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// The access-point list. Two sources, merged by MAC address: the wireless controller's own list (authoritative for names, Wi-Fi client counts, uptime and whether the AP is
/// joined) and the device rules (APs the switches see). An AP the controller lists is shown even if no switch scan has located it yet.
/// </summary>
public class AccessPointService(AppDbContext db, DeviceQueryService devices)
{
    public async Task<AccessPointsResponse> GetAsync(CancellationToken ct)
    {
        var latest = await devices.LatestScanAsync(ct);
        var devs = await devices.Project(db.Devices.AsNoTracking().Where(d => d.Category == DeviceCategory.AccessPoint), latest, ct);
        var byMac = devs.ToDictionary(d => d.Mac.Replace(":", ""), d => d);
        var waps = await db.WirelessAps.AsNoTracking().Select(a => new { a.Id, a.SysMac, a.EthMac, a.Name, a.Model, a.Ip, a.UptimeSeconds, a.Clients, a.AdminEnabled, a.Associated, a.LastSeen, Controller = a.Controller!.Name, CtrlScan = a.Controller.LastScanAt }).ToListAsync(ct);

        var portIds = devs.Where(d => d.PortId != null).Select(d => d.PortId!.Value).Distinct().ToList();
        var onPort = new Dictionary<int, int>(); var neigh = new Dictionary<int, string>();
        foreach (var chunk in portIds.Chunk(800))
        {
            foreach (var g in await db.Devices.AsNoTracking().Where(d => d.PortId != null && chunk.Contains(d.PortId!.Value)).GroupBy(d => d.PortId!.Value).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct)) onPort[g.Key] = g.N;
            foreach (var n in await db.Neighbors.AsNoTracking().Where(n => chunk.Contains(n.PortId) && n.Protocol != NeighborProtocol.Inferred).Select(n => new { n.PortId, n.SysName, n.Platform }).ToListAsync(ct))
                neigh[n.PortId] = string.Join(" · ", new[] { n.SysName, n.Platform }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        int Behind(DeviceDto d) => d.PortId is { } p ? Math.Max(0, onPort.GetValueOrDefault(p) - 1) : 0;

        var rows = new List<AccessPointDto>(); var used = new HashSet<int>();
        foreach (var a in waps)
        {
            DeviceDto? d = null;
            if (a.EthMac is not null && byMac.TryGetValue(a.EthMac, out var d1)) d = d1; else if (byMac.TryGetValue(a.SysMac, out var d2)) d = d2;
            if (d is not null) used.Add(d.Id);
            var listed = a.CtrlScan is null || a.LastSeen >= a.CtrlScan.Value.AddMinutes(-DeviceStates.ToleranceMinutes);     // the controller's latest scan listed it
            var joined = listed && a.Associated != false;
            var status = !listed ? "Offline" : a.Associated == false ? "Offline" : a.AdminEnabled == false ? "Disabled" : "Online";
            rows.Add(new AccessPointDto(d?.Id, a.Name ?? d?.Hostname ?? FormatMac(a.EthMac ?? a.SysMac), d?.Ip ?? a.Ip, FormatMac(a.EthMac ?? a.SysMac), d?.Vendor, a.Model, d?.SwitchId, d?.SwitchName, d?.PortId, d?.IfIndex, d?.PortName, d?.Vlan,
                joined && status == "Online", status, a.LastSeen, d is null ? 0 : Behind(d), a.Clients, a.UptimeSeconds, a.Controller, "Controller",
                !listed ? "No longer listed by the controller." : d is null ? "Not located on a switch port yet: scan the switch it connects to." : null));
        }
        foreach (var d in devs.Where(x => !used.Contains(x.Id)))
            rows.Add(new AccessPointDto(d.Id, d.Hostname == "Unknown Device" ? (d.PortId is { } p && neigh.TryGetValue(p, out var n) ? n.Split(" · ")[0] : FormatMac(d.Mac.Replace(":", ""))) : d.Hostname, d.Ip, d.Mac, d.Vendor, null, d.SwitchId, d.SwitchName, d.PortId, d.IfIndex, d.PortName, d.Vlan,
                d.State == DeviceState.Active, d.State == DeviceState.Active ? "Online" : "Offline", d.LastSeen, Behind(d), null, null, null, d.CategoryManual ? "Manual" : "Rules", d.CategoryReason));
        rows = rows.OrderBy(r => r.SwitchName ?? "~", StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

        KeyValuePair<string, int>[] Top(IEnumerable<string> keys) => keys.GroupBy(k => k).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => new KeyValuePair<string, int>(g.Key, g.Count())).ToArray();
        var online = rows.Count(r => r.Online);
        var clients = rows.Any(r => r.WifiClients is not null) ? rows.Sum(r => r.WifiClients ?? 0) : (int?)null;
        return new AccessPointsResponse(new AccessPointSummary(rows.Count, online, rows.Count - online, await db.Switches.CountAsync(s => s.IsWirelessController, ct), clients,
            Top(rows.Select(r => r.SwitchName ?? "Not located")), Top(rows.Select(r => r.Vendor ?? "Unknown"))), rows);
    }

    private static string FormatMac(string m) => string.Join(':', Enumerable.Range(0, m.Length / 2).Select(i => m.Substring(i * 2, 2)));
}
