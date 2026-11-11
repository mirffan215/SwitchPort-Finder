using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Domain.Entities;

/// <summary>
/// Classifies devices. Every filled-in condition must match (AND); the lowest Priority number that matches wins. Text conditions are case-insensitive "contains"
/// (HostnameStartsWith: "starts with"). "Neighbor" means what the device announces about itself by LLDP/CDP on the switch port it is plugged into.
/// </summary>
public class CategoryRule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public DeviceCategory Category { get; set; }
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? VendorContains { get; set; }
    public string? HostnameStartsWith { get; set; }
    public string? HostnameContains { get; set; }
    public string? PortDescriptionContains { get; set; }
    /// <summary>Matched against the LLDP/CDP system name, description and platform of the neighbor on the device's port.</summary>
    public string? NeighborContains { get; set; }
    /// <summary>"WlanAp" or "Telephone": the LLDP system-capability bit the neighbor announces.</summary>
    public string? NeighborCapability { get; set; }
    public int? Vlan { get; set; }
}
