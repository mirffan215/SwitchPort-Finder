using ClosedXML.Excel;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.Application.Import;

/// <summary>Reads CSV or XLSX into raw rows. Files are processed from a stream in memory; nothing is written to disk.</summary>
public static class ImportFileReader
{
    public static List<RawImportRow> Read(Stream stream, string fileName, int maxRows)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var rows = ext switch
        {
            ".csv" or ".txt" => ReadCsv(stream, maxRows),
            ".xlsx" => ReadXlsx(stream, maxRows),
            _ => throw new ValidationFailedException("file", "Unsupported file type. Upload a .csv or .xlsx file.")
        };
        return rows;
    }

    private static void CheckHeader(IEnumerable<string> headers)
    {
        var set = new HashSet<string>(headers.Select(h => h.Trim()), StringComparer.OrdinalIgnoreCase);
        var missing = ImportColumns.Required.Where(c => !set.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new ValidationFailedException("file", $"Missing required column(s): {string.Join(", ", missing)}. Download the template for the expected header row.");
    }

    private static List<RawImportRow> ReadCsv(Stream stream, int maxRows)
    {
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var nl = text.IndexOfAny(['\r', '\n']);
        var firstLine = nl >= 0 ? text[..nl] : text;
        var delimiter = firstLine.Contains(',') ? ',' : firstLine.Contains(';') ? ';' : firstLine.Contains('\t') ? '\t' : ',';
        var table = Csv.ParseRecords(text, delimiter);
        if (table.Count == 0) throw new ValidationFailedException("file", "The file is empty.");
        var headers = table[0].Fields.Select(h => h.Trim()).ToArray();
        CheckHeader(headers);
        var rows = new List<RawImportRow>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var f = table[r].Fields;
            for (var c = 0; c < headers.Length; c++)
                if (headers[c].Length > 0) cells[headers[c]] = c < f.Count ? f[c].Trim() : "";
            if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new RawImportRow(table[r].Line, cells));
            if (rows.Count > maxRows) throw TooMany(maxRows);
        }
        return rows;
    }

    private static List<RawImportRow> ReadXlsx(Stream stream, int maxRows)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(stream); }
        catch (Exception) { throw new ValidationFailedException("file", "The file could not be read as an Excel (.xlsx) workbook."); }
        using (wb)
        {
            var ws = wb.Worksheets.FirstOrDefault() ?? throw new ValidationFailedException("file", "The workbook has no worksheets.");
            var used = ws.RangeUsed();
            if (used is null) throw new ValidationFailedException("file", "The worksheet is empty.");
            var firstRow = used.FirstRow().RowNumber();
            var lastRow = used.LastRow().RowNumber();
            var firstCol = used.FirstColumn().ColumnNumber();
            var lastCol = used.LastColumn().ColumnNumber();
            var headers = Enumerable.Range(firstCol, lastCol - firstCol + 1).Select(c => ws.Cell(firstRow, c).GetString().Trim()).ToArray();
            CheckHeader(headers);
            var rows = new List<RawImportRow>();
            for (var r = firstRow + 1; r <= lastRow; r++)
            {
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var c = 0; c < headers.Length; c++)
                    if (headers[c].Length > 0) cells[headers[c]] = ws.Cell(r, firstCol + c).GetString().Trim();
                if (cells.Values.All(string.IsNullOrWhiteSpace)) continue;
                rows.Add(new RawImportRow(r, cells));
                if (rows.Count > maxRows) throw TooMany(maxRows);
            }
            return rows;
        }
    }

    private static ValidationFailedException TooMany(int max) => new("file", $"The file has more than {max} rows. Split it into smaller files.");
}
