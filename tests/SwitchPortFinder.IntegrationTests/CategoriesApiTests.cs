using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Notifications;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Discovery;
using SwitchPortFinder.Infrastructure.Notifications;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.IntegrationTests;

[Collection("api")]
public class CategoriesApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private async Task<(int Sw, Dictionary<string, int> Dev)> Seed()
    {
        using var scope = fx.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await scope.ServiceProvider.GetRequiredService<DeviceCategoryService>().EnsureDefaultRulesAsync(CancellationToken.None);
        db.Devices.RemoveRange(db.Devices.Where(d => d.Hostname.Contains("CAT-"))); db.Switches.RemoveRange(db.Switches.Where(s => s.Name == "CAT-SW")); await db.SaveChangesAsync();
        var sw = new Switch { Name = "CAT-SW", ManagementIp = "198.18.80.1", LastScanAt = DateTime.UtcNow }; db.Switches.Add(sw); await db.SaveChangesAsync();
        var ports = new Dictionary<string, SwitchPort>();
        foreach (var (n, i) in new[] { "ap-lldp", "ap-cdp", "phone-sep", "grand", "plain", "vlan50", "ruijie10" }.Select((n, i) => (n, i + 1))) { var p = new SwitchPort { SwitchId = sw.Id, IfIndex = i, Name = $"Gi1/0/{i}", OperUp = true, PortType = PortType.Endpoint, Alias = n }; ports[n] = p; db.SwitchPorts.Add(p); }
        await db.SaveChangesAsync();
        db.Neighbors.Add(new SwitchNeighbor { SwitchId = sw.Id, PortId = ports["ap-lldp"].Id, Protocol = NeighborProtocol.Lldp, RemoteKey = "a", Caps = NeighborCaps.WlanAp | NeighborCaps.Bridge, SysName = "ap-lobby", FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        db.Neighbors.Add(new SwitchNeighbor { SwitchId = sw.Id, PortId = ports["ap-cdp"].Id, Protocol = NeighborProtocol.Cdp, RemoteKey = "b", Platform = "cisco AIR-AP3802I-E-K9", SysName = "ap-floor2", FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
        var now = DateTime.UtcNow; var ids = new Dictionary<string, int>();
        Device D(string name, string mac, string? vendor, string port, int? vlan) => new() { Mac = mac, Hostname = name, HostnameSource = HostnameSource.ReverseDns, Vendor = vendor, FirstSeen = now, LastSeen = now, SwitchId = sw.Id, PortId = ports[port].Id, Vlan = vlan };
        var devs = new[] { ("lldp", D("CAT-lldp", "AA00000000B1", "Cisco Systems, Inc", "ap-lldp", 10)), ("cdp", D("CAT-cdp", "AA00000000B2", "Cisco Systems, Inc", "ap-cdp", 10)), ("phone", D("SEP-CAT-001122334455", "AA00000000B3", "Cisco Systems, Inc", "phone-sep", 50)),
            ("grand", D("CAT-grand", "AA00000000B4", "Grandstream Networks, Inc.", "grand", 10)), ("plain", D("CAT-plain", "AA00000000B5", "Dell Inc.", "plain", 10)), ("vlan50", D("CAT-vlan50", "AA00000000B6", "Cisco Systems, Inc", "vlan50", 50)), ("ruijie10", D("CAT-ruijie10", "AA00000000B7", "Ruijie Networks Co.,LTD", "ruijie10", 10)) };
        foreach (var (_, d) in devs) db.Devices.Add(d);
        await db.SaveChangesAsync(); foreach (var (k, d) in devs) ids[k] = d.Id;
        foreach (var (_, d) in devs) db.Observations.Add(new DeviceObservation { SwitchId = sw.Id, PortId = d.PortId!.Value, Mac = d.Mac, Vlan = d.Vlan, FirstSeen = now, LastSeen = now });   // what a scan would have recorded
        await db.SaveChangesAsync();
        return (sw.Id, ids);
    }

    private async Task<List<string>> Names(string category) => (await _http.GetFromJsonAsync<Paged<DeviceDto>>($"/api/devices?q=CAT-&category={category}&pageSize=50", Json))!.Items.Select(d => d.Hostname).OrderBy(x => x).ToList();

    [Fact]
    public async Task Rules_classify_access_points_and_phones_from_neighbors_vendor_vlan_and_names_and_the_list_filters_by_category()
    {
        var (_, ids) = await Seed();
        (await _http.PostAsync("/api/categories/reapply", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Names("AccessPoint")).Should().Equal("CAT-cdp", "CAT-grand", "CAT-lldp", "CAT-vlan50");
        (await Names("IpPhone")).Should().Equal("SEP-CAT-001122334455");
        var plain = (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{ids["plain"]}", Json))!.Device; plain.Category.Should().BeNull(); 
        var lldp = (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{ids["lldp"]}", Json))!.Device; (lldp.Category, lldp.CategoryManual).Should().Be(("AccessPoint", false)); lldp.CategoryReason.Should().Contain("LLDP");
        (await _http.GetFromJsonAsync<Paged<DeviceDto>>("/api/devices?q=CAT-&category=Bogus", Json).ContinueWith(t => t.IsFaulted)).Should().BeTrue("an unknown category value is refused, not ignored");
        (await _http.GetFromJsonAsync<SearchResponse>("/api/search?q=access%20point", Json))!.Devices.Select(d => d.Hostname).Should().Contain(["CAT-lldp", "CAT-cdp"]);
    }

    [Fact]
    public async Task A_manual_category_wins_over_the_rules_survives_reapply_and_can_be_handed_back()
    {
        var (_, ids) = await Seed(); await _http.PostAsync("/api/categories/reapply", null);
        var r = await _http.PutAsJsonAsync($"/api/devices/{ids["lldp"]}/manual", new ManualDeviceRequest(null, null, null, null, null, null, null, null, "Printer"), Json); r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        (await r.Content.ReadFromJsonAsync<DeviceDetailDto>(Json))!.Device.Should().Match<DeviceDto>(d => d.Category == "Printer" && d.CategoryManual);
        await _http.PostAsync("/api/categories/reapply", null);
        (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{ids["lldp"]}", Json))!.Device.Category.Should().Be("Printer");
        (await _http.PutAsJsonAsync($"/api/devices/{ids["lldp"]}/manual", new ManualDeviceRequest(null, null, null, null, null, null, null, null, "Nonsense"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.PutAsJsonAsync($"/api/devices/{ids["lldp"]}/manual", new ManualDeviceRequest(null, null, null, null, null, null, null, null, null), Json)).EnsureSuccessStatusCode();
        (await _http.GetFromJsonAsync<DeviceDetailDto>($"/api/devices/{ids["lldp"]}", Json))!.Device.Should().Match<DeviceDto>(d => d.Category == "AccessPoint" && !d.CategoryManual);
    }

    [Fact]
    public async Task The_access_point_page_counts_online_and_offline_and_the_MACs_behind_each_port()
    {
        var (sw, ids) = await Seed(); await _http.PostAsync("/api/categories/reapply", null);
        using (var scope = fx.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var d = db.Devices.Single(x => x.Id == ids["cdp"]); d.LastSeen = DateTime.UtcNow.AddHours(-2);           // the latest scan of its switch did not find it
            var portId = d.PortId!.Value; foreach (var m in new[] { "AA00000000C1", "AA00000000C2", "AA00000000C3" }) db.Observations.Add(new DeviceObservation { SwitchId = sw, PortId = portId, Mac = m, FirstSeen = DateTime.UtcNow, LastSeen = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var r = (await _http.GetFromJsonAsync<AccessPointsResponse>("/api/access-points", Json))!;
        var mine = r.Items.Where(i => i.Device.Hostname.StartsWith("CAT-")).ToList();
        mine.Should().HaveCount(4, "the lldp, cdp, grandstream and VLAN 50 devices; the phone stays a phone"); mine.Count(i => i.Device.State == DeviceState.Offline).Should().Be(1);
        mine.Single(i => i.Device.Hostname == "CAT-cdp").ClientsBehind.Should().Be(3, "the AP's own MAC is not a client");
        mine.Single(i => i.Device.Hostname == "CAT-cdp").NeighborInfo.Should().Contain("AIR-AP3802I");
        r.Summary.Total.Should().BeGreaterThanOrEqualTo(4); r.Summary.BySwitch.Should().Contain(k => k.Key == "CAT-SW");
    }

    [Fact]
    public async Task Category_rules_can_be_added_changed_and_deleted_and_changing_them_reclassifies_devices()
    {
        await Seed(); await _http.PostAsync("/api/categories/reapply", null);
        var c = await _http.PostAsJsonAsync("/api/categories/rules", new CategoryRuleRequest("Dell are printers (test)", "Printer", true, 5, "Dell", null, null, null, null, null, null), Json); c.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await c.Content.ReadFromJsonAsync<CategoryRuleDto>(Json))!.Id;
        (await Names("Printer")).Should().Contain("CAT-plain");
        (await _http.PutAsJsonAsync($"/api/categories/rules/{id}", new CategoryRuleRequest("Dell are servers (test)", "Server", true, 5, "Dell", null, null, null, null, null, null), Json)).EnsureSuccessStatusCode();
        (await Names("Server")).Should().Contain("CAT-plain"); (await Names("Printer")).Should().NotContain("CAT-plain");
        (await _http.DeleteAsync($"/api/categories/rules/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Names("Server")).Should().NotContain("CAT-plain");
        foreach (var bad in new[] { new CategoryRuleRequest("", "Printer", true, 5, "x", null, null, null, null, null, null), new CategoryRuleRequest("n", "Printer", true, 5, null, null, null, null, null, null, null), new CategoryRuleRequest("n", "Bogus", true, 5, "x", null, null, null, null, null, null), new CategoryRuleRequest("n", "Printer", true, 0, "x", null, null, null, null, null, null) })
            (await _http.PostAsJsonAsync("/api/categories/rules", bad, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_access_point_the_latest_scan_did_not_find_raises_an_alert_and_it_clears_when_it_is_back()
    {
        var (sw, ids) = await Seed(); await _http.PostAsync("/api/categories/reapply", null);
        using var scope = fx.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AlertRules.RemoveRange(db.AlertRules); db.AlertEvents.RemoveRange(db.AlertEvents);
        db.AlertRules.Add(new AlertRule { Name = "AP offline", Type = AlertRuleType.AccessPointOffline, DurationMinutes = 10, CooldownMinutes = 0, NotifyOnResolve = false, CreatedAt = DateTime.UtcNow });
        db.Devices.Single(d => d.Id == ids["grand"]).LastSeen = DateTime.UtcNow.AddMinutes(-90);   // missed by the last scan 90 minutes ago
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<AlertService>().EvaluateAsync(CancellationToken.None);
        var alerts = (await _http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!; alerts.Active.Should().ContainSingle(a => a.Title.Contains("CAT-grand") && a.Type == AlertRuleType.AccessPointOffline);
        db.Devices.Single(d => d.Id == ids["grand"]).LastSeen = DateTime.UtcNow; await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<AlertService>().EvaluateAsync(CancellationToken.None);
        (await _http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!.Active.Should().BeEmpty();
    }
}

public class CategoriesRbacTests(AuthApiFixture fx) : IClassFixture<AuthApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Only_administrators_can_see_or_change_the_category_rules_but_everyone_signed_in_can_see_the_access_points()
    {
        var admin = fx.CreateClient(); (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", AuthApiFixture.AdminPassword), Json)).EnsureSuccessStatusCode();
        var u = "cat-viewer-" + Guid.NewGuid().ToString("N")[..5]; (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(u, "Viewer-Passphrase-2026", UserRole.Viewer), Json)).EnsureSuccessStatusCode();
        var v = fx.CreateClient(); (await v.PostAsJsonAsync("/api/auth/login", new LoginRequest(u, "Viewer-Passphrase-2026"), Json)).EnsureSuccessStatusCode();
        (await v.GetAsync("/api/categories/rules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PostAsync("/api/categories/reapply", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PutAsJsonAsync("/api/devices/1/manual", new ManualDeviceRequest(null, null, null, null, null, null, null, null, "Printer"), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.GetAsync("/api/access-points")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/categories/rules")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fx.CreateClient().GetAsync("/api/access-points")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
