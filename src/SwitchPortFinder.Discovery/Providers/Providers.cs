using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Discovery.Providers;

/// <summary>A vendor OID for model/serial. <see cref="Scalar"/> = GET this exact OID; otherwise it is a table column and the first non-empty row is used.</summary>
public record VendorOid(string Oid, bool Scalar);

/// <summary>
/// Vendor quirks only. All standards-based discovery (IF-MIB, Q-BRIDGE/BRIDGE-MIB, IP-MIB, LLDP-MIB, ENTITY-MIB) lives in the scanner, so any
/// manufacturer — including an unknown one — still returns ports, MACs, ARP and LLDP via <see cref="GenericProvider"/>.
///
/// Every <see cref="VendorOid"/> below was resolved from the vendor's own MIB file with snmptranslate (JUNIPER-MIB, MIKROTIK-MIB,
/// FORTINET-CORE-MIB, F10-CHASSIS-MIB, DELL-NETWORKING-CHASSIS-MIB, CISCO-STACK-MIB). Vendors with no verified OID are left empty on purpose:
/// no OID is ever guessed. They fall back to ENTITY-MIB (entPhysicalModelName / entPhysicalSerialNum).
/// </summary>
public interface ISwitchDiscoveryProvider
{
    Manufacturer Manufacturer { get; }
    /// <summary>BRIDGE-MIB is per-VLAN (community@vlan / context vlan-N) and must be queried VLAN by VLAN.</summary>
    bool UsesPerVlanFdb { get; }
    /// <summary>Vendor-specific VLAN/port-VLAN OIDs used when Q-BRIDGE has no data.</summary>
    bool HasCiscoVlanMibs { get; }
    /// <summary>CDP is Cisco-proprietary; it is only attempted where it applies and is never required.</summary>
    bool SupportsCdp { get; }
    /// <summary>Used only when ENTITY-MIB gives no model / serial.</summary>
    IReadOnlyList<VendorOid> ModelOids { get; }
    IReadOnlyList<VendorOid> SerialOids { get; }
}

public abstract class ProviderBase(Manufacturer m) : ISwitchDiscoveryProvider
{
    public Manufacturer Manufacturer { get; } = m;
    public virtual bool UsesPerVlanFdb => false;
    public virtual bool HasCiscoVlanMibs => false;
    public virtual bool SupportsCdp => false;
    public virtual IReadOnlyList<VendorOid> ModelOids => [];
    public virtual IReadOnlyList<VendorOid> SerialOids => [];
}

public class GenericProvider() : ProviderBase(Manufacturer.OtherGeneric);

public class CiscoProvider() : ProviderBase(Manufacturer.Cisco)
{
    public override bool UsesPerVlanFdb => true;
    public override bool HasCiscoVlanMibs => true;
    public override bool SupportsCdp => true;
    public override IReadOnlyList<VendorOid> ModelOids => [new("1.3.6.1.4.1.9.5.1.2.16.0", true)];     // CISCO-STACK-MIB::chassisModel
    public override IReadOnlyList<VendorOid> SerialOids => [new("1.3.6.1.4.1.9.5.1.2.19.0", true)];    // CISCO-STACK-MIB::chassisSerialNumberString
}

public class JuniperProvider() : ProviderBase(Manufacturer.Juniper)
{
    public override IReadOnlyList<VendorOid> ModelOids => [new("1.3.6.1.4.1.2636.3.1.2.0", true)];     // JUNIPER-MIB::jnxBoxDescr
    public override IReadOnlyList<VendorOid> SerialOids => [new("1.3.6.1.4.1.2636.3.1.3.0", true)];    // JUNIPER-MIB::jnxBoxSerialNo
}

public class MikroTikProvider() : ProviderBase(Manufacturer.MikroTik)
{
    public override IReadOnlyList<VendorOid> ModelOids => [new("1.3.6.1.4.1.14988.1.1.7.9.0", true)];  // MIKROTIK-MIB::mtxrBoardName
    public override IReadOnlyList<VendorOid> SerialOids => [new("1.3.6.1.4.1.14988.1.1.7.3.0", true)]; // MIKROTIK-MIB::mtxrSerialNumber
}

public class FortinetProvider() : ProviderBase(Manufacturer.Fortinet)
{
    public override IReadOnlyList<VendorOid> SerialOids => [new("1.3.6.1.4.1.12356.100.1.1.1.0", true)]; // FORTINET-CORE-MIB::fnSysSerial
}

public class DellProvider() : ProviderBase(Manufacturer.Dell)
{
    public override IReadOnlyList<VendorOid> SerialOids =>
    [
        new("1.3.6.1.4.1.6027.3.1.1.1.2", false),          // F10-CHASSIS-MIB::chSerialNumber (Force10)
        new("1.3.6.1.4.1.6027.3.26.1.2.3.1.4", false),     // DELL-NETWORKING-CHASSIS-MIB::dellNetChassisSerialNumber
    ];
}

// No vendor-specific OID could be verified for these: they use the generic standards-based path (ENTITY-MIB for model/serial).
public class HpeArubaProvider() : ProviderBase(Manufacturer.HpeAruba);
public class ExtremeProvider() : ProviderBase(Manufacturer.ExtremeNetworks);
public class HuaweiProvider() : ProviderBase(Manufacturer.Huawei);
public class UbiquitiProvider() : ProviderBase(Manufacturer.Ubiquiti);
public class NetgearProvider() : ProviderBase(Manufacturer.Netgear);
public class TpLinkProvider() : ProviderBase(Manufacturer.TpLink);
public class AlliedTelesisProvider() : ProviderBase(Manufacturer.AlliedTelesis);

public static class ProviderRegistry
{
    private static readonly Dictionary<Manufacturer, ISwitchDiscoveryProvider> Providers = new ISwitchDiscoveryProvider[]
    {
        new CiscoProvider(), new HpeArubaProvider(), new DellProvider(), new JuniperProvider(), new ExtremeProvider(), new HuaweiProvider(), new MikroTikProvider(),
        new UbiquitiProvider(), new FortinetProvider(), new NetgearProvider(), new TpLinkProvider(), new AlliedTelesisProvider(), new GenericProvider(),
    }.ToDictionary(p => p.Manufacturer);

    public static IEnumerable<ISwitchDiscoveryProvider> All => Providers.Values;

    /// <summary>Selected vendor wins; AutoDetect uses sysObjectID detection; unknown or unsupported always falls back to Generic.</summary>
    public static ISwitchDiscoveryProvider For(Manufacturer effective) => Providers.TryGetValue(effective, out var p) ? p : Providers[Manufacturer.OtherGeneric];
}
