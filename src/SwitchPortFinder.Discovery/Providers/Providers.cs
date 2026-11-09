using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery.Providers;

/// <summary>
/// Vendor quirks only. All standards-based discovery (IF-MIB, Q-BRIDGE/BRIDGE-MIB, IP-MIB) lives in the scanner so any
/// unknown manufacturer still returns ports, MACs and ARP via <see cref="GenericProvider"/>.
/// </summary>
public interface ISwitchDiscoveryProvider
{
    Manufacturer Manufacturer { get; }
    /// <summary>BRIDGE-MIB is per-VLAN (community@vlan / context vlan-N) and must be queried VLAN by VLAN.</summary>
    bool UsesPerVlanFdb { get; }
    /// <summary>Vendor-specific VLAN/port VLAN OIDs used when Q-BRIDGE has no data.</summary>
    bool HasCiscoVlanMibs { get; }
}

public class GenericProvider : ISwitchDiscoveryProvider
{
    public Manufacturer Manufacturer => Manufacturer.OtherGeneric;
    public bool UsesPerVlanFdb => false;
    public bool HasCiscoVlanMibs => false;
}

public class CiscoProvider : ISwitchDiscoveryProvider
{
    public Manufacturer Manufacturer => Manufacturer.Cisco;
    public bool UsesPerVlanFdb => true;
    public bool HasCiscoVlanMibs => true;
}

public static class ProviderRegistry
{
    private static readonly GenericProvider Generic = new();
    private static readonly CiscoProvider Cisco = new();
    /// <summary>Selected vendor wins; AutoDetect uses sysObjectID detection; everything else (and unknown) is Generic.</summary>
    public static ISwitchDiscoveryProvider For(Manufacturer effective) => effective == Manufacturer.Cisco ? Cisco : Generic;
}
