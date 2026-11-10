using ClosedXML.Excel;
using SwitchPortFinder.Application.Import;

namespace SwitchPortFinder.Application.Discovery;

/// <summary>CSV / Excel export of the device list (Switch, Port, MAC, IP, Hostname, VLAN, Status, Last Seen, Manufacturer).</summary>
public static class DeviceExportBuilder
{
    public static readonly string[] Columns = ["Switch", "Port", "MAC", "IP", "Hostname", "VLAN", "Status", "Last Seen", "Manufacturer"];

    private static string[] Row(DeviceDto d) =>
    [
        d.SwitchName ?? "", d.PortName ?? "", d.Mac, d.Ip ?? "", d.Hostname, d.Vlan?.ToString() ?? "", d.State.ToString(),
        d.LastSeen.ToString("yyyy-MM-dd HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture), d.Vendor ?? ""
    ];

    public static byte[] Csv(IEnumerable<DeviceDto> rows)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append('﻿').Append(Import.Csv.Line(Columns)).Append("\r\n");
        foreach (var d in rows) sb.Append(Import.Csv.Line(Row(d))).Append("\r\n");
        return new System.Text.UTF8Encoding(false).GetBytes(sb.ToString());
    }

    public static byte[] Xlsx(IEnumerable<DeviceDto> rows)
    {
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Devices");
        for (var c = 0; c < Columns.Length; c++) { var cell = ws.Cell(1, c + 1); cell.Value = Columns[c]; cell.Style.Font.Bold = true; }
        var r = 2;
        foreach (var d in rows)
        {
            var f = Row(d);
            for (var c = 0; c < f.Length; c++) ws.Cell(r, c + 1).SetValue(f[c]);   // always text: no formula evaluation, MACs/IPs stay as written
            r++;
        }
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, r - 1), Columns.Length).SetAutoFilter();
        for (var c = 1; c <= Columns.Length; c++) ws.Column(c).Width = c is 5 ? 28 : 18;   // fixed widths: AdjustToContents is slow on 50k rows
        using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
    }
}
