namespace SwitchPortFinder.Domain.Entities;

/// <summary>One health data point of a switch (written by every scan and by the background health poll). Reachable=false rows are what downtime is computed from.</summary>
public class SwitchMetricSample
{
    public long Id { get; set; }
    public int SwitchId { get; set; }
    public Switch? Switch { get; set; }
    public DateTime At { get; set; }
    public bool Reachable { get; set; }
    public long? UptimeSeconds { get; set; }
    public double? CpuPct { get; set; }
    public double? MemPct { get; set; }
    public int? LatencyMs { get; set; }
}

/// <summary>Average traffic of one port between two consecutive readings.</summary>
public class PortMetricSample
{
    public long Id { get; set; }
    public int SwitchId { get; set; }
    public int PortId { get; set; }
    public SwitchPort? Port { get; set; }
    public DateTime At { get; set; }
    public double? InBps { get; set; }
    public double? OutBps { get; set; }
    public double? UtilPct { get; set; }
    public double DiscardsPerSec { get; set; }
    public double ErrorsPerSec { get; set; }
}

/// <summary>A saved report: the definition (dataset, fields, grouping, filters, chart) as JSON.</summary>
public class ReportDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Json { get; set; } = "{}";
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
