namespace SwitchPortFinder.Application.Common;

public class DiscoveryOptions
{
    public const string Section = "Discovery";
    public int MaxParallelScans { get; set; } = 5;
    /// <summary>Device shows Offline when not seen for longer than this.</summary>
    public int OfflineAfterMinutes { get; set; } = 60;
    /// <summary>Device shows Stale when not seen for longer than this.</summary>
    public int StaleAfterDays { get; set; } = 7;
    /// <summary>0 disables the scheduled scan of all switches.</summary>
    public int ScanIntervalMinutes { get; set; } = 0;
    /// <summary>How often the scheduler checks whether a scan-all is due (seconds). Tests lower this.</summary>
    /// <summary>0 disables the background health poll (uptime, CPU, memory, latency, interface counters).</summary>
    public int HealthPollMinutes { get; set; } = 5;
    public int MetricsRetentionDays { get; set; } = 90;
    public int SchedulePollSeconds { get; set; } = 30;
    public int PerVlanTimeoutMs { get; set; } = 1500;
    public int PerVlanParallelism { get; set; } = 4;
    public int DnsTimeoutMs { get; set; } = 2000;
    public int HostnameRefreshHours { get; set; } = 24;
    /// <summary>FDB heuristic: minimum MACs on a port before it can be inferred as an uplink from overlap evidence.</summary>
    public int UplinkHeuristicMinMacs { get; set; } = 3;
    /// <summary>FDB heuristic: fraction of a port's MACs that must also be learned on edge-like ports of other switches.</summary>
    public double UplinkHeuristicRatio { get; set; } = 0.6;
}
