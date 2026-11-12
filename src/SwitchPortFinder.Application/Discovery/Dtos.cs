using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public record JobSummaryDto(
    int Id, int SwitchId, string SwitchName, JobStatus Status, DateTime QueuedAt, DateTime? StartedAt, DateTime? CompletedAt,
    double? DurationSeconds, int PortsFound, int DevicesFound, int NeighborsFound, int ErrorCount, string? Summary);

public record JobErrorDto(string Step, string Code, string Message, DateTime CreatedAt);

public record JobDetailDto(JobSummaryDto Job, IReadOnlyList<StepResult> Steps, IReadOnlyList<JobErrorDto> Errors);

public record PortDeviceDto(int DeviceId, string Hostname, string? Ip, string Mac, int? Vlan, DateTime LastSeen, DeviceState State, bool LowConfidence, string? Category = null);

public record NeighborDto(
    long NeighborId, NeighborProtocol Protocol, int? ConnectedSwitchId, string? ConnectedSwitchName, string? ConnectedSwitchIp,
    string? RemoteName, string? RemoteChassis, string? RemotePort, int? RemotePortIfIndex, string? RemoteIp, string? Platform,
    NeighborConfidence Confidence, string? Evidence, string Capabilities, bool CanAdd);

public record PortRowDto(
    int PortId, int IfIndex, string Name, string? Alias, string Status, PortType Type, long SpeedMbps, int? Vlan,
    int MacCount, string? PhysAddress, IReadOnlyList<PortDeviceDto> Devices, NeighborDto? Neighbor, IReadOnlyList<string> Supporting,
    PortTrafficDto? Traffic = null);

/// <summary>Average traffic between the last two scans. Utilization is the busier direction as a percentage of link speed.</summary>
public record PortTrafficDto(double? InBps, double? OutBps, double? UtilizationPct, double DiscardsPerSec, double ErrorsPerSec, int IntervalSeconds, DateTime SampledAt, bool Wide);

/// <summary>One row of a "Top N" chart (top utilization / top drops) across all switches.</summary>
public record TopPortDto(int SwitchId, string SwitchName, int PortId, int IfIndex, string PortName, string? Alias, long SpeedMbps, PortTrafficDto Traffic);

/// <summary>A switch's uptime for the dashboard gauges. UptimeSeconds is the estimate for right now (null = unknown or unreachable).</summary>
public record SwitchUptimeDto(int SwitchId, string Name, string ManagementIp, string? Model, bool IsCore, string Status, long? UptimeSeconds, DateTime? SampledAt);

public record SwitchUptimeResponse(IReadOnlyList<SwitchUptimeDto> Items, int SwitchesTotal, int WithData);

public record TopPortsResponse(string Metric, IReadOnlyList<TopPortDto> Items, int PortsWithData, int PortsTotal);

public record NeighborRowDto(int PortId, int IfIndex, string PortName, NeighborDto Neighbor);

public record PortSummary(int Total, int Connected, int Down, int Uplinks, DateTime? LastScan);

public record PortsResponse(PortSummary Summary, IReadOnlyList<PortRowDto> Ports, bool HasArpSource);

public record DeviceDto(
    int Id, string Hostname, HostnameSource HostnameSource, string? Ip, string Mac, int? SwitchId, string? SwitchName,
    int? PortId, int? IfIndex, string? PortName, int? Vlan, DeviceState State, DateTime FirstSeen, DateTime LastSeen, bool LowConfidence, string? Vendor = null, bool Manual = false, bool Pinned = false, string? Owner = null, string? Notes = null, string? Category = null, bool CategoryManual = false, string? CategoryReason = null);

/// <summary>Category: null/empty = let the rules decide, otherwise a DeviceCategory name chosen by the administrator.</summary>
public record ManualDeviceRequest(string? Mac, string? Name, string? Ip, string? Owner, string? Notes, int? SwitchId, int? PortId, int? Vlan, string? Category = null);

public record CategoryRuleDto(int Id, string Name, string Category, bool Enabled, int Priority, string? VendorContains, string? HostnameStartsWith, string? HostnameContains, string? PortDescriptionContains, string? NeighborContains, string? NeighborCapability, int? Vlan);
public record CategoryRuleRequest(string Name, string Category, bool Enabled, int Priority, string? VendorContains, string? HostnameStartsWith, string? HostnameContains, string? PortDescriptionContains, string? NeighborContains, string? NeighborCapability, int? Vlan);

/// <summary>One access point. It comes from the wireless controller's list and/or from the device rules; the two are merged by MAC address.</summary>
public record AccessPointDto(int? DeviceId, string Name, string? Ip, string Mac, string? Vendor, string? Model, int? SwitchId, string? SwitchName, int? PortId, int? IfIndex, string? PortName, int? Vlan,
    bool Online, string Status, DateTime? LastSeen, int DevicesBehind, int? WifiClients, long? UptimeSeconds, string? Controller, string Source, string? Note);
public record AccessPointSummary(int Total, int Online, int Offline, int Controllers, int? WifiClients, IReadOnlyList<KeyValuePair<string, int>> BySwitch, IReadOnlyList<KeyValuePair<string, int>> ByManufacturer);
public record AccessPointsResponse(AccessPointSummary Summary, IReadOnlyList<AccessPointDto> Items);

public record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public class PortListQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Sort { get; set; }
    public bool Desc { get; set; }
    public string? Q { get; set; }
    public int? SwitchId { get; set; }
    /// <summary>"up" or "down" (operational status).</summary>
    public string? Status { get; set; }
    public PortType? Type { get; set; }
}

public record PortListItem(int SwitchId, string SwitchName, int PortId, int IfIndex, string Name, string? Alias, string Status, PortType Type, long SpeedMbps, int? Vlan,
    int MacCount, string? Device, PortTrafficDto? Traffic);

public class DeviceQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string? Sort { get; set; }
    public bool Desc { get; set; }
    public string? Q { get; set; }
    public int? SwitchId { get; set; }
    public DeviceState? State { get; set; }
    public DeviceCategory? Category { get; set; }
    /// <summary>Only devices located on this switch port (the devices behind an access point).</summary>
    public int? PortId { get; set; }
    /// <summary>Leave this device out of the list (the access point itself).</summary>
    public int? ExcludeId { get; set; }
}

/// <summary>
/// A device is judged by the LAST SCAN, not by the clock: Online (Active) if the latest scan of the switch it sits behind found it, Offline if that scan did not,
/// Stale if it has not been found for more than StaleAfterDays of scans. Nothing changes while nobody scans, so scheduled scans can stay off and devices do not
/// "age" into Offline just because time passed.
/// </summary>
public static class DeviceStates
{
    /// <summary>Devices of one scan get their LastSeen within the same moment as the switch's LastScanAt; allow a little slack.</summary>
    public const int ToleranceMinutes = 2;

    /// <summary>The scan time to compare with: the switch it is located on, else the newest scan of any switch (an unlocated device is learned behind uplinks), else itself.</summary>
    public static DateTime Reference(DateTime lastSeen, DateTime? switchLastScan, DateTime? latestScanAnywhere) => switchLastScan ?? latestScanAnywhere ?? lastSeen;

    public static DeviceState Compute(DateTime lastSeen, DateTime reference, DiscoveryOptions o) =>
        reference - lastSeen <= TimeSpan.FromMinutes(ToleranceMinutes) ? DeviceState.Active
        : reference - lastSeen <= TimeSpan.FromDays(o.StaleAfterDays) ? DeviceState.Offline : DeviceState.Stale;
}

public record SightingDto(int SwitchId, string SwitchName, int PortId, int IfIndex, string PortName, bool IsUplink, int MacsOnPort, int? Vlan, DateTime LastSeen);

public record DeviceDetailDto(
    DeviceDto Device, string? IpSourceSwitch, DateTime? IpObservedAt, string LocationNote, IReadOnlyList<SightingDto> Sightings, ExternalInfoDto? External = null, string? Dhcp = null);

/// <summary>Facts from an optional integration (Active Directory, Intune): shown on the device page.</summary>
public record ExternalInfoDto(string Source, string? Os, string? User, string? Detail, DateTime? At);
