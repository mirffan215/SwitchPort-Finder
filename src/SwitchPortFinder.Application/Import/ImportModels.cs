using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Import;

public static class ImportColumns
{
    public static readonly string[] All =
    [
        "Name", "ManagementIP", "Manufacturer", "Model", "SnmpVersion", "Community", "V3Username", "V3AuthProtocol",
        "V3AuthPassword", "V3PrivProtocol", "V3PrivPassword", "V3Context", "CredentialProfile", "UseAsArpSource", "Location", "Notes"
    ];
    public static readonly string[] Required = ["Name", "ManagementIP"];
}

public enum ImportRowStatus { New, Update, Skipped, Error }

/// <summary>A raw row as read from CSV/XLSX: 1-based row number in the file (header = row 1) plus cell text by column.</summary>
public record RawImportRow(int RowNumber, Dictionary<string, string> Cells);

/// <summary>A validated row. Holds secrets in memory only (never serialised to API responses or logs).</summary>
public sealed class ParsedImportRow
{
    public int RowNumber { get; init; }
    public string Name { get; init; } = "";
    public string Ip { get; init; } = "";
    public Manufacturer Manufacturer { get; init; }
    public string? Model { get; init; }
    public string? Location { get; init; }
    public string? Notes { get; init; }
    public bool? UseAsArpSource { get; init; }
    public string? ProfileName { get; init; }
    public SnmpCredentials? InlineCredentials { get; init; }
    public List<string> Warnings { get; } = [];
    public List<string> Errors { get; } = [];
    public override string ToString() => $"Row {RowNumber} {Name}";
}

public record ImportRowResult(
    int RowNumber, string Name, string ManagementIp, string ManufacturerName, string SnmpVersion,
    string? CredentialProfile, ImportRowStatus Status, IReadOnlyList<string> Messages);

public record ImportPreviewDto(
    string PreviewId, string FileName, ImportMode Mode, int Total, int New, int Update, int Skipped, int Errors,
    IReadOnlyList<ImportRowResult> Rows);

public record ImportCommitResult(
    int BatchId, int Total, int Created, int Updated, int Skipped, int Errors, IReadOnlyList<int> SwitchIds);

/// <summary>Server-side holder for a previewed import. Contents never touch disk.</summary>
public sealed record StoredImport(string FileName, IReadOnlyList<ParsedImportRow> Rows);

public interface IImportPreviewStore
{
    string Put(StoredImport import, TimeSpan ttl);
    StoredImport? Take(string id);
}
