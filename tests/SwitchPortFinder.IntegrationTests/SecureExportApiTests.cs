using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Switch list WITH credentials: only as an encrypted file, admin-only, password re-entered, never in clear text.</summary>
public class SecureExportApiTests(AuthApiFixture fx) : IClassFixture<AuthApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private const string Pass = "export-passphrase-2026";
    private async Task<HttpClient> Login(string user, string pw) { var c = fx.CreateClient(); (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, pw), Json)).EnsureSuccessStatusCode(); return c; }

    [Fact]
    public async Task Export_round_trips_through_the_encrypted_file_and_the_secret_is_never_in_clear()
    {
        var admin = await Login("admin", AuthApiFixture.AdminPassword);
        var name = "SX-" + Guid.NewGuid().ToString("N")[..6];
        (await admin.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = name, ManagementIp = "203.0.113.171", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "TOP-SECRET-community-9" } }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);

        // re-authentication and a decent passphrase are required
        (await admin.PostAsJsonAsync("/api/admin/export/switches/secure", new { password = "wrong-password-xx", passphrase = Pass }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/admin/export/switches/secure", new { password = AuthApiFixture.AdminPassword, passphrase = "short" }, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var r = await admin.PostAsJsonAsync("/api/admin/export/switches/secure", new { password = AuthApiFixture.AdminPassword, passphrase = Pass }, Json);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        r.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".spfenc");
        var file = await r.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(file).Should().NotContain("TOP-SECRET").And.NotContain(name);
        SecureExport.IsContainer(file).Should().BeTrue();
        Encoding.UTF8.GetString(SecureExport.Decrypt(file, Pass)).Should().Contain("TOP-SECRET-community-9").And.Contain(name);

        // the plain exports still contain no secret
        var plain = await admin.GetStringAsync("/api/admin/export/switches?format=csv"); plain.Should().NotContain("TOP-SECRET");

        // import: wrong passphrase is refused, the right one previews the row with its credentials
        async Task<HttpResponseMessage> Preview(string passphrase)
        {
            using var f = new MultipartFormDataContent { { new ByteArrayContent(file), "file", "switches.spfenc" }, { new StringContent(passphrase), "passphrase" } };
            return await admin.PostAsync("/api/admin/import/switches/preview?mode=UpdateExisting", f);
        }
        (await Preview("a-wrong-passphrase")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var ok = await Preview(Pass); ok.StatusCode.Should().Be(HttpStatusCode.OK, await ok.Content.ReadAsStringAsync());
        var preview = (await ok.Content.ReadFromJsonAsync<ImportPreviewDto>(Json))!;
        preview.Rows.Should().Contain(x => x.Name == name && x.SnmpVersion.ToLower() == "v2c" && x.Status != ImportRowStatus.Error);
        (await ok.Content.ReadAsStringAsync()).Should().NotContain("TOP-SECRET", "previews never echo secrets");
    }

    [Fact]
    public async Task Only_administrators_can_export_credentials_and_anonymous_callers_cannot()
    {
        var admin = await Login("admin", AuthApiFixture.AdminPassword);
        var u = "viewer-sx-" + Guid.NewGuid().ToString("N")[..5];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(u, "Viewer-Passphrase-2026", UserRole.Viewer), Json)).EnsureSuccessStatusCode();
        var viewer = await Login(u, "Viewer-Passphrase-2026");
        (await viewer.PostAsJsonAsync("/api/admin/export/switches/secure", new { password = "Viewer-Passphrase-2026", passphrase = Pass }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fx.CreateClient().PostAsJsonAsync("/api/admin/export/switches/secure", new { password = "x", passphrase = Pass }, Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
