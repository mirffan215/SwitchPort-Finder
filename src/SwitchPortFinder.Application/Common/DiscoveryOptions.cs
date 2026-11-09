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
    public int PerVlanTimeoutMs { get; set; } = 1500;
    public int PerVlanParallelism { get; set; } = 4;
    public int DnsTimeoutMs { get; set; } = 2000;
    public int HostnameRefreshHours { get; set; } = 24;
}
