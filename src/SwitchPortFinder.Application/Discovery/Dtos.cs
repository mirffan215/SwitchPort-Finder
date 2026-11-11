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
    int? PortId, int? IfIndex, string? PortName, int? Vlan, DeviceState State, DateTime FirstSeen, DateTime LastSeen, bool LowConfidence, string? Vendor = null);

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

public static class DeviceStates
{
    public static DeviceState Compute(DateTime lastSeen, DateTime now, DiscoveryOptions o) =>
        now - lastSeen <= TimeSpan.FromMinutes(o.OfflineAfterMinutes) ? DeviceState.Active
        : now - lastSeen <= TimeSpan.FromDays(o.StaleAfterDays) ? DeviceState.Offline : DeviceState.Stale;
}

public record SightingDto(int SwitchId, string SwitchName, int PortId, int IfIndex, string PortName, bool IsUplink, int MacsOnPort, int? Vlan, DateTime LastSeen);

public record DeviceDetailDto(
    DeviceDto Device, string? IpSourceSwitch, DateTime? IpObservedAt, string LocationNote, IReadOnlyList<SightingDto> Sightings);
