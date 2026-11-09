using System.Text;

namespace SwitchPortFinder.Application.Import;

/// <summary>Minimal RFC 4180 CSV reader/writer (quoted fields, escaped quotes, embedded newlines).</summary>
public static class Csv
{
    private static readonly System.Text.RegularExpressions.Regex PlainNumber = new(@"^-\d+(\.\d+)?$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static List<List<string>> Parse(string text, char delimiter) =>
        ParseRecords(text, delimiter).Select(r => r.Fields).ToList();

    public sealed record Record(int Line, List<string> Fields);

    /// <summary>Parses records and remembers the 1-based physical line each record starts on (blank lines are skipped but counted).</summary>
    public static List<Record> ParseRecords(string text, char delimiter)
    {
        var rows = new List<Record>();
        var row = new List<string>(); var field = new StringBuilder();
        var inQuotes = false; var anyContent = false;
        var line = 1; var startLine = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch is '\n') line++;
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            if (!anyContent && field.Length == 0) startLine = line;
            if (ch == '"' && field.Length == 0) { inQuotes = true; anyContent = true; }
            else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); anyContent = true; }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                if (anyContent || field.Length > 0) { row.Add(field.ToString()); rows.Add(new Record(startLine, row)); }
                row = []; field.Clear(); anyContent = false; line++;
            }
            else { field.Append(ch); anyContent = true; }
        }
        if (anyContent || field.Length > 0) { row.Add(field.ToString()); rows.Add(new Record(startLine, row)); }
        return rows;
    }

    public static string Escape(string? value)
    {
        value ??= "";
        // Neutralise spreadsheet formula injection: a leading = + @ TAB CR is dangerous, and so is '-' unless the whole value is a plain number.
        if (value.Length > 0 && (value[0] is '=' or '+' or '@' or '\t' or '\r' || (value[0] == '-' && !PlainNumber.IsMatch(value))))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n', ';']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static string Line(IEnumerable<string?> fields) => string.Join(',', fields.Select(Escape));
}
