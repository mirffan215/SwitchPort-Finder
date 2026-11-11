using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Reports;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Discovery;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Reports;

/// <summary>Runs report queries against the current state and the metric history. Whitelist-driven and bounded (row caps, time-range caps).</summary>
public class ReportEngine(AppDbContext db, SwitchPortFinder.Infrastructure.Discovery.AccessPointService accessPoints)
{
    public const int MaxOutputRows = 5000, MaxHistoryRows = 400_000;
    private static readonly string[] Aggs = ["count", "sum", "avg", "min", "max", "distinct"];
    private static readonly string[] Ops = ["eq", "ne", "contains", "gt", "gte", "lt", "lte", "in"];

    // ------------------------------------------------------------------ dataset catalogue
    private static FieldDef T(string k, string l) => new(k, l, FieldType.Text);
    private static FieldDef N(string k, string l, string? u = null, bool m = true) => new(k, l, FieldType.Number, u, m);
    private static FieldDef D(string k, string l) => new(k, l, FieldType.DateTime);

    public static readonly IReadOnlyList<DatasetDef> Datasets =
    [
        new("switches", "Switches", "One row per switch: status, uptime, CPU, memory, latency, port counts.", false,
            [T("name", "Switch"), T("ip", "Management IP"), T("manufacturer", "Manufacturer"), T("model", "Model"), T("location", "Location"), T("status", "Status"),
             N("uptimeDays", "Uptime", "days"), N("cpuPct", "CPU", "%"), N("memPct", "Memory", "%"), N("latencyMs", "SNMP latency", "ms"),
             N("ports", "Ports"), N("portsUp", "Ports up"), N("uplinks", "Uplinks"), D("lastScan", "Last scan")]),
        new("ports", "Ports", "One row per port with the latest traffic rates.", false,
            [T("switch", "Switch"), T("port", "Port"), T("alias", "Description"), T("status", "Status"), T("type", "Type"), N("speedMbps", "Speed", "Mbps", false), N("vlan", "VLAN", null, false),
             N("inMbps", "Inbound", "Mbit/s"), N("outMbps", "Outbound", "Mbit/s"), N("utilPct", "Utilization", "%"), N("discardsPerSec", "Drops", "pkt/s"), N("errorsPerSec", "Errors", "pkt/s"), N("macs", "MACs learned")]),
        new("devices", "Devices", "One row per discovered or manually added device.", false,
            [T("hostname", "Hostname"), T("ip", "IP"), T("mac", "MAC"), T("vendor", "Manufacturer"), T("switch", "Switch"), T("port", "Port"), N("vlan", "VLAN", null, false), T("state", "State"), T("owner", "Owner"), T("category", "Type"), D("firstSeen", "First seen"), D("lastSeen", "Last seen")]),
        new("access_points", "Access points", "One row per access point: controller, switch and port, status, uptime and the Wi-Fi clients the controller reports.", false,
            [T("name", "Access point"), T("ip", "IP"), T("mac", "MAC"), T("vendor", "Manufacturer"), T("model", "Model"), T("controller", "Controller"), T("switch", "Switch"), T("port", "Port"), N("vlan", "VLAN", null, false), T("status", "Status"), N("wifiClients", "Wi-Fi clients"), N("uptimeDays", "Uptime", "days"), N("devicesBehind", "Devices behind"), D("lastSeen", "Last seen")]),
        new("switch_health", "Switch health history", "CPU, memory, latency and uptime over time (one point per scan or health poll).", true,
            [D("at", "Time"), T("switch", "Switch"), N("up", "Reachable (1/0)", null), N("uptimeDays", "Uptime", "days"), N("cpuPct", "CPU", "%"), N("memPct", "Memory", "%"), N("latencyMs", "SNMP latency", "ms")]),
        new("port_traffic", "Port traffic history", "Per-port traffic, utilization, drops and errors over time.", true,
            [D("at", "Time"), T("switch", "Switch"), T("port", "Port"), T("type", "Port type"), N("inMbps", "Inbound", "Mbit/s"), N("outMbps", "Outbound", "Mbit/s"), N("utilPct", "Utilization", "%"), N("discardsPerSec", "Drops", "pkt/s"), N("errorsPerSec", "Errors", "pkt/s")]),
        new("availability", "Availability and downtime", "Per switch over the chosen period: availability %, downtime, outages, restarts, average/peak CPU and memory.", true,
            [T("switch", "Switch"), N("availabilityPct", "Availability", "%"), N("downtimeMinutes", "Downtime", "min"), N("outages", "Outages"), N("restarts", "Restarts"), N("samples", "Samples", null, false),
             N("avgLatencyMs", "Avg latency", "ms"), N("avgCpuPct", "Avg CPU", "%"), N("maxCpuPct", "Peak CPU", "%"), N("avgMemPct", "Avg memory", "%"), N("maxMemPct", "Peak memory", "%")]),
    ];

    public sealed class Row : Dictionary<string, object?> { }

    // ------------------------------------------------------------------ run
    public async Task<ReportResult> RunAsync(ReportQuery q, CancellationToken ct)
    {
        var ds = Datasets.FirstOrDefault(d => d.Key == q.Dataset) ?? throw new ValidationFailedException("dataset", "Unknown dataset.");
        var fields = ds.Fields.ToDictionary(f => f.Key);
        FieldDef F(string k, string what) => fields.TryGetValue(k, out var f) ? f : throw new ValidationFailedException(what, $"'{k}' is not a field of {ds.Label}.");

        var cols = q.Columns ?? []; var groups = q.GroupBy ?? []; var measures = q.Measures ?? [];
        if (cols.Count > 30 || groups.Count > 3 || measures.Count > 8 || (q.Filters?.Count ?? 0) > 12) throw new ValidationFailedException("query", "Too many fields, groups, measures or filters.");
        foreach (var c in cols) F(c, "columns"); foreach (var g in groups) F(g, "groupBy");
        foreach (var m in measures)
        {
            if (!Aggs.Contains(m.Agg)) throw new ValidationFailedException("measures", $"Unknown aggregation '{m.Agg}'.");
            if (m.Field != "*") { var f = F(m.Field, "measures"); if (f.Type != FieldType.Number && m.Agg is "sum" or "avg") throw new ValidationFailedException("measures", $"'{f.Label}' is not a number."); }
            else if (m.Agg != "count") throw new ValidationFailedException("measures", "Only count can be used without a field.");
        }
        foreach (var f in q.Filters ?? []) { F(f.Field, "filters"); if (!Ops.Contains(f.Op)) throw new ValidationFailedException("filters", $"Unknown operator '{f.Op}'."); }
        if (q.TimeBucket is not null and not ("hour" or "day" or "week" or "month")) throw new ValidationFailedException("timeBucket", "Use hour, day, week or month.");
        var summary = groups.Count > 0 || measures.Count > 0;
        if (!summary && cols.Count == 0) cols = ds.Fields.Select(f => f.Key).ToList();
        if (summary && measures.Count == 0) measures = [new ReportMeasure("*", "count")];

        var from = q.From ?? DateTime.UtcNow.AddDays(-7); var to = q.To ?? DateTime.UtcNow;
        if (ds.History && (to - from).TotalDays > 1100) throw new ValidationFailedException("from", "The time range is too long.");
        var sids = (q.SwitchIds ?? []).Distinct().Take(500).ToList();
        var rows = await LoadAsync(ds.Key, from.ToUniversalTime(), to.ToUniversalTime(), sids, ct);
        if (sids.Count > 0 && !ds.History)   // current-state datasets: filter by the switch name column
        {
            var names = (await db.Switches.AsNoTracking().Where(s => sids.Contains(s.Id)).Select(s => s.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var key = ds.Key == "switches" ? "name" : "switch";
            rows = rows.Where(r => r.GetValueOrDefault(key) is string n && names.Contains(n)).ToList();
        }
        if (rows.Count > MaxHistoryRows) throw new ValidationFailedException("from", "Too much data for this period. Choose a shorter range or filter by switch.");

        // filters
        foreach (var f in q.Filters ?? []) { var def = fields[f.Field]; rows = rows.Where(r => Match(r.GetValueOrDefault(f.Field), def.Type, f.Op, f.Value)).ToList(); }

        // output
        List<ReportColumn> outCols; List<List<object?>> outRows;
        if (!summary)
        {
            outCols = cols.Select(c => ToCol(fields[c])).ToList();
            outRows = rows.Select(r => cols.Select(c => Round(r.GetValueOrDefault(c))).ToList()).ToList();
        }
        else
        {
            var bucket = q.TimeBucket ?? "day";
            object? Key(Row r, string g) => fields[g].Type == FieldType.DateTime && g == "at" ? Bucket((DateTime)r[g]!, bucket) : r.GetValueOrDefault(g);
            outCols = groups.Select(g => ToCol(fields[g]) with { Type = fields[g].Type == FieldType.DateTime && g == "at" ? FieldType.Text : fields[g].Type }).ToList();
            foreach (var m in measures) outCols.Add(new ReportColumn(MeasureKey(m), MeasureLabel(m, m.Field == "*" ? null : fields[m.Field]), FieldType.Number, m.Agg == "count" || m.Agg == "distinct" ? null : m.Field == "*" ? null : fields[m.Field].Unit));
            var grouped = rows.GroupBy(r => string.Join('\u001f', groups.Select(g => Key(r, g)?.ToString() ?? "")));
            outRows = grouped.Select(g =>
            {
                var first = g.First(); var line = new List<object?>();
                foreach (var gk in groups) line.Add(Key(first, gk));
                foreach (var m in measures) line.Add(Round(Aggregate(g, m)));
                return line;
            }).ToList();
        }

        // sort
        var sortIdx = q.SortBy is null ? -1 : outCols.FindIndex(c => c.Key == q.SortBy);
        if (sortIdx < 0 && summary) sortIdx = groups.Count > 0 && groups[0] == "at" ? 0 : groups.Count;   // default: time order, else the first measure
        if (sortIdx >= 0)
        {
            var desc = q.SortBy is null ? !(groups.Count > 0 && groups[0] == "at") : q.Desc;
            Func<List<object?>, object?> k = r => r[sortIdx];
            outRows = (desc ? outRows.OrderByDescending(k, Comparer) : outRows.OrderBy(k, Comparer)).ToList();
        }
        var total = outRows.Count; var limit = Math.Clamp(q.Limit ?? MaxOutputRows, 1, MaxOutputRows);
        return new ReportResult(ds.Key, outCols, outRows.Take(limit).Select(r => (IReadOnlyList<object?>)r).ToList(), total, total > limit, DateTime.UtcNow);
    }

    private static readonly IComparer<object?> Comparer = Comparer<object?>.Create((a, b) =>
    {
        if (a is null) return b is null ? 0 : -1; if (b is null) return 1;
        if (a is IComparable ca && a.GetType() == b.GetType()) return ca.CompareTo(b);
        return string.Compare(Convert.ToString(a, CultureInfo.InvariantCulture), Convert.ToString(b, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    });

    public static string MeasureKey(ReportMeasure m) => m.Field == "*" ? "count" : $"{m.Agg}_{m.Field}";
    private static string MeasureLabel(ReportMeasure m, FieldDef? f) => f is null ? "Count" : m.Agg switch { "count" => $"Count of {f.Label}", "distinct" => $"Distinct {f.Label}", "sum" => $"Total {f.Label}", "avg" => $"Average {f.Label}", "min" => $"Minimum {f.Label}", _ => $"Maximum {f.Label}" };
    private static ReportColumn ToCol(FieldDef f) => new(f.Key, f.Label, f.Type, f.Unit);

    private static object? Round(object? v) => v is double d ? (double.IsNaN(d) || double.IsInfinity(d) ? null : Math.Round(d, 2)) : v is DateTime dt ? dt.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture) : v;

    private static string Bucket(DateTime t, string b) => b switch
    {
        "hour" => t.ToString("yyyy-MM-dd HH':00'", CultureInfo.InvariantCulture),
        "week" => t.Date.AddDays(-(((int)t.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),   // Monday of that week
        "month" => t.ToString("yyyy-MM", CultureInfo.InvariantCulture),
        _ => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    private static object? Aggregate(IEnumerable<Row> g, ReportMeasure m)
    {
        if (m.Field == "*") return (double)g.Count();
        var vals = g.Select(r => r.GetValueOrDefault(m.Field)).Where(v => v is not null).ToList();
        if (m.Agg == "count") return (double)vals.Count;
        if (m.Agg == "distinct") return (double)vals.Select(v => v!.ToString()).Distinct().Count();
        var nums = vals.Select(v => v is IConvertible c ? Convert.ToDouble(c, CultureInfo.InvariantCulture) : double.NaN).Where(x => !double.IsNaN(x)).ToList();
        if (nums.Count == 0) return null;
        return m.Agg switch { "sum" => nums.Sum(), "avg" => nums.Average(), "min" => nums.Min(), _ => nums.Max() };
    }

    private static bool Match(object? v, FieldType type, string op, string? arg)
    {
        arg = arg?.Trim() ?? "";
        if (op == "in") return arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(a => Match(v, type, "eq", a));
        if (v is null) return op == "ne" && arg.Length > 0;
        if (type == FieldType.Number && double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
        {
            var x = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            return op switch { "eq" => Math.Abs(x - n) < 1e-9, "ne" => Math.Abs(x - n) >= 1e-9, "gt" => x > n, "gte" => x >= n, "lt" => x < n, "lte" => x <= n, _ => false };
        }
        if (type == FieldType.DateTime && DateTime.TryParse(arg, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) && v is DateTime dv)
            return op switch { "eq" => dv.Date == d.Date, "ne" => dv.Date != d.Date, "gt" => dv > d, "gte" => dv >= d, "lt" => dv < d, "lte" => dv <= d, _ => false };
        var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        return op switch
        {
            "eq" => s.Equals(arg, StringComparison.OrdinalIgnoreCase), "ne" => !s.Equals(arg, StringComparison.OrdinalIgnoreCase),
            "contains" => s.Contains(arg, StringComparison.OrdinalIgnoreCase),
            "gt" => string.Compare(s, arg, StringComparison.OrdinalIgnoreCase) > 0, "gte" => string.Compare(s, arg, StringComparison.OrdinalIgnoreCase) >= 0,
            "lt" => string.Compare(s, arg, StringComparison.OrdinalIgnoreCase) < 0, "lte" => string.Compare(s, arg, StringComparison.OrdinalIgnoreCase) <= 0, _ => false
        };
    }

    // ------------------------------------------------------------------ data loading
    private async Task<List<Row>> LoadAsync(string dataset, DateTime from, DateTime to, List<int> sids, CancellationToken ct)
    {
        switch (dataset)
        {
            case "switches":
            {
                var sw = await db.Switches.AsNoTracking().ToListAsync(ct);
                var ports = await db.SwitchPorts.AsNoTracking().GroupBy(p => p.SwitchId).Select(g => new { g.Key, Total = g.Count(), Up = g.Count(p => p.OperUp), Uplinks = g.Count(p => p.PortType == PortType.SwitchUplink) }).ToDictionaryAsync(x => x.Key, ct);
                var now = DateTime.UtcNow;
                return sw.Select(s => new Row
                {
                    ["name"] = s.Name, ["ip"] = s.ManagementIp, ["manufacturer"] = s.Manufacturer == Manufacturer.AutoDetect && s.DetectedManufacturer is { } dm ? Application.Snmp.VendorDetector.DisplayName(dm) : Application.Snmp.VendorDetector.DisplayName(s.Manufacturer),
                    ["model"] = s.Model, ["location"] = s.Location, ["status"] = s.Status.ToString(),
                    ["uptimeDays"] = s.Status == SwitchStatus.Reachable && s.UptimeSeconds is { } u && s.UptimeAt is { } ua ? (u + Math.Max(0, (now - ua).TotalSeconds)) / 86400.0 : null,
                    ["cpuPct"] = s.CpuPct, ["memPct"] = s.MemPct, ["latencyMs"] = s.LatencyMs is { } l ? (double)l : null,
                    ["ports"] = ports.TryGetValue(s.Id, out var p) ? (double)p.Total : 0d, ["portsUp"] = p?.Up is { } pu ? (double)pu : 0d, ["uplinks"] = p?.Uplinks is { } pl ? (double)pl : 0d, ["lastScan"] = s.LastScanAt,
                }).ToList();
            }
            case "ports":
            {
                var ps = await db.SwitchPorts.AsNoTracking().Select(p => new { p, Sw = p.Switch!.Name }).ToListAsync(ct);
                var macs = await db.Observations.AsNoTracking().GroupBy(o => o.PortId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
                return ps.Select(x => { var t = PortQueryService.TrafficOf(x.p); return new Row
                {
                    ["switch"] = x.Sw, ["port"] = x.p.Name, ["alias"] = x.p.Alias, ["status"] = x.p.OperUp ? "UP" : "DOWN", ["type"] = x.p.PortType.ToString(), ["speedMbps"] = (double)x.p.SpeedMbps, ["vlan"] = x.p.Vlan is { } v ? (double)v : null,
                    ["inMbps"] = t?.InBps / 1e6, ["outMbps"] = t?.OutBps / 1e6, ["utilPct"] = t?.UtilizationPct, ["discardsPerSec"] = t?.DiscardsPerSec, ["errorsPerSec"] = t?.ErrorsPerSec, ["macs"] = (double)macs.GetValueOrDefault(x.p.Id),
                }; }).ToList();
            }
            case "devices":
            {
                var o = new DiscoveryOptions(); var latest = await db.Switches.AsNoTracking().MaxAsync(s => s.LastScanAt, ct);
                var ds = await db.Devices.AsNoTracking().Select(d => new { d, SwScan = d.Switch != null ? d.Switch.LastScanAt : null, Sw = d.Switch != null ? d.Switch.Name : null, Pt = d.Port != null ? d.Port.Name : null }).ToListAsync(ct);
                return ds.Select(x => new Row
                {
                    ["hostname"] = x.d.Hostname, ["ip"] = x.d.IpAddress, ["mac"] = MacAddress.Format(x.d.Mac), ["vendor"] = VendorText.Of(x.d.Vendor, x.d.Mac), ["switch"] = x.Sw, ["port"] = x.Pt, ["vlan"] = x.d.Vlan is { } v ? (double)v : null,
                    ["state"] = DeviceStates.Compute(x.d.LastSeen, DeviceStates.Reference(x.d.LastSeen, x.SwScan, latest), o).ToString(), ["owner"] = x.d.Owner, ["category"] = x.d.Category == DeviceCategory.Unclassified ? "Unclassified" : x.d.Category.ToString(), ["firstSeen"] = x.d.FirstSeen, ["lastSeen"] = x.d.LastSeen,
                }).ToList();
            }
            case "access_points":
            {
                var aps = (await accessPoints.GetAsync(ct)).Items;
                return aps.Select(a => new Row { ["name"] = a.Name, ["ip"] = a.Ip, ["mac"] = a.Mac, ["vendor"] = a.Vendor, ["model"] = a.Model, ["controller"] = a.Controller, ["switch"] = a.SwitchName, ["port"] = a.PortName, ["vlan"] = a.Vlan is { } v ? (double)v : null,
                    ["status"] = a.Status, ["wifiClients"] = a.WifiClients is { } c ? (double)c : null, ["uptimeDays"] = a.UptimeSeconds is { } u ? u / 86400.0 : null, ["devicesBehind"] = (double)a.DevicesBehind, ["lastSeen"] = a.LastSeen }).ToList();
            }
            case "switch_health":
            {
                var s = await db.SwitchMetrics.AsNoTracking().Where(m => m.At >= from && m.At <= to && (sids.Count == 0 || sids.Contains(m.SwitchId))).Select(m => new { m.At, Sw = m.Switch!.Name, m.Reachable, m.UptimeSeconds, m.CpuPct, m.MemPct, m.LatencyMs }).Take(MaxHistoryRows + 1).ToListAsync(ct);
                return s.Select(m => new Row { ["at"] = m.At, ["switch"] = m.Sw, ["up"] = m.Reachable ? 1d : 0d, ["uptimeDays"] = m.UptimeSeconds is { } u ? u / 86400.0 : null, ["cpuPct"] = m.CpuPct, ["memPct"] = m.MemPct, ["latencyMs"] = m.LatencyMs is { } l ? (double)l : null }).ToList();
            }
            case "port_traffic":
            {
                var s = await db.PortMetrics.AsNoTracking().Where(m => m.At >= from && m.At <= to && (sids.Count == 0 || sids.Contains(m.SwitchId))).Join(db.SwitchPorts, m => m.PortId, p => p.Id, (m, p) => new { m, p.Name, p.PortType, Sw = p.Switch!.Name }).Take(MaxHistoryRows + 1).ToListAsync(ct);
                return s.Select(x => new Row { ["at"] = x.m.At, ["switch"] = x.Sw, ["port"] = x.Name, ["type"] = x.PortType.ToString(), ["inMbps"] = x.m.InBps / 1e6, ["outMbps"] = x.m.OutBps / 1e6, ["utilPct"] = x.m.UtilPct, ["discardsPerSec"] = x.m.DiscardsPerSec, ["errorsPerSec"] = x.m.ErrorsPerSec }).ToList();
            }
            case "availability":
            {
                var s = await db.SwitchMetrics.AsNoTracking().Where(m => m.At >= from && m.At <= to && (sids.Count == 0 || sids.Contains(m.SwitchId))).Select(m => new { m.SwitchId, Sw = m.Switch!.Name, m.At, m.Reachable, m.UptimeSeconds, m.CpuPct, m.MemPct, m.LatencyMs }).Take(MaxHistoryRows + 1).ToListAsync(ct);
                return s.GroupBy(x => x.SwitchId).Select(g => Availability(g.First().Sw, g.OrderBy(x => x.At).Select(x => (x.At, x.Reachable, x.UptimeSeconds, x.CpuPct, x.MemPct, x.LatencyMs)).ToList())).ToList();
            }
        }
        throw new ValidationFailedException("dataset", "Unknown dataset.");
    }

    /// <summary>
    /// Downtime = the time between an unreachable sample and the next sample (at most 1 hour per gap, so a polling pause is not counted as an outage).
    /// An outage is a run of unreachable samples; a restart is a drop of the switch's uptime.
    /// </summary>
    internal static Row Availability(string name, List<(DateTime At, bool Up, long? Uptime, double? Cpu, double? Mem, int? Lat)> s)
    {
        double down = 0; var outages = 0; var restarts = 0; long? lastUp = null;
        for (var i = 0; i < s.Count; i++)
        {
            if (!s[i].Up)
            {
                if (i == 0 || s[i - 1].Up) outages++;
                var next = i + 1 < s.Count ? s[i + 1].At : s[i].At.AddMinutes(5);
                down += Math.Min((next - s[i].At).TotalMinutes, 60);
            }
            else { if (lastUp is { } lu && s[i].Uptime is { } cu && cu + 5 < lu) restarts++; if (s[i].Uptime is { } u) lastUp = u; }
        }
        double? Avg(IEnumerable<double?> v) { var l = v.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Average(); }
        double? Max(IEnumerable<double?> v) { var l = v.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Max(); }
        return new Row
        {
            ["switch"] = name, ["availabilityPct"] = s.Count(x => x.Up) * 100.0 / s.Count, ["downtimeMinutes"] = down, ["outages"] = (double)outages, ["restarts"] = (double)restarts, ["samples"] = (double)s.Count,
            ["avgLatencyMs"] = Avg(s.Select(x => x.Lat is { } l ? (double?)l : null)), ["avgCpuPct"] = Avg(s.Select(x => x.Cpu)), ["maxCpuPct"] = Max(s.Select(x => x.Cpu)), ["avgMemPct"] = Avg(s.Select(x => x.Mem)), ["maxMemPct"] = Max(s.Select(x => x.Mem)),
        };
    }
}
