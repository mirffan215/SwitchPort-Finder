using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Application.Discovery;

/// <summary>One health reading of a switch. Null values mean "this switch does not expose it" - never zero.</summary>
public record HealthReading(bool Reachable, long? UptimeSeconds, double? CpuPercent, double? MemoryPercent, int? LatencyMs, string? Error = null, IReadOnlyList<InterfaceInfo>? Interfaces = null);

public record HealthRequest(string Ip, SnmpCredentials Credentials, Domain.Enums.Manufacturer Manufacturer, bool WithInterfaces);

public interface IHealthProbe
{
    /// <summary>Read-only SNMP: uptime + round-trip latency, CPU and memory (vendor MIB with HOST-RESOURCES-MIB fallback) and, optionally, the interface counters.</summary>
    Task<HealthReading> ProbeAsync(HealthRequest request, CancellationToken ct);
}
