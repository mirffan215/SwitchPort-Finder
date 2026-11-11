namespace SwitchPortFinder.Domain.Entities;

/// <summary>An access point as listed by a wireless controller (Cisco CISCO-LWAPP-AP-MIB / AIRESPACE-WIRELESS-MIB). Rows are refreshed by every scan of the controller.</summary>
public class WirelessAp
{
    public int Id { get; set; }
    public int ControllerId { get; set; }
    public Switch? Controller { get; set; }
    /// <summary>The AP's identity on the controller (12 hex digits, upper case).</summary>
    public string SysMac { get; set; } = "";
    /// <summary>The MAC of its wired Ethernet interface: the one the switch learns on its port. May equal SysMac.</summary>
    public string? EthMac { get; set; }
    public string? Name { get; set; }
    public string? Model { get; set; }
    public string? Serial { get; set; }
    public string? Ip { get; set; }
    public long? UptimeSeconds { get; set; }
    /// <summary>Wi-Fi clients associated right now, as reported by the controller.</summary>
    public int? Clients { get; set; }
    public bool? AdminEnabled { get; set; }
    /// <summary>False when the controller lists the AP but reports it as not associated (AireOS operation status).</summary>
    public bool? Associated { get; set; }
    public DateTime FirstSeen { get; set; }
    /// <summary>Last controller scan that listed this AP. An AP the latest scan did not list has left the controller.</summary>
    public DateTime LastSeen { get; set; }
}
