using ClosedXML.Excel;
using SwitchPortFinder.Application.Import;

namespace SwitchPortFinder.Application.Reports;

/// <summary>Excel / CSV for a finished report. Text is always written as text (no formula evaluation); numbers stay numbers so the sheet can be charted or pivoted in Excel.</summary>
public static class ReportExportBuilder
{
    public static byte[] Csv(ReportResult r)
    {
        var sb = new System.Text.StringBuilder().Append('﻿');
        sb.Append(Import.Csv.Line(r.Columns.Select(Header))).Append("\r\n");
        foreach (var row in r.Rows) sb.Append(Import.Csv.Line(row.Select(Cell))).Append("\r\n");
        return new System.Text.UTF8Encoding(false).GetBytes(sb.ToString());
    }

    private static string Header(ReportColumn c) => c.Unit is null ? c.Label : $"{c.Label} ({c.Unit})";
    private static string Cell(object? v) => v switch { null => "", double d => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), _ => v.ToString() ?? "" };

    public static byte[] Xlsx(ReportResult r, string title, string description)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Report");
        ws.Cell(1, 1).SetValue(string.IsNullOrWhiteSpace(title) ? "Report" : title).Style.Font.SetBold().Font.SetFontSize(14);
        ws.Cell(2, 1).SetValue($"Generated {r.GeneratedAt:yyyy-MM-dd HH:mm} UTC · {r.TotalRows} row(s){(r.Truncated ? " (list truncated)" : "")}").Style.Font.SetFontColor(XLColor.Gray);
        const int head = 4;
        for (var c = 0; c < r.Columns.Count; c++)
        {
            var cell = ws.Cell(head, c + 1); cell.SetValue(Header(r.Columns[c]));
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#0E7490"));
        }
        for (var i = 0; i < r.Rows.Count; i++)
            for (var c = 0; c < r.Columns.Count; c++)
            {
                var v = r.Rows[i][c]; var cell = ws.Cell(head + 1 + i, c + 1);
                if (v is double d) { cell.SetValue(d); cell.Style.NumberFormat.Format = "#,##0.##"; } else cell.SetValue(v?.ToString() ?? "");
            }
        if (r.Rows.Count > 0)
        {
            // Power BI-style data bars on the numeric columns of a summary
            for (var c = 0; c < r.Columns.Count; c++)
                if (r.Columns[c].Type == FieldType.Number && r.Rows.Count > 1)
                    ws.Range(head + 1, c + 1, head + r.Rows.Count, c + 1).AddConditionalFormat().DataBar(XLColor.FromHtml("#67E8F9"));
            ws.Range(head, 1, head + r.Rows.Count, r.Columns.Count).SetAutoFilter();
        }
        ws.SheetView.FreezeRows(head);
        for (var c = 1; c <= r.Columns.Count; c++) ws.Column(c).Width = Math.Min(40, Math.Max(12, r.Columns[c - 1].Label.Length + 4));
        var def = wb.AddWorksheet("Definition");
        def.Cell(1, 1).SetValue("What this report shows").Style.Font.SetBold();
        var lines = description.Split('\n'); for (var i = 0; i < lines.Length; i++) def.Cell(2 + i, 1).SetValue(lines[i]);
        def.Column(1).Width = 100;
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
}
