using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Discovery;

public record JobSummaryDto(
    int Id, int SwitchId, string SwitchName, JobStatus Status, DateTime QueuedAt, DateTime? StartedAt, DateTime? CompletedAt,
    double? DurationSeconds, int PortsFound, int DevicesFound, int NeighborsFound, int ErrorCount, string? Summary);

public record JobErrorDto(string Step, string Code, string Message, DateTime CreatedAt);

public record JobDetailDto(JobSummaryDto Job, IReadOnlyList<StepResult> Steps, IReadOnlyList<JobErrorDto> Errors);

public record PortDeviceDto(int DeviceId, string Hostname, string? Ip, string Mac, int? Vlan, DateTime LastSeen, DeviceState State, bool LowConfidence);

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
    int? PortId, int? IfIndex, string? PortName, int? Vlan, DeviceState State, DateTime FirstSeen, DateTime LastSeen, bool LowConfidence, string? Vendor = null, bool Manual = false, bool Pinned = false, string? Owner = null, string? Notes = null);

public record ManualDeviceRequest(string? Mac, string? Name, string? Ip, string? Owner, string? Notes, int? SwitchId, int? PortId, int? Vlan);

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
    DeviceDto Device, string? IpSourceSwitch, DateTime? IpObservedAt, string LocationNote, IReadOnlyList<SightingDto> Sightings);
