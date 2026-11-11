using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Notifications;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Notifications;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>A tiny SMTP server (no TLS, no auth) that records the messages it receives.</summary>
public sealed class FakeSmtp : IDisposable
{
    private readonly TcpListener _l = new(IPAddress.Loopback, 0); private readonly CancellationTokenSource _cts = new();
    public int Port => ((IPEndPoint)_l.LocalEndpoint).Port;
    public List<string> Messages { get; } = [];
    public FakeSmtp() { _l.Start(); _ = Task.Run(Loop); }
    private async Task Loop() { try { while (!_cts.IsCancellationRequested) { var c = await _l.AcceptTcpClientAsync(_cts.Token); _ = Task.Run(() => Session(c)); } } catch { /* stopped */ } }
    private async Task Session(TcpClient c)
    {
        using var _ = c; var s = c.GetStream(); using var r = new StreamReader(s, Encoding.ASCII); using var w = new StreamWriter(s, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };
        await w.WriteLineAsync("220 fake ESMTP ready");
        string? line;
        while ((line = await r.ReadLineAsync()) is not null)
        {
            var cmd = line.ToUpperInvariant();
            if (cmd.StartsWith("EHLO") || cmd.StartsWith("HELO")) await w.WriteLineAsync("250 fake");
            else if (cmd.StartsWith("MAIL") || cmd.StartsWith("RCPT") || cmd.StartsWith("RSET") || cmd.StartsWith("NOOP")) await w.WriteLineAsync("250 ok");
            else if (cmd.StartsWith("DATA"))
            {
                await w.WriteLineAsync("354 go"); var sb = new StringBuilder();
                while ((line = await r.ReadLineAsync()) is not null && line != ".") sb.AppendLine(line);
                lock (Messages) Messages.Add(sb.ToString()); await w.WriteLineAsync("250 queued");
            }
            else if (cmd.StartsWith("QUIT")) { await w.WriteLineAsync("221 bye"); break; }
            else await w.WriteLineAsync("502 no");
        }
    }
    public string[] All() { lock (Messages) return Messages.ToArray(); }
    public void Dispose() { _cts.Cancel(); _l.Stop(); }
}

[Collection("api")]
public class NotificationsApiTests(ApiFixture fx)
{
    private readonly HttpClient _http = fx.CreateClient();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static NotificationConfigRequest Cfg(string host = "smtp.example.com", int port = 587, SmtpSecurity sec = SmtpSecurity.StartTls, string? user = "alerts@example.com", string? pw = "s3cret-mail-pw", bool enabled = true, params string[] to)
        => new(enabled, host, port, sec, user, pw, "spf@example.com", "SwitchPort", to.Length == 0 ? ["ops@example.com"] : to, false, null);

    [Fact]
    public async Task The_mail_password_is_stored_encrypted_never_returned_and_can_be_kept_or_cleared()
    {
        var put = await _http.PutAsJsonAsync("/api/notifications/config", Cfg(), Json); put.StatusCode.Should().Be(HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        (await put.Content.ReadAsStringAsync()).Should().NotContain("s3cret-mail-pw");
        var get = await _http.GetStringAsync("/api/notifications/config"); get.Should().NotContain("s3cret").And.Contain("\"hasPassword\":true");
        using (var scope = fx.Services.CreateScope()) { var enc = scope.ServiceProvider.GetRequiredService<AppDbContext>().NotificationConfigs.Single().EncryptedPassword; enc.Should().NotBeNullOrEmpty().And.NotContain("s3cret-mail-pw"); }
        (await _http.PutAsJsonAsync("/api/notifications/config", Cfg(pw: null), Json)).EnsureSuccessStatusCode();
        (await _http.GetFromJsonAsync<NotificationConfigDto>("/api/notifications/config", Json))!.HasPassword.Should().BeTrue("null keeps the stored password");
        (await _http.PutAsJsonAsync("/api/notifications/config", Cfg(pw: "", user: null), Json)).EnsureSuccessStatusCode();
        (await _http.GetFromJsonAsync<NotificationConfigDto>("/api/notifications/config", Json))!.HasPassword.Should().BeFalse("an empty string clears it");
    }

    [Theory]
    [InlineData("", 587, "ops@example.com", "spf@example.com")] [InlineData("smtp host", 587, "ops@example.com", "spf@example.com")] [InlineData("smtp.example.com", 0, "ops@example.com", "spf@example.com")]
    [InlineData("smtp.example.com", 70000, "ops@example.com", "spf@example.com")] [InlineData("smtp.example.com", 587, "not-an-address", "spf@example.com")] [InlineData("smtp.example.com", 587, "ops@example.com", "nope")]
    public async Task Invalid_mail_settings_are_refused(string host, int port, string to, string from)
    {
        var r = Cfg(host, port, to: to) with { FromAddress = from };
        (await _http.PutAsJsonAsync("/api/notifications/config", r, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Credentials_are_never_allowed_over_an_unencrypted_connection()
    {
        var r = await _http.PutAsJsonAsync("/api/notifications/config", Cfg(sec: SmtpSecurity.None), Json);
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest); (await r.Content.ReadAsStringAsync()).Should().Contain("unencrypted");
        (await _http.PutAsJsonAsync("/api/notifications/config", Cfg(sec: SmtpSecurity.None, user: null, pw: ""), Json)).StatusCode.Should().Be(HttpStatusCode.OK, "an IP-restricted relay without credentials is allowed");
    }

    [Fact]
    public async Task Rules_can_be_created_changed_and_deleted_and_bad_values_are_refused()
    {
        var c = await _http.PostAsJsonAsync("/api/notifications/rules", new AlertRuleRequest("Uplink above 70", AlertRuleType.UplinkUtilization, true, 70, 5, null, 60, true), Json); c.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await c.Content.ReadFromJsonAsync<AlertRuleDto>(Json))!.Id;
        (await _http.PutAsJsonAsync($"/api/notifications/rules/{id}", new AlertRuleRequest("Uplink above 75", AlertRuleType.UplinkUtilization, false, 75, 5, null, 60, false), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _http.GetFromJsonAsync<List<AlertRuleDto>>("/api/notifications/rules", Json))!.Should().Contain(r => r.Id == id && r.Threshold == 75 && !r.Enabled);
        foreach (var bad in new[] { new AlertRuleRequest("", AlertRuleType.HighCpu, true, 90, 5, null, 60, true), new AlertRuleRequest("x", AlertRuleType.HighCpu, true, 150, 5, null, 60, true), new AlertRuleRequest("x", AlertRuleType.HighCpu, true, 90, 5000, null, 60, true),
                                    new AlertRuleRequest("x", AlertRuleType.PacketDrops, true, 0, 5, null, 60, true), new AlertRuleRequest("x", AlertRuleType.HighCpu, true, 90, 5, 99999, 60, true) })
            (await _http.PostAsJsonAsync("/api/notifications/rules", bad, Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await _http.DeleteAsync($"/api/notifications/rules/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_mail_server_address_cannot_point_at_loopback_link_local_or_unspecified_addresses_in_production()
    {
        using var prod = fx.Variant("Production"); var http = prod.CreateClient();   // Development allows loopback for local testing; production does not
        foreach (var addr in new[] { "127.0.0.1", "169.254.169.254", "0.0.0.0", "::1" })
        {
            (await http.PutAsJsonAsync("/api/notifications/config", Cfg(addr, 2525, SmtpSecurity.None, null, "", true), Json)).EnsureSuccessStatusCode();
            var r = (await (await http.PostAsJsonAsync("/api/notifications/test", new TestMailRequest(null), Json)).Content.ReadFromJsonAsync<TestMailResult>(Json))!;
            r.Success.Should().BeFalse(addr); r.Message.Should().Contain("not allowed", addr);
        }
        (await http.PutAsJsonAsync("/api/notifications/config", Cfg("127.0.0.1", 1, SmtpSecurity.None, null, "", true), Json)).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task An_uplink_over_the_limit_opens_one_alert_and_one_e_mail_then_resolves_with_hysteresis()
    {
        using var smtp = new FakeSmtp();
        using var host = fx.Variant("Development", ("Security:AllowLoopbackTargets", "true")); var http = host.CreateClient();
        (await http.PutAsJsonAsync("/api/notifications/config", Cfg("127.0.0.1", smtp.Port, SmtpSecurity.None, null, "", true, "ops@example.com", "noc@example.com"), Json)).EnsureSuccessStatusCode();
        var test = (await (await http.PostAsJsonAsync("/api/notifications/test", new TestMailRequest(null), Json)).Content.ReadFromJsonAsync<TestMailResult>(Json))!;
        test.Success.Should().BeTrue(test.Message); smtp.All().Should().ContainSingle().Which.Should().Contain("Test message");

        int portId;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AlertRules.RemoveRange(db.AlertRules); db.AlertEvents.RemoveRange(db.AlertEvents);
            db.AlertRules.Add(new AlertRule { Name = "Uplink above 80", Type = AlertRuleType.UplinkUtilization, Threshold = 80, DurationMinutes = 10, CooldownMinutes = 0, NotifyOnResolve = true, CreatedAt = DateTime.UtcNow });
            var sw = new Switch { Name = "ALERT-CORE", ManagementIp = "198.18.60.1" }; db.Switches.Add(sw); await db.SaveChangesAsync();
            var p = new SwitchPort { SwitchId = sw.Id, IfIndex = 24, Name = "Gi1/0/24", OperUp = true, PortType = PortType.SwitchUplink, SpeedMbps = 1000 }; db.SwitchPorts.Add(p); await db.SaveChangesAsync(); portId = p.Id;
            var now = DateTime.UtcNow;
            foreach (var m in new[] { 12, 9, 6, 3, 0 }) db.PortMetrics.Add(new PortMetricSample { SwitchId = sw.Id, PortId = p.Id, At = now.AddMinutes(-m), UtilPct = 85 + m / 3.0 });
            await db.SaveChangesAsync();
        }
        async Task<int> Evaluate() { using var s = host.Services.CreateScope(); return await s.ServiceProvider.GetRequiredService<AlertService>().EvaluateAsync(CancellationToken.None); }

        (await Evaluate()).Should().Be(1);
        var alerts = (await http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!; alerts.ActiveCount.Should().Be(1);
        alerts.Active[0].Title.Should().Contain("Uplink utilization").And.Contain("ALERT-CORE Gi1/0/24");
        var mails = smtp.All(); mails.Should().HaveCount(2); mails[1].Should().Contain("[ALERT]").And.Contain("ALERT-CORE");
        (await Evaluate()).Should().Be(0); smtp.All().Should().HaveCount(2, "an alert that is already open does not mail again (cooldown 0 = no reminders)");

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();   // utilization drops to 78 %: below the limit but above 90 % of it -> stays open
            db.PortMetrics.Add(new PortMetricSample { SwitchId = db.SwitchPorts.Single(x => x.Id == portId).SwitchId, PortId = portId, At = DateTime.UtcNow.AddSeconds(30), UtilPct = 78 }); await db.SaveChangesAsync();
        }
        await Evaluate(); (await http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!.ActiveCount.Should().Be(1, "hysteresis: no flapping around the limit");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PortMetrics.Add(new PortMetricSample { SwitchId = db.SwitchPorts.Single(x => x.Id == portId).SwitchId, PortId = portId, At = DateTime.UtcNow.AddMinutes(1), UtilPct = 40 }); await db.SaveChangesAsync();
        }
        await Evaluate();
        var after = (await http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!; after.ActiveCount.Should().Be(0); after.Recent.Should().ContainSingle(x => x.State == AlertState.Resolved);
        smtp.All().Should().HaveCount(3); smtp.All()[2].Should().Contain("[RESOLVED]");
        (await http.GetFromJsonAsync<List<NotificationLogDto>>("/api/notifications/log", Json))!.Should().Contain(l => l.Success && l.Subject.StartsWith("[ALERT]"));
    }

    [Fact]
    public async Task An_unreachable_switch_alerts_after_the_configured_minutes_and_a_failed_mail_is_retried()
    {
        using var smtp = new FakeSmtp(); using var host = fx.Variant("Development", ("Security:AllowLoopbackTargets", "true")); var http = host.CreateClient();
        // the mail server is not reachable yet: the first attempt fails and is logged, the alert stays "not yet notified"
        (await http.PutAsJsonAsync("/api/notifications/config", Cfg("127.0.0.1", 1, SmtpSecurity.None, null, "", true, "ops@example.com"), Json)).EnsureSuccessStatusCode();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AlertRules.RemoveRange(db.AlertRules); db.AlertRules.Add(new AlertRule { Name = "Unreachable", Type = AlertRuleType.SwitchUnreachable, DurationMinutes = 10, CooldownMinutes = 60, NotifyOnResolve = false, CreatedAt = DateTime.UtcNow });
            var sw = new Switch { Name = "DOWN-SW", ManagementIp = "198.18.60.2" }; db.Switches.Add(sw); await db.SaveChangesAsync(); var now = DateTime.UtcNow;
            db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = sw.Id, At = now.AddMinutes(-30), Reachable = true });
            foreach (var m in new[] { 20, 10, 0 }) db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = sw.Id, At = now.AddMinutes(-m), Reachable = false });
            await db.SaveChangesAsync();
        }
        async Task Evaluate() { using var s = host.Services.CreateScope(); await s.ServiceProvider.GetRequiredService<AlertService>().EvaluateAsync(CancellationToken.None); }
        await Evaluate();
        (await http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!.Active.Should().ContainSingle(a => a.Title.Contains("DOWN-SW") && a.NotifyCount == 0);
        (await http.GetFromJsonAsync<List<NotificationLogDto>>("/api/notifications/log", Json))!.Should().Contain(l => !l.Success && l.Error!.Contains("refused", StringComparison.OrdinalIgnoreCase));
        (await http.PutAsJsonAsync("/api/notifications/config", Cfg("127.0.0.1", smtp.Port, SmtpSecurity.None, null, "", true, "ops@example.com"), Json)).EnsureSuccessStatusCode();   // the administrator fixes the port
        await Evaluate();
        smtp.All().Should().ContainSingle().Which.Should().Contain("DOWN-SW");
        (await http.GetFromJsonAsync<AlertsResponse>("/api/alerts", Json))!.Active.Single().NotifyCount.Should().Be(1);
    }
}

public class NotificationsRbacTests(AuthApiFixture fx) : IClassFixture<AuthApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task Only_administrators_can_see_or_change_mail_settings_and_rules_but_every_signed_in_user_can_see_alerts()
    {
        var admin = fx.CreateClient(); (await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", AuthApiFixture.AdminPassword), Json)).EnsureSuccessStatusCode();
        var u = "notif-viewer-" + Guid.NewGuid().ToString("N")[..5];
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(u, "Viewer-Passphrase-2026", UserRole.Viewer), Json)).EnsureSuccessStatusCode();
        var v = fx.CreateClient(); (await v.PostAsJsonAsync("/api/auth/login", new LoginRequest(u, "Viewer-Passphrase-2026"), Json)).EnsureSuccessStatusCode();
        foreach (var url in new[] { "/api/notifications/config", "/api/notifications/rules", "/api/notifications/log" }) (await v.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url);
        (await v.PutAsJsonAsync("/api/notifications/config", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.PostAsJsonAsync("/api/notifications/test", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await v.GetAsync("/api/alerts")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/notifications/config")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fx.CreateClient().GetAsync("/api/alerts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
