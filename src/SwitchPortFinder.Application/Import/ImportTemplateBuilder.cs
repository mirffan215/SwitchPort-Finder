using ClosedXML.Excel;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Import;

public static class ImportTemplateBuilder
{
    // 192.0.2.0/24 is the RFC 5737 documentation range: a safe example that is not a real network.
    private static readonly string[] Example =
        ["CORE-SW-01", "192.0.2.10", "Cisco", "Catalyst 9300", "v2c", "public-ro", "", "", "", "", "", "", "", "true", "Server room", "Example row - replace or delete"];

    public static byte[] Template(string format) => Build(format, [Example], "Switches");

    public static byte[] ExportSwitches(IEnumerable<Switch> switches, string format)
    {
        // Secrets are never exported: community/username/passwords columns stay empty.
        var rows = switches.OrderBy(s => s.Name).Select(s =>
        {
            var c = s.Credential;
            var version = c is null ? "" : c.Version == SnmpVersion.V3 ? "v3" : "v2c";
            return new[]
            {
                s.Name, s.ManagementIp, VendorDetector.DisplayName(s.Manufacturer), s.Model ?? "", version, "", "",
                c is { Version: SnmpVersion.V3 } ? c.AuthProtocol.ToString() : "", "",
                c is { Version: SnmpVersion.V3 } ? c.PrivProtocol.ToString() : "", "", c?.ContextName ?? "",
                s.CredentialProfile?.Name ?? "", s.UseAsArpSource ? "true" : "false", s.Location ?? "", s.Notes ?? ""
            };
        }).ToList();
        return Build(format, rows, "Switches");
    }

    /// <summary>
    /// The same columns as the import template, WITH the SNMP credentials (a switch that uses a credential profile gets that profile's credentials inline).
    /// The result is plain CSV text: it must only ever be handed out inside <see cref="SecureExport"/>.
    /// </summary>
    public static byte[] ExportSwitchesWithCredentials(IEnumerable<(Switch Sw, SnmpCredentials? Cred)> switches)
    {
        var rows = switches.OrderBy(x => x.Sw.Name).Select(x =>
        {
            var s = x.Sw; var c = x.Cred; var v3 = c is { Version: SnmpVersion.V3 };
            return new[]
            {
                s.Name, s.ManagementIp, VendorDetector.DisplayName(s.Manufacturer), s.Model ?? "", c is null ? "" : v3 ? "v3" : "v2c",
                v3 ? "" : c?.Community ?? "", v3 ? c!.Username ?? "" : "", v3 ? c!.AuthProtocol.ToString() : "", v3 ? c!.AuthPassword ?? "" : "",
                v3 ? c!.PrivProtocol.ToString() : "", v3 ? c!.PrivPassword ?? "" : "", v3 ? c!.ContextName ?? "" : "",
                "", s.UseAsArpSource ? "true" : "false", s.Location ?? "", s.Notes ?? ""
            };
        }).ToList();
        return Build("csv", rows, "Switches");
    }

    private static byte[] Build(string format, IEnumerable<string[]> rows, string sheet)
    {
        if (string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet(sheet);
            for (var c = 0; c < ImportColumns.All.Length; c++)
            {
                var cell = ws.Cell(1, c + 1); cell.Value = ImportColumns.All[c]; cell.Style.Font.Bold = true;
            }
            var r = 2;
            foreach (var row in rows)
            {
                for (var c = 0; c < row.Length; c++) ws.Cell(r, c + 1).SetValue(row[c]);   // text, so IPs stay text
                r++;
            }
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream(); wb.SaveAs(ms); return ms.ToArray();
        }
        var sb = new System.Text.StringBuilder();
        sb.Append('\uFEFF');
        sb.Append(Csv.Line(ImportColumns.All)).Append("\r\n");
        foreach (var row in rows) sb.Append(Csv.Line(row)).Append("\r\n");
        return new System.Text.UTF8Encoding(false).GetBytes(sb.ToString());
    }
}
