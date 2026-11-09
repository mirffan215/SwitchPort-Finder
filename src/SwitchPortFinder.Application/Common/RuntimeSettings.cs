namespace SwitchPortFinder.Application.Common;

/// <summary>The settings an administrator can change at runtime (stored in the database, applied without a restart). Everything else stays in appsettings.json.</summary>
public class RuntimeSettings
{
    public int SnmpTimeoutMs { get; set; } = 5000;
    public int SnmpRetries { get; set; } = 1;
    public int SnmpMaxRepetitions { get; set; } = 10;
    public int MaxParallelScans { get; set; } = 5;
    public int PerVlanTimeoutMs { get; set; } = 1500;
    public int PerVlanParallelism { get; set; } = 4;
    public int OfflineAfterMinutes { get; set; } = 60;
    public int StaleAfterDays { get; set; } = 7;
    /// <summary>0 = scheduled scans off.</summary>
    public int ScanIntervalMinutes { get; set; }
    public int DnsTimeoutMs { get; set; } = 2000;
    public int HostnameRefreshHours { get; set; } = 24;
    public int UplinkHeuristicMinMacs { get; set; } = 3;
    public double UplinkHeuristicRatio { get; set; } = 0.6;

    public static RuntimeSettings From(SnmpOptions s, DiscoveryOptions d) => new()
    {
        SnmpTimeoutMs = s.TimeoutMs, SnmpRetries = s.Retries, SnmpMaxRepetitions = s.MaxRepetitions, MaxParallelScans = d.MaxParallelScans, PerVlanTimeoutMs = d.PerVlanTimeoutMs,
        PerVlanParallelism = d.PerVlanParallelism, OfflineAfterMinutes = d.OfflineAfterMinutes, StaleAfterDays = d.StaleAfterDays, ScanIntervalMinutes = d.ScanIntervalMinutes,
        DnsTimeoutMs = d.DnsTimeoutMs, HostnameRefreshHours = d.HostnameRefreshHours, UplinkHeuristicMinMacs = d.UplinkHeuristicMinMacs, UplinkHeuristicRatio = d.UplinkHeuristicRatio
    };

    public void ApplyTo(SnmpOptions s, DiscoveryOptions d)
    {
        s.TimeoutMs = SnmpTimeoutMs; s.Retries = SnmpRetries; s.MaxRepetitions = SnmpMaxRepetitions;
        d.MaxParallelScans = MaxParallelScans; d.PerVlanTimeoutMs = PerVlanTimeoutMs; d.PerVlanParallelism = PerVlanParallelism; d.OfflineAfterMinutes = OfflineAfterMinutes;
        d.StaleAfterDays = StaleAfterDays; d.ScanIntervalMinutes = ScanIntervalMinutes; d.DnsTimeoutMs = DnsTimeoutMs; d.HostnameRefreshHours = HostnameRefreshHours;
        d.UplinkHeuristicMinMacs = UplinkHeuristicMinMacs; d.UplinkHeuristicRatio = UplinkHeuristicRatio;
    }

    /// <summary>Range checks so a typo cannot stall discovery (e.g. a 0 ms timeout) or hammer the network (e.g. 500 parallel scans).</summary>
    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        void Range(string name, double v, double min, double max, string unit = "") { if (double.IsNaN(v) || v < min || v > max) e[name] = [$"Must be between {min}{unit} and {max}{unit}."]; }
        Range("snmpTimeoutMs", SnmpTimeoutMs, 500, 60_000, " ms"); Range("snmpRetries", SnmpRetries, 0, 5); Range("snmpMaxRepetitions", SnmpMaxRepetitions, 1, 50);
        Range("maxParallelScans", MaxParallelScans, 1, 50); Range("perVlanTimeoutMs", PerVlanTimeoutMs, 200, 30_000, " ms"); Range("perVlanParallelism", PerVlanParallelism, 1, 16);
        Range("offlineAfterMinutes", OfflineAfterMinutes, 1, 10_080, " min"); Range("staleAfterDays", StaleAfterDays, 1, 365, " days");
        if (ScanIntervalMinutes != 0) Range("scanIntervalMinutes", ScanIntervalMinutes, 5, 10_080, " min");
        Range("dnsTimeoutMs", DnsTimeoutMs, 200, 30_000, " ms"); Range("hostnameRefreshHours", HostnameRefreshHours, 1, 720, " h");
        Range("uplinkHeuristicMinMacs", UplinkHeuristicMinMacs, 2, 100); Range("uplinkHeuristicRatio", UplinkHeuristicRatio, 0.3, 1.0);
        if (!e.ContainsKey("offlineAfterMinutes") && !e.ContainsKey("staleAfterDays") && TimeSpan.FromMinutes(OfflineAfterMinutes) >= TimeSpan.FromDays(StaleAfterDays))
            e["staleAfterDays"] = ["Stale must be later than Offline (devices go Active → Offline → Stale)."];
        return e;
    }
}
