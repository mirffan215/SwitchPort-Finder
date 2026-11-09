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
    public const string Correlation = "Device correlation";
}

public enum StepState { Pending, Running, Ok, Warning, Failed, Skipped }

public record StepResult(string Name, StepState State, string? Message = null);

public record ScanError(string Step, string Code, string Message);

public record SystemInfo(string? Name, string? Descr, string? ObjectId, long UpTimeTicks, string? Model, string? Serial);

public record InterfaceInfo(
    int IfIndex, string Name, string? Descr, string? Alias, bool AdminUp, bool OperUp, long SpeedMbps, string? Mac, int Mtu, int IfType);

public enum FdbKind { Learned, Self, Static, Other, Invalid }

/// <summary>One forwarding-table row. <see cref="IfIndex"/> is already mapped from the bridge port (null when unmappable).</summary>
public record FdbEntry(string Mac, int? Vlan, int BridgePort, int? IfIndex, FdbKind Kind);

public record ArpInfo(string Ip, string Mac, int? IfIndex);

public class ScanResult
{
    public SystemInfo? System { get; set; }
    public Manufacturer? DetectedManufacturer { get; set; }
    public List<InterfaceInfo> Interfaces { get; } = [];
    public Dictionary<int, int> PvidByIfIndex { get; } = [];
    public List<int> Vlans { get; } = [];
    public List<FdbEntry> Fdb { get; } = [];
    public string FdbSource { get; set; } = "";
    public List<ArpInfo> Arp { get; } = [];
    public bool IsLayer3 { get; set; }
    public bool Reachable { get; set; } = true;
    public List<StepResult> Steps { get; } = [];
    public List<ScanError> Errors { get; } = [];
}

public record ScanRequest(string Ip, SnmpCredentials Credentials, Manufacturer Selected, bool UseAsArpSource);

/// <summary>Runs the read-only SNMP collection for one switch. Never throws for a single failed table.</summary>
public interface ISwitchScanner
{
    Task<ScanResult> ScanAsync(ScanRequest request, Func<StepResult, Task> onStep, CancellationToken ct);
}
