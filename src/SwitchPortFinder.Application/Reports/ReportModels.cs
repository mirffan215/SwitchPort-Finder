namespace SwitchPortFinder.Application.Reports;

public enum FieldType { Text, Number, DateTime, Bool }

/// <summary>A column a report can use. Measure = a number that makes sense to sum/average/etc.</summary>
public record FieldDef(string Key, string Label, FieldType Type, string? Unit = null, bool Measure = false);

/// <summary>History datasets (time series) honour From/To and the time bucket; the others describe the current state.</summary>
public record DatasetDef(string Key, string Label, string Description, bool History, IReadOnlyList<FieldDef> Fields);

public record ReportFilter(string Field, string Op, string? Value);
public record ReportMeasure(string Field, string Agg);

/// <summary>
/// What to show. Either a plain table (<see cref="Columns"/>) or a summary (<see cref="GroupBy"/> + <see cref="Measures"/>).
/// Every field/operator is checked against the dataset's whitelist: nothing in here is ever turned into SQL.
/// </summary>
public class ReportQuery
{
    public string Dataset { get; set; } = "";
    public List<string>? Columns { get; set; }
    public List<string>? GroupBy { get; set; }
    public List<ReportMeasure>? Measures { get; set; }
    public List<ReportFilter>? Filters { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    /// <summary>hour | day | week | month: how the "at" field is grouped in a summary.</summary>
    public string? TimeBucket { get; set; }
    public string? SortBy { get; set; }
    public bool Desc { get; set; }
    public int? Limit { get; set; }
}

public record ReportColumn(string Key, string Label, FieldType Type, string? Unit);
public record ReportResult(string Dataset, IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows, int TotalRows, bool Truncated, DateTime GeneratedAt);
public record SavedReportDto(int Id, string Name, string Json, string? CreatedBy, DateTime UpdatedAt);
public record SaveReportRequest(string Name, string Json);
