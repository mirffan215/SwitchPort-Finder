using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Integrations;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.IntegrationTests;

[Collection("api")]
public class IntegrationsApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static int _n = 100;

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0];

    private async Task<string> Seed(string host = "Unknown Device", HostnameSource src = HostnameSource.None)
    {
        var mac = $"AA77{Interlocked.Increment(ref _n):X8}";
        using var scope = fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Devices.Add(new Device { Mac = mac, Hostname = host, HostnameSource = src, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return mac;
    }
    private async Task<Device> Dev(string mac) { using var scope = fx.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Devices.AsNoTracking().SingleAsync(d => d.Mac == mac); }
    private FakeIntegrationSources Fake => fx.Services.GetRequiredService<FakeIntegrationSources>();
    private static StringContent J(object o) => new(JsonSerializer.Serialize(o), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task Every_integration_is_off_by_default_and_cannot_be_run_until_switched_on()
    {
        var list = (await _http.GetFromJsonAsync<List<IntegrationDto>>("/api/integrations", Json))!;
        list.Select(i => i.Kind).Should().BeEquivalentTo(["dhcp", "ad", "intune"]);
        list.Should().OnlyContain(i => !i.Enabled);
        (await _http.PostAsync("/api/integrations/dhcp/run", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.PostAsync("/api/integrations/nope/run", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Dhcp_names_unknown_devices_but_never_overwrites_a_manual_name()
    {
        var unknown = await Seed(); var manual = await Seed("Reception printer", HostnameSource.Manual);
        Fake.Leases.Clear(); Fake.Leases.Add(new DhcpLease(unknown, "10.77.0.5", "PC-SALES-07.corp.local")); Fake.Leases.Add(new DhcpLease(manual, "10.77.0.6", "dhcp-name"));
        (await _http.PutAsync("/api/integrations/dhcp", J(new { enabled = true, config = new { server = "dhcp01.corp.local" } }))).StatusCode.Should().Be(HttpStatusCode.OK);
        var run = await (await _http.PostAsync("/api/integrations/dhcp/run", null)).Content.ReadFromJsonAsync<IntegrationDto>(Json);
        run!.LastOk.Should().BeTrue();
        var d = await Dev(unknown); (d.Hostname, d.HostnameSource, d.IpAddress).Should().Be(("PC-SALES-07", HostnameSource.Dhcp, "10.77.0.5"));
        (await Dev(manual)).Hostname.Should().Be("Reception printer");

        Fake.Fail = true;
        var bad = await (await _http.PostAsync("/api/integrations/dhcp/run", null)).Content.ReadFromJsonAsync<IntegrationDto>(Json);
        Fake.Fail = false;
        (bad!.LastOk, bad.LastMessage).Should().Be((false, "DHCP server not reachable"));
        await _http.PutAsync("/api/integrations/dhcp", J(new { enabled = false, config = new { server = "dhcp01.corp.local" } }));
    }

    [Fact]
    public async Task Active_directory_adds_os_and_ou_and_the_password_is_stored_encrypted_and_never_returned()
    {
        var mac = await Seed("LAP-FIN-03", HostnameSource.ReverseDns);
        Fake.Computers.Clear(); Fake.Computers.Add(new DirectoryComputer("LAP-FIN-03", "lap-fin-03.corp.local", "Windows 11 Pro", "Finance / Staff"));
        var put = await _http.PutAsync("/api/integrations/ad", J(new { enabled = true, config = new { server = "dc01.corp.local", port = 636, baseDn = "DC=corp,DC=local", bindUser = "svc-portal@corp.local" }, secret = "S3cret-pass!" }));
        var body = await put.Content.ReadAsStringAsync(); put.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().NotContain("S3cret-pass!"); (await _http.GetStringAsync("/api/integrations")).Should().NotContain("S3cret-pass!");
        (await _http.PostAsync("/api/integrations/ad/run", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        Fake.LastSecret.Should().Be("S3cret-pass!");
        var d = await Dev(mac); (d.ExtSource, d.ExtOs, d.ExtDetail).Should().Be(("Active Directory", "Windows 11 Pro", "OU: Finance / Staff"));
        using (var scope = fx.Services.CreateScope()) (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Integrations.SingleAsync(i => i.Kind == "ad")).EncryptedSecret.Should().NotContain("S3cret");
        await _http.PutAsync("/api/integrations/ad", J(new { enabled = false, config = new { server = "dc01.corp.local", baseDn = "DC=corp,DC=local", bindUser = "svc-portal@corp.local" } }));
    }

    [Fact]
    public async Task Intune_matches_by_mac_and_fills_name_user_os_and_compliance()
    {
        var mac = await Seed();
        Fake.Intune.Clear(); Fake.Intune.Add(new IntuneDevice("LAP-HR-01", "ayesha@corp.example", "Windows 11 10.0.22631", "compliant", $"{mac[..2]}:{mac[2..4]}:{mac[4..6]}:{mac[6..8]}:{mac[8..10]}:{mac[10..]}", null, "SN123"));
        var tenant = Guid.NewGuid().ToString(); var client = Guid.NewGuid().ToString();
        (await _http.PutAsync("/api/integrations/intune", J(new { enabled = true, config = new { tenantId = tenant, clientId = client }, secret = "graph-secret" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.PostAsync("/api/integrations/intune/run", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var d = await Dev(mac); (d.Hostname, d.ExtUser, d.ExtSource).Should().Be(("LAP-HR-01", "ayesha@corp.example", "Intune")); d.ExtDetail.Should().Contain("compliant").And.Contain("SN123");
        await _http.PutAsync("/api/integrations/intune", J(new { enabled = false, config = new { tenantId = tenant, clientId = client } }));
    }

    [Theory]
    [InlineData("dhcp", "bad host name!")]
    [InlineData("intune", "not-a-guid")]
    public async Task Turning_an_integration_on_with_invalid_settings_is_refused(string kind, string value)
    {
        var cfg = kind == "dhcp" ? (object)new { server = value } : new { tenantId = value, clientId = value };
        (await _http.PutAsync($"/api/integrations/{kind}", J(new { enabled = true, config = cfg, secret = "x" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Branding_accepts_a_real_image_serves_it_publicly_and_refuses_svg_and_junk()
    {
        using var form = new MultipartFormDataContent(); var f = new ByteArrayContent(Png); f.Headers.ContentType = new MediaTypeHeaderValue("image/png"); form.Add(f, "file", "logo.png");
        (await _http.PostAsync("/api/branding/logo", form)).StatusCode.Should().Be(HttpStatusCode.OK);
        var logo = await _http.GetAsync("/api/branding/logo"); logo.StatusCode.Should().Be(HttpStatusCode.OK); logo.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        logo.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        foreach (var junk in new[] { System.Text.Encoding.UTF8.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"), System.Text.Encoding.UTF8.GetBytes("MZ-not-an-image-at-all"), new byte[600 * 1024] })
        {
            using var bad = new MultipartFormDataContent(); var c = new ByteArrayContent(junk); c.Headers.ContentType = new MediaTypeHeaderValue("image/png"); bad.Add(c, "file", "x.png");
            ((int)(await _http.PostAsync("/api/branding/logo", bad)).StatusCode).Should().BeOneOf(400, 413);
        }
        (await _http.GetFromJsonAsync<BrandingDto>("/api/branding", Json))!.HasLogo.Should().BeTrue();

        (await _http.PutAsync("/api/branding", J(new { companyName = "Contoso Ltd", linkUrl = "https://www.contoso.example/" }))).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var url in new[] { "javascript:alert(1)", "ftp://x.example", "https://user:pw@x.example/", "not a url" })
            (await _http.PutAsync("/api/branding", J(new { companyName = "X", linkUrl = url }))).StatusCode.Should().Be(HttpStatusCode.BadRequest, url);
        var b = (await _http.GetFromJsonAsync<BrandingDto>("/api/branding", Json))!; (b.CompanyName, b.LinkUrl).Should().Be(("Contoso Ltd", "https://www.contoso.example/"));

        (await _http.DeleteAsync("/api/branding/logo")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.GetAsync("/api/branding/logo")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await _http.PutAsync("/api/branding", J(new { companyName = (string?)null, linkUrl = (string?)null }));
    }
}
