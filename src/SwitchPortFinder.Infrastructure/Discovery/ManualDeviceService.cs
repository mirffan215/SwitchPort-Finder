using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// Administrator-entered details for a device: a name, an owner, notes and (the important part) a location on a switch port, so that a device the scans
/// cannot name or place - an unknown device behind an unmanaged switch, a printer that never answers DNS - can still be found and traced to the core.
/// A pinned location survives rescans; a typed name is never replaced by reverse DNS.
/// </summary>
public class ManualDeviceService(AppDbContext db, ILogger<ManualDeviceService> log)
{
    private static string? Clean(string? s, int max, string field)
    {
        var t = s?.Trim(); if (string.IsNullOrEmpty(t)) return null;
        if (t.Length > max) throw new ValidationFailedException(field, $"At most {max} characters.");
        if (t.Any(c => char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')) throw new ValidationFailedException(field, "Control characters are not allowed.");
        return t;
    }

    private async Task ApplyAsync(Device d, ManualDeviceRequest r, string actor, CancellationToken ct)
    {
        var name = Clean(r.Name, 255, "name");
        if (name is not null) { d.Hostname = name; d.HostnameSource = HostnameSource.Manual; }
        else if (d.HostnameSource == HostnameSource.Manual) { d.Hostname = "Unknown Device"; d.HostnameSource = HostnameSource.None; d.HostnameCheckedAt = null; }   // clearing the name hands it back to DNS
        d.Owner = Clean(r.Owner, 100, "owner"); d.Notes = Clean(r.Notes, 1000, "notes");
        if (!string.IsNullOrWhiteSpace(r.Ip))
        {
            var ip = SwitchPortFinder.Application.Switches.SwitchService.NormalizeIp(r.Ip.Trim()) ?? throw new ValidationFailedException("ip", "Not a valid IPv4/IPv6 address.");
            d.IpAddress = ip;
        }
        if (r.PortId is null && r.SwitchId is null) { d.PinnedSwitchId = null; d.PinnedPortId = null; d.PinnedVlan = null; }
        else
        {
            if (r.SwitchId is null || r.PortId is null) throw new ValidationFailedException("portId", "Choose both the switch and the port.");
            var port = await db.SwitchPorts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == r.PortId && p.SwitchId == r.SwitchId, ct) ?? throw new ValidationFailedException("portId", "That port does not belong to that switch.");
            if (port.PortType == PortType.SwitchUplink) throw new ValidationFailedException("portId", "That port is a switch uplink (it leads to another switch). Choose the port the device is plugged into.");
            if (r.Vlan is < 1 or > 4094) throw new ValidationFailedException("vlan", "VLAN must be 1-4094.");
            d.PinnedSwitchId = port.SwitchId; d.PinnedPortId = port.Id; d.PinnedVlan = r.Vlan ?? port.Vlan;
            d.SwitchId = port.SwitchId; d.PortId = port.Id; d.Vlan = d.PinnedVlan; d.LowConfidence = false;
        }
        if (d.PinnedPortId is null && !d.IsManual) { /* discovered location (if any) stays as classification last set it */ }
        d.ManualUpdatedBy = actor; d.ManualUpdatedAt = DateTime.UtcNow;
    }

    public async Task UpdateAsync(int id, ManualDeviceRequest r, string actor, CancellationToken ct)
    {
        var d = await db.Devices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException($"Device {id} not found.");
        await ApplyAsync(d, r, actor, ct);
        if (d.PinnedPortId is null) await RestoreDiscoveredLocationAsync(d, ct);
        await db.SaveChangesAsync(ct);
        log.LogInformation("DeviceEdited: device {DeviceId} by {Actor} (pinned: {Pinned})", d.Id, actor, d.PinnedPortId != null);
    }

    /// <summary>Removing a pin puts the device back where scans last saw it on an edge port (or nowhere).</summary>
    private async Task RestoreDiscoveredLocationAsync(Device d, CancellationToken ct)
    {
        var best = await db.Observations.AsNoTracking().Where(o => o.Mac == d.Mac && o.Port!.PortType != PortType.SwitchUplink && o.Port.PortType != PortType.Down)
            .OrderByDescending(o => o.LastSeen).Select(o => new { o.SwitchId, o.PortId, o.Vlan }).FirstOrDefaultAsync(ct);
        d.SwitchId = best?.SwitchId; d.PortId = best?.PortId; d.Vlan = best?.Vlan;
    }

    public async Task<int> CreateAsync(ManualDeviceRequest r, string actor, CancellationToken ct)
    {
        var mac = MacAddress.Normalize(r.Mac) ?? throw new ValidationFailedException("mac", "Enter a MAC address like AA:BB:CC:DD:EE:FF.");
        if (OuiLookup.Lookup(mac) is null && (Convert.ToInt32(mac[..2], 16) & 1) != 0) throw new ValidationFailedException("mac", "That is a multicast address, not a device.");
        if (await db.Devices.AnyAsync(x => x.Mac == mac, ct)) throw new ConflictException("A device with this MAC address already exists. Open it and use Edit details.");
        var now = DateTime.UtcNow;
        var d = new Device { Mac = mac, IsManual = true, FirstSeen = now, LastSeen = now, Vendor = OuiLookup.Lookup(mac) ?? "" };
        await ApplyAsync(d, r, actor, ct);
        db.Devices.Add(d); await db.SaveChangesAsync(ct);
        log.LogInformation("DeviceAdded: manual device {DeviceId} by {Actor}", d.Id, actor);
        return d.Id;
    }

    public async Task DeleteAsync(int id, string actor, CancellationToken ct)
    {
        var d = await db.Devices.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException($"Device {id} not found.");
        if (!d.IsManual) throw new ConflictException("This device was found by a scan and would reappear. Clear its manual details instead; it ages out by itself when it disappears.");
        if (await db.Observations.AnyAsync(o => o.Mac == d.Mac, ct)) throw new ConflictException("This device has since been seen by a scan. Clear its manual details instead of deleting it.");
        db.Devices.Remove(d); await db.SaveChangesAsync(ct);
        log.LogInformation("DeviceDeleted: manual device {DeviceId} by {Actor}", id, actor);
    }
}
