using ClosedXML.Excel;
using FluentAssertions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using System.Text;

namespace SwitchPortFinder.UnitTests;

public class CsvTests
{
    [Fact]
    public void Parses_quotes_embedded_commas_newlines_and_escaped_quotes()
    {
        var rows = Csv.Parse("a,b,c\r\n1,\"x, y\",\"say \"\"hi\"\"\"\r\n\"line1\nline2\",,z\r\n", ',');
        rows.Should().HaveCount(3);
        rows[1].Should().Equal("1", "x, y", "say \"hi\"");
        rows[2].Should().Equal("line1\nline2", "", "z");
    }

    [Fact] public void Writer_output_round_trips_through_reader()
    {
        var line = Csv.Line(["a,b", "q\"q", "plain", ""]);
        Csv.Parse(line, ',').Single().Should().Equal("a,b", "q\"q", "plain", "");
    }

    [Fact] public void Spreadsheet_formula_prefixes_are_neutralised_on_export() =>
        Csv.Escape("=cmd|' /C calc'!A0").Should().StartWith("'=");
}

public class ImportFileReaderTests
{
    private static MemoryStream Utf8(string s, bool bom = false) => new((bom ? new UTF8Encoding(true) : new UTF8Encoding(false)).GetPreamble().Concat(Encoding.UTF8.GetBytes(s)).ToArray());

    [Fact]
    public void Reads_csv_with_bom_and_reports_file_row_numbers()
    {
        var rows = ImportFileReader.Read(Utf8("Name,ManagementIP\r\nA,192.0.2.1\r\n\r\nB,192.0.2.2\r\n", bom: true), "x.csv", 100);
        rows.Select(r => r.RowNumber).Should().Equal(2, 4);
        rows[1].Cells["Name"].Should().Be("B");
    }

    [Fact]
    public void Row_numbers_stay_correct_after_multiline_quoted_cells()
    {
        var rows = ImportFileReader.Read(Utf8("Name,ManagementIP,Notes\nA,192.0.2.1,\"two\nlines\"\n\nB,192.0.2.2,\n"), "x.csv", 10);
        rows.Select(r => r.RowNumber).Should().Equal(2, 5);   // header=1, A=2..3 (multi-line cell), blank=4, B=5
    }

    [Fact] public void Header_matching_is_case_insensitive() =>
        ImportFileReader.Read(Utf8("name,managementip\nA,192.0.2.1\n"), "x.csv", 10).Single().Cells["ManagementIP"].Should().Be("192.0.2.1");

    [Fact] public void Semicolon_delimited_files_are_supported() =>
        ImportFileReader.Read(Utf8("Name;ManagementIP\nA;192.0.2.1\n"), "x.csv", 10).Single().Cells["Name"].Should().Be("A");

    [Fact]
    public void Missing_required_column_is_a_clear_error()
    {
        var act = () => ImportFileReader.Read(Utf8("Name,Location\nA,B\n"), "x.csv", 10);
        act.Should().Throw<ValidationFailedException>().Which.Errors["file"][0].Should().Contain("ManagementIP");
    }

    [Fact] public void Rejects_unsupported_extension() =>
        ((Action)(() => ImportFileReader.Read(Utf8("x"), "x.pdf", 10))).Should().Throw<ValidationFailedException>();

    [Fact] public void Enforces_row_limit() =>
        ((Action)(() => ImportFileReader.Read(Utf8("Name,ManagementIP\nA,1.1.1.1\nB,1.1.1.2\nC,1.1.1.3\n"), "x.csv", 2))).Should().Throw<ValidationFailedException>();

    [Fact]
    public void Template_csv_and_xlsx_are_readable_and_match()
    {
        var csv = ImportFileReader.Read(new MemoryStream(ImportTemplateBuilder.Template("csv")), "t.csv", 10);
        var xlsx = ImportFileReader.Read(new MemoryStream(ImportTemplateBuilder.Template("xlsx")), "t.xlsx", 10);
        csv.Should().HaveCount(1); xlsx.Should().HaveCount(1);
        foreach (var col in ImportColumns.All) xlsx[0].Cells[col].Should().Be(csv[0].Cells[col], because: col);
        csv[0].Cells["ManagementIP"].Should().Be("192.0.2.10");   // RFC 5737 documentation range, not a real network
    }

    [Fact]
    public void Reads_a_1500_row_xlsx_efficiently_with_correct_row_numbers()
    {
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet();
        for (var c = 0; c < ImportColumns.All.Length; c++) ws.Cell(1, c + 1).Value = ImportColumns.All[c];
        for (var r = 0; r < 1500; r++) { ws.Cell(r + 2, 1).Value = $"SW-{r}"; ws.Cell(r + 2, 2).Value = $"10.{r / 250}.{r % 250}.1"; ws.Cell(r + 2, 5).Value = "v2c"; ws.Cell(r + 2, 6).Value = "c"; }
        using var ms = new MemoryStream(); wb.SaveAs(ms); ms.Position = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = ImportFileReader.Read(ms, "big.xlsx", 10000);
        var parsed = ImportRowValidator.Validate(rows, new HashSet<string>());
        sw.Stop();
        rows.Should().HaveCount(1500); rows[^1].RowNumber.Should().Be(1501);
        parsed.Should().OnlyContain(p => p.Errors.Count == 0);
        sw.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public void Switch_export_never_contains_credentials()
    {
        var sw = new Switch { Name = "S", ManagementIp = "192.0.2.5", Manufacturer = Manufacturer.Cisco,
            Credential = new SwitchCredential { Version = SnmpVersion.V3, AuthProtocol = SnmpAuthProtocol.Sha1, PrivProtocol = SnmpPrivProtocol.Aes128, EncryptedSecrets = "CfDJ8-ENCRYPTED-BLOB" } };
        var text = Encoding.UTF8.GetString(ImportTemplateBuilder.ExportSwitches([sw], "csv"));
        text.Should().NotContain("CfDJ8").And.Contain("192.0.2.5");
    }
}

public class ImportRowValidatorTests
{
    private static RawImportRow Row(int n, params (string k, string v)[] cells) =>
        new(n, cells.ToDictionary(c => c.k, c => c.v, StringComparer.OrdinalIgnoreCase));
    private static readonly HashSet<string> Profiles = new(["Core-v3", "Access-v2c-RO"], StringComparer.OrdinalIgnoreCase);
    private static ParsedImportRow One(params (string, string)[] c) => ImportRowValidator.Validate([Row(2, c)], Profiles).Single();

    [Fact]
    public void Valid_v2c_row()
    {
        var r = One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("Manufacturer", "Cisco"), ("SnmpVersion", "v2c"), ("Community", "c"));
        r.Errors.Should().BeEmpty(); r.Manufacturer.Should().Be(Manufacturer.Cisco); r.InlineCredentials!.Version.Should().Be(SnmpVersion.V2c);
    }

    [Fact]
    public void Valid_v3_row_with_ipv6()
    {
        var r = One(("Name", "A"), ("ManagementIP", "2001:db8::1"), ("SnmpVersion", "v3"), ("V3Username", "u"), ("V3AuthProtocol", "SHA256"),
            ("V3AuthPassword", "authpass123"), ("V3PrivProtocol", "AES256"), ("V3PrivPassword", "privpass123"));
        r.Errors.Should().BeEmpty();
        r.InlineCredentials!.AuthProtocol.Should().Be(SnmpAuthProtocol.Sha256); r.InlineCredentials.PrivProtocol.Should().Be(SnmpPrivProtocol.Aes256);
    }

    [Theory]
    [InlineData("10")] [InlineData("10.1")] [InlineData("999.1.1.1")] [InlineData("abc")] [InlineData("")]
    public void Invalid_ips_are_rejected(string ip) =>
        One(("Name", "A"), ("ManagementIP", ip), ("SnmpVersion", "v2c"), ("Community", "c")).Errors.Should().Contain(e => e.Contains("ManagementIP"));

    [Fact]
    public void Duplicate_ip_inside_file_is_flagged_on_the_second_row()
    {
        var rows = ImportRowValidator.Validate([
            Row(2, ("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v2c"), ("Community", "c")),
            Row(3, ("Name", "B"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v2c"), ("Community", "c"))], Profiles);
        rows[0].Errors.Should().BeEmpty();
        rows[1].Errors.Should().ContainSingle().Which.Should().Contain("row 2");
    }

    [Fact] public void V2c_without_community_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v2c")).Errors.Should().NotBeEmpty();

    [Fact] public void V3_without_username_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v3")).Errors.Should().Contain(e => e.Contains("username"));

    [Fact] public void V3_auth_without_password_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v3"), ("V3Username", "u"), ("V3AuthProtocol", "SHA")).Errors.Should().Contain(e => e.Contains("Authentication password"));

    [Fact] public void Priv_without_auth_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v3"), ("V3Username", "u"), ("V3PrivProtocol", "AES128"), ("V3PrivPassword", "privpass123")).Errors.Should().NotBeEmpty();

    [Fact]
    public void Unknown_manufacturer_warns_and_falls_back_to_autodetect()
    {
        var r = One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("Manufacturer", "Foobar"), ("SnmpVersion", "v2c"), ("Community", "c"));
        r.Errors.Should().BeEmpty(); r.Manufacturer.Should().Be(Manufacturer.AutoDetect); r.Warnings.Should().ContainSingle(w => w.Contains("Foobar"));
    }

    [Fact]
    public void Credential_profile_is_resolved_case_insensitively_and_replaces_inline_credentials()
    {
        var r = One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("CredentialProfile", "core-v3"));
        r.Errors.Should().BeEmpty(); r.ProfileName.Should().Be("Core-v3"); r.InlineCredentials.Should().BeNull();
        var both = One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("CredentialProfile", "Core-v3"), ("SnmpVersion", "v2c"), ("Community", "c"));
        both.InlineCredentials.Should().BeNull(); both.Warnings.Should().NotBeEmpty();
    }

    [Fact] public void Unknown_profile_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("CredentialProfile", "Nope")).Errors.Should().Contain(e => e.Contains("Nope"));

    [Theory] [InlineData("true", true)] [InlineData("YES", true)] [InlineData("0", false)] [InlineData("no", false)]
    public void Parses_booleans(string text, bool expected) =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v2c"), ("Community", "c"), ("UseAsArpSource", text)).UseAsArpSource.Should().Be(expected);

    [Fact] public void Bad_boolean_is_an_error() =>
        One(("Name", "A"), ("ManagementIP", "192.0.2.1"), ("SnmpVersion", "v2c"), ("Community", "c"), ("UseAsArpSource", "maybe")).Errors.Should().NotBeEmpty();

    [Fact]
    public void Validation_messages_never_echo_secret_values()
    {
        var r = One(("Name", "A"), ("ManagementIP", "bad"), ("SnmpVersion", "v3"), ("V3Username", "u"), ("V3AuthProtocol", "SHA"), ("V3AuthPassword", "short"),
            ("V3PrivProtocol", "AES128"), ("V3PrivPassword", "TOPSECRET1"));
        string.Join(' ', r.Errors.Concat(r.Warnings)).Should().NotContain("short").And.NotContain("TOPSECRET1");
    }
}
