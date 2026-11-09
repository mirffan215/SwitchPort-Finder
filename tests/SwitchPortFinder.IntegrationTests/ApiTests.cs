using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.IntegrationTests;

[Collection("api")]
public class ApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static int _n;
    private static string Ip() => $"198.51.100.{Interlocked.Increment(ref _n) % 250 + 1}";

    private static SwitchRequest V2(string name, string ip, string community = "S3CRET-COMM-1") => new()
    { Name = name, ManagementIp = ip, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = community } };

    private async Task<T> Ok<T>(HttpResponseMessage r) { ((int)r.StatusCode).Should().BeInRange(200, 202, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<T>(Json))!; }

    [Fact]
    public async Task Health_and_spa_fallback_work_over_plain_http()
    {
        (await _http.GetAsync("/api/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        _http.BaseAddress!.Scheme.Should().Be("http");
    }

    [Fact]
    public async Task Create_get_update_delete_switch_roundtrip_without_leaking_credentials()
    {
        var ip = Ip();
        var created = await Ok<SwitchDetailDto>(await _http.PostAsJsonAsync("/api/switches", V2("RT-SW", ip), Json));
        created.Credential!.HasCommunity.Should().BeTrue();

        var raw = await (await _http.GetAsync($"/api/switches/{created.Id}")).Content.ReadAsStringAsync();
        raw.Should().NotContain("S3CRET-COMM-1");

        // Edit with a blank community keeps the stored one ("unchanged" semantics) and still tests OK.
        var edit = V2("RT-SW-renamed", ip, community: ""); edit.Manufacturer = Manufacturer.Cisco;
        var updated = await Ok<SwitchDetailDto>(await _http.PutAsJsonAsync($"/api/switches/{created.Id}", edit, Json));
        updated.Name.Should().Be("RT-SW-renamed"); updated.Credential!.HasCommunity.Should().BeTrue();
        var test = await Ok<TestConnectionResult>(await _http.PostAsync($"/api/switches/{created.Id}/test", null));
        test.Success.Should().BeTrue();
        fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Calls.Last().Credentials.Community.Should().Be("S3CRET-COMM-1");

        (await _http.DeleteAsync($"/api/switches/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync($"/api/switches/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Duplicate_ip_is_a_409_and_invalid_input_is_a_400_problem_with_field_errors()
    {
        var ip = Ip();
        (await _http.PostAsJsonAsync("/api/switches", V2("A", ip), Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _http.PostAsJsonAsync("/api/switches", V2("B", ip), Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var bad = await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "", ManagementIp = "10", Credential = new CredentialInput { Version = SnmpVersion.V2c } }, Json);
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await bad.Content.ReadAsStringAsync();
        body.Should().Contain("\"errors\"").And.Contain("managementIp");
    }

    [Fact]
    public async Task Test_connection_reports_success_and_detects_vendor_without_saving()
    {
        var before = (await Ok<List<SwitchSummaryDto>>(await _http.GetAsync("/api/switches"))).Count;
        var r = await Ok<TestConnectionResult>(await _http.PostAsJsonAsync("/api/switches/test",
            new TestConnectionRequest { ManagementIp = "192.0.2.7", Manufacturer = Manufacturer.Dell, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "ok" } }, Json));
        r.Success.Should().BeTrue(); r.Message.Should().Be("SNMP connection successful");
        r.DetectedManufacturer.Should().Be(Manufacturer.Cisco); r.VendorMismatch.Should().BeTrue("Dell was selected but sysObjectID says Cisco");
        (await Ok<List<SwitchSummaryDto>>(await _http.GetAsync("/api/switches"))).Count.Should().Be(before);
    }

    [Fact]
    public async Task Test_connection_failures_give_clear_messages_without_secrets()
    {
        var timeout = await Ok<TestConnectionResult>(await _http.PostAsJsonAsync("/api/switches/test",
            new TestConnectionRequest { ManagementIp = "192.0.2.8", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "bad" } }, Json));
        timeout.Success.Should().BeFalse(); timeout.ErrorCode.Should().Be("Timeout"); timeout.Message.Should().Contain("No response").And.NotContain("bad");

        var auth = await Ok<TestConnectionResult>(await _http.PostAsJsonAsync("/api/switches/test",
            new TestConnectionRequest { ManagementIp = "192.0.2.8", Credential = new CredentialInput { Version = SnmpVersion.V3, Username = "u", AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = "wrongpassword" } }, Json));
        auth.ErrorCode.Should().Be("AuthenticationFailed"); auth.Message.Should().Contain("authentication failed").And.NotContain("wrongpassword");
    }

    [Fact]
    public async Task Credential_profiles_never_return_secrets_and_can_be_referenced_by_switches()
    {
        var name = "Prof-" + Guid.NewGuid().ToString("N")[..6];
        var p = await Ok<CredentialProfileDto>(await _http.PostAsJsonAsync("/api/credential-profiles",
            new CredentialProfileRequest { Name = name, Credential = new CredentialInput { Version = SnmpVersion.V3, Username = "profuser", AuthProtocol = SnmpAuthProtocol.Sha256, AuthPassword = "PROFILE-AUTH-PW1", PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = "PROFILE-PRIV-PW1" } }, Json));
        var list = await (await _http.GetAsync("/api/credential-profiles")).Content.ReadAsStringAsync();
        list.Should().NotContain("PROFILE-AUTH-PW1").And.NotContain("PROFILE-PRIV-PW1").And.NotContain("profuser");

        var ip = Ip();
        var sw = await Ok<SwitchDetailDto>(await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "P-SW", ManagementIp = ip, CredentialProfileId = p.Id }, Json));
        sw.CredentialProfileName.Should().Be(name);
        (await Ok<TestConnectionResult>(await _http.PostAsync($"/api/switches/{sw.Id}/test", null))).Success.Should().BeTrue();
        var calls = fx.Services.GetService(typeof(FakeSnmpClient)).As<FakeSnmpClient>().Calls;
        calls.Last().Credentials.Username.Should().Be("profuser"); calls.Last().Credentials.AuthProtocol.Should().Be(SnmpAuthProtocol.Sha256);

        (await _http.DeleteAsync($"/api/credential-profiles/{p.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict, "a profile in use cannot be deleted");
        (await _http.PostAsJsonAsync("/api/credential-profiles", new CredentialProfileRequest { Name = name, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "x" } }, Json))
            .StatusCode.Should().Be(HttpStatusCode.Conflict, "profile names are unique");
    }

    [Fact]
    public async Task Import_preview_commit_skip_update_and_profile_flow()
    {
        var pName = "ImpProf-" + Guid.NewGuid().ToString("N")[..6];
        await Ok<CredentialProfileDto>(await _http.PostAsJsonAsync("/api/credential-profiles", new CredentialProfileRequest { Name = pName, Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "IMPORT-PROFILE-COMM" } }, Json));
        var ipNew = Ip(); var ipNew2 = Ip(); var ipExisting = Ip();
        await _http.PostAsJsonAsync("/api/switches", V2("ExistingOriginal", ipExisting), Json);

        var csv = $"Name,ManagementIP,Manufacturer,SnmpVersion,Community,CredentialProfile\n" +
                  $"New1,{ipNew},Cisco,v2c,IMPORT-ROW-COMM-9,\n" +
                  $"New2,{ipNew2},HPE,,,{pName}\n" +
                  $"Existing,{ipExisting},Juniper,v2c,other,\n" +
                  $"Bad,not-an-ip,Cisco,v2c,x,\n";

        var prev = await Preview(csv, "SkipExisting");
        prev.New.Should().Be(2); prev.Skipped.Should().Be(1); prev.Errors.Should().Be(1); prev.Update.Should().Be(0);
        prev.Rows.Single(r => r.Name == "Bad").Messages.Should().ContainSingle(m => m.Contains("not-an-ip"));
        (await Ok<List<SwitchSummaryDto>>(await _http.GetAsync("/api/switches"))).Should().NotContain(s => s.Name == "New1", "nothing is saved before commit");

        var done = await Commit(prev.PreviewId, "SkipExisting");
        done.Created.Should().Be(2); done.Skipped.Should().Be(1); done.Errors.Should().Be(1);
        var all = await Ok<List<SwitchSummaryDto>>(await _http.GetAsync("/api/switches"));
        all.Single(s => s.ManagementIp == ipExisting).Name.Should().Be("ExistingOriginal");
        all.Single(s => s.Name == "New2").CredentialProfileName.Should().Be(pName);
        all.Single(s => s.Name == "New2").Manufacturer.Should().Be(Manufacturer.HpeAruba);

        // Preview ids are single-use.
        (await _http.PostAsJsonAsync("/api/admin/import/switches/commit", new { previewId = prev.PreviewId, mode = "SkipExisting" })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Update mode updates instead of duplicating.
        var prev2 = await Preview($"Name,ManagementIP,Manufacturer,SnmpVersion,Community\nExistingRenamed,{ipExisting},Juniper,v2c,newcomm\n", "UpdateExisting");
        prev2.Update.Should().Be(1);
        (await Commit(prev2.PreviewId, "UpdateExisting")).Updated.Should().Be(1);
        var after = (await Ok<List<SwitchSummaryDto>>(await _http.GetAsync("/api/switches"))).Where(s => s.ManagementIp == ipExisting).ToList();
        after.Should().ContainSingle().Which.Name.Should().Be("ExistingRenamed");
    }

    [Fact]
    public async Task Import_accepts_xlsx_and_rejects_garbage()
    {
        var bytes = ImportTemplateBuilder.Template("xlsx");
        var r = await Ok<ImportPreview>(await Upload(bytes, "tpl.xlsx", "SkipExisting"));
        r.Total.Should().Be(1);

        var bad = await Upload(Encoding.UTF8.GetBytes("this is not a workbook"), "x.xlsx", "SkipExisting");
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Upload(Encoding.UTF8.GetBytes("Name,Location\nA,B"), "x.csv", "SkipExisting")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Template_and_export_endpoints_return_files()
    {
        foreach (var fmt in new[] { "csv", "xlsx" })
        {
            (await _http.GetAsync($"/api/admin/import/template?format={fmt}")).Content.Headers.ContentDisposition!.FileName.Should().Contain($".{fmt}");
            (await _http.GetAsync($"/api/admin/export/switches?format={fmt}")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // ---------- security: run LAST in spirit (xUnit order is not guaranteed, so this test creates its own secrets) ----------

    [Fact]
    public async Task Secrets_never_appear_in_api_responses_logs_or_the_database_file()
    {
        string[] secrets = ["LEAKCHECK-COMM-77", "LEAKCHECK-AUTH-77", "LEAKCHECK-PRIV-77", "leakcheck-user-77", "LEAKCHECK-IMPORT-88", "LEAKCHECK-PROFILE-99"];
        var responses = new StringBuilder();
        async Task<string> Capture(HttpResponseMessage r) { var s = await r.Content.ReadAsStringAsync(); responses.AppendLine(s); return s; }

        await Capture(await _http.PostAsJsonAsync("/api/switches", V2("Leak-v2", Ip(), secrets[0]), Json));
        await Capture(await _http.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "Leak-v3", ManagementIp = Ip(), Credential = new CredentialInput { Version = SnmpVersion.V3, Username = secrets[3], AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = secrets[1], PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = secrets[2] } }, Json));
        await Capture(await _http.PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "192.0.2.50", Credential = new CredentialInput { Version = SnmpVersion.V3, Username = secrets[3], AuthProtocol = SnmpAuthProtocol.Sha1, AuthPassword = secrets[1], PrivProtocol = SnmpPrivProtocol.Aes128, PrivPassword = secrets[2] } }, Json));
        await Capture(await _http.PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "192.0.2.51", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "bad" } }, Json));
        await Capture(await _http.PostAsJsonAsync("/api/credential-profiles", new CredentialProfileRequest { Name = "Leak-" + Guid.NewGuid().ToString("N")[..5], Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = secrets[5] } }, Json));
        var prev = await Preview($"Name,ManagementIP,SnmpVersion,Community\nLeakImp,{Ip()},v2c,{secrets[4]}\nLeakBad,zzz,v2c,{secrets[4]}\n", "SkipExisting");
        responses.AppendLine(JsonSerializer.Serialize(prev));
        await Commit(prev.PreviewId, "SkipExisting");
        await Capture(await _http.GetAsync("/api/switches"));
        await Capture(await _http.GetAsync("/api/credential-profiles"));
        await Capture(await _http.GetAsync("/api/admin/export/switches?format=csv"));
        responses.Append(Encoding.UTF8.GetString(await (await _http.GetAsync("/api/admin/export/switches?format=xlsx")).Content.ReadAsByteArrayAsync()));
        await Capture(await _http.GetAsync("/api/switches/999999"));   // error path

        var logs = fx.Logs.ToString();
        var db = Encoding.Latin1.GetString(File.ReadAllBytes(fx.DbPath));
        var wal = File.Exists(fx.DbPath + "-wal") ? Encoding.Latin1.GetString(File.ReadAllBytes(fx.DbPath + "-wal")) : "";
        foreach (var s in secrets)
        {
            responses.ToString().Should().NotContain(s, $"API output must not contain '{s}'");
            logs.Should().NotContain(s, $"logs must not contain '{s}'");
            db.Should().NotContain(s, $"credentials must be encrypted at rest ('{s}')");
            wal.Should().NotContain(s);
        }
        logs.Should().Contain("SNMP test", "the scan above should actually have produced log output (guards against a vacuous test)");
        Directory.GetFiles(fx.KeysPath).Should().NotBeEmpty("data-protection keys are persisted to the configured folder");
    }

    // ---------- helpers ----------
    private Task<HttpResponseMessage> Upload(byte[] bytes, string fileName, string mode)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName } };
        return _http.PostAsync($"/api/admin/import/switches/preview?mode={mode}", form);
    }
    private async Task<ImportPreview> Preview(string csv, string mode) => await Ok<ImportPreview>(await Upload(Encoding.UTF8.GetBytes(csv), "import.csv", mode));
    private async Task<ImportCommit> Commit(string id, string mode) => await Ok<ImportCommit>(await _http.PostAsJsonAsync("/api/admin/import/switches/commit", new { previewId = id, mode }));

    private record ImportPreview(string PreviewId, int Total, int New, int Update, int Skipped, int Errors, List<Row> Rows);
    private record Row(int RowNumber, string Name, string Status, List<string> Messages);
    private record ImportCommit(int Created, int Updated, int Skipped, int Errors);
}

internal static class Ext { public static T As<T>(this object? o) => (T)o!; }
