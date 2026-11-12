using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public static class Steps
{
    public const string System = "System information";
    public const string Interfaces = "Interfaces";
    public const string Vlans = "VLANs";
    public const string MacTable = "MAC table";
    public const string Arp = "ARP";
    public const string Lldp = "LLDP";
    public const string Cdp = "CDP";
    public const string WirelessAps = "Wireless access points";
    public const string Correlation = "Device correlation";
}

public enum StepState { Pending, Running, Ok, Warning, Failed, Skipped }

public record StepResult(string Name, StepState State, string? Message = null);

/// <summary>A raw LLDP/CDP neighbor as collected from one local port.</summary>
public record NeighborInfo(
    NeighborProtocol Protocol, int LocalIfIndex, string? ChassisId, string? ChassisMac, string? RemotePortId, string? RemotePortDescr,
    string? SysName, string? SysDescr, string? Platform, NeighborCaps Caps, string? MgmtIp)
{
    public string RemoteKey => $"{ChassisId}|{RemotePortId}";
}

public record ScanError(string Step, string Code, string Message);

public record SystemInfo(string? Name, string? Descr, string? ObjectId, long UpTimeTicks, string? Model, string? Serial);

public record InterfaceInfo(
    int IfIndex, string Name, string? Descr, string? Alias, bool AdminUp, bool OperUp, long SpeedMbps, string? Mac, int Mtu, int IfType,
    /// <summary>False when the agent returned the interface without ifOperStatus (a truncated walk): the status is unknown, NOT down.</summary>
    bool OperKnown = true, PortCounters? Counters = null);

/// <summary>Raw IF-MIB counters read in the same walk as the interface table (no extra SNMP traffic). Wide = 64-bit ifHC octet counters.</summary>
public record PortCounters(long InOctets, long OutOctets, long InDiscards, long OutDiscards, long InErrors, long OutErrors, bool Wide);

public enum FdbKind { Learned, Self, Static, Other, Invalid }

/// <summary>One forwarding-table row. <see cref="IfIndex"/> is already mapped from the bridge port (null when unmappable).</summary>
public record FdbEntry(string Mac, int? Vlan, int BridgePort, int? IfIndex, FdbKind Kind);

public record SupportInfo(PortSupport Flags, int VlanCount);

public record ArpInfo(string Ip, string Mac, int? IfIndex);

/// <summary>One access point as listed by a wireless controller.</summary>
public record WirelessApInfo(string SysMac, string? EthMac, string? Name, string? Model, string? Serial, string? Ip, long? UptimeSeconds, int? Clients, bool? AdminEnabled, bool? Associated);

public class ScanResult
{
    public List<WirelessApInfo> WirelessAps { get; } = [];
    /// <summary>The device identified itself as a well-known wireless controller although it was not flagged as one.</summary>
    public bool AutoDetectedController { get; set; }
    public SystemInfo? System { get; set; }
    public HealthReading? Health { get; set; }
    public Manufacturer? DetectedManufacturer { get; set; }
    public List<InterfaceInfo> Interfaces { get; } = [];
    public Dictionary<int, int> PvidByIfIndex { get; } = [];
    public List<int> Vlans { get; } = [];
    /// <summary>Supporting evidence per ifIndex (STP root port, tagged VLAN membership, description hint).</summary>
    public Dictionary<int, SupportInfo> Support { get; } = [];
    public List<FdbEntry> Fdb { get; } = [];
    public string FdbSource { get; set; } = "";
    public List<ArpInfo> Arp { get; } = [];
    public bool IsLayer3 { get; set; }
    public List<NeighborInfo> Neighbors { get; } = [];
    /// <summary>Every MAC this switch owns (all ifPhysAddress values + LLDP local chassis MAC).</summary>
    public HashSet<string> SwitchMacs { get; } = [];
    public bool LldpCollected { get; set; }
    public bool CdpCollected { get; set; }
    public bool Reachable { get; set; } = true;
    public List<StepResult> Steps { get; } = [];
    public List<ScanError> Errors { get; } = [];
}

public record ScanRequest(string Ip, SnmpCredentials Credentials, Manufacturer Selected, bool UseAsArpSource, bool IsWirelessController = false);

/// <summary>Runs the read-only SNMP collection for one switch. Never throws for a single failed table.</summary>
public interface ISwitchScanner
{
    Task<ScanResult> ScanAsync(ScanRequest request, Func<StepResult, Task> onStep, CancellationToken ct);
}
