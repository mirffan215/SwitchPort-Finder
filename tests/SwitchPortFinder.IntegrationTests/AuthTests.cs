using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Optional local-accounts mode. (Default mode stays "no login", covered by every other test class.)</summary>
public class AuthTests(AuthApiFixture fx) : IClassFixture<AuthApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static int _n;
    private HttpClient Anon() => fx.CreateClient();                      // each client has its own cookie jar
    private static string Uniq(string p) => $"{p}{Interlocked.Increment(ref _n)}";

    private async Task<HttpClient> LoginAs(string user, string pw)
    {
        var c = fx.CreateClient(); var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, pw), Json);
        r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync()); return c;
    }
    private Task<HttpClient> Admin() => LoginAs("admin", AuthApiFixture.AdminPassword);

    private async Task<(string Name, string Pw, int Id)> NewUser(HttpClient admin, UserRole role, string pw = "Viewer-Passphrase-2026")
    {
        var name = Uniq("user"); var r = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(name, pw, role), Json);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        return (name, pw, (await r.Content.ReadFromJsonAsync<UserDto>(Json))!.Id);
    }

    /// <summary>A raw copy of the session cookie, sent by a client that has no cookie jar: what an attacker who stole the cookie would do.</summary>
    private async Task<HttpClient> Stolen(string user, string pw, Func<HttpClient, Task>? beforeReplay = null)
    {
        var c = fx.CreateClient(); var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, pw), Json);
        var cookie = r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("spf.session")).Split(';')[0];
        var thief = fx.CreateClient(); thief.DefaultRequestHeaders.Add("Cookie", cookie);
        (await thief.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK, "the copied cookie works while the session is live");
        if (beforeReplay is not null) await beforeReplay(c);
        return thief;
    }

    [Fact] public async Task A_logged_out_cookie_cannot_be_replayed()
    {
        var admin = await Admin(); var u = await NewUser(admin, UserRole.Viewer);
        var thief = await Stolen(u.Name, u.Pw, c => c.PostAsync("/api/auth/logout", null));
        (await thief.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "logout must revoke the cookie server-side, not just delete it in the browser");
    }

    [Fact] public async Task Changing_the_password_revokes_other_sessions_but_keeps_this_one()
    {
        var admin = await Admin(); var u = await NewUser(admin, UserRole.Viewer);
        var me = await LoginAs(u.Name, u.Pw);
        var thief = await Stolen(u.Name, u.Pw, async _ => (await me.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(u.Pw, "A-Brand-New-Passphrase-77"), Json)).StatusCode.Should().Be(HttpStatusCode.NoContent));
        (await thief.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "an old/stolen session dies when the password changes");
        (await me.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK, "the browser that changed the password stays signed in");
    }

    [Fact] public async Task An_admin_password_reset_or_role_change_signs_the_user_out()
    {
        var admin = await Admin(); var u = await NewUser(admin, UserRole.Viewer);
        var thief = await Stolen(u.Name, u.Pw, async _ => (await admin.PutAsJsonAsync($"/api/users/{u.Id}", new UpdateUserRequest(null, null, "Reset-By-Admin-Passphrase-55"), Json)).EnsureSuccessStatusCode());
        (await thief.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] public async Task Everything_except_health_login_and_the_ui_shell_requires_a_login()
    {
        var c = Anon();
        foreach (var url in new[] { "/api/switches", "/api/devices", "/api/search?q=abc", "/api/settings", "/api/topology", "/api/dashboard", "/api/users", "/api/export/devices", "/api/credential-profiles", "/api/discovery/jobs", "/api/integrations" })
            (await c.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, url);
        (await c.PostAsync("/api/switches/scan-all", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await c.GetAsync("/api/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetAsync("/")).StatusCode.Should().Be(HttpStatusCode.OK, "the SPA shell must load so it can show the login page");
        (await c.GetAsync("/devices/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        var me = (await c.GetFromJsonAsync<MeDto>("/api/auth/me", Json))!;
        (me.AuthRequired, me.Authenticated).Should().Be((true, false));
    }

    [Fact] public async Task Bad_logins_are_indistinguishable_and_a_good_login_sets_a_hardened_session_cookie()
    {
        var c = Anon();
        var unknown = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("nobody-" + Uniq("x"), "whatever-password-1"), Json);
        var wrong = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("admin", "wrong-password-123456"), Json);
        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized); wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await unknown.Content.ReadAsStringAsync()).Replace("traceId", "").Should().Contain("Invalid username or password.");
        (await wrong.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString().Should().Be((await unknown.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("title").GetString());

        var ok = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("ADMIN", AuthApiFixture.AdminPassword), Json);   // username is case-insensitive
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = ok.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("spf.session")).ToLowerInvariant();
        cookie.Should().Contain("httponly").And.Contain("samesite=strict").And.NotContain("expires=", "a session cookie, not a persistent one");
        (await c.GetFromJsonAsync<MeDto>("/api/auth/me", Json))!.Should().Match<MeDto>(m => m.Authenticated && m.Username == "admin" && m.Role == UserRole.Admin);
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] public async Task Logout_ends_the_session()
    {
        var c = await Admin(); (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] public async Task Viewers_can_read_everything_but_change_nothing()
    {
        var admin = await Admin(); var v = await NewUser(admin, UserRole.Viewer); var viewer = await LoginAs(v.Name, v.Pw);
        (await viewer.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/devices")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/search?q=anything")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/topology")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/export/devices?format=csv")).StatusCode.Should().Be(HttpStatusCode.OK);
        var sw = new SwitchRequest { Name = "X", ManagementIp = "192.0.2.201", Credential = new CredentialInput { Version = SnmpVersion.V2c, Community = "c" } };
        (await viewer.PostAsJsonAsync("/api/switches", sw, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PutAsJsonAsync("/api/switches/1", sw, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.DeleteAsync("/api/switches/1")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsync("/api/switches/scan-all", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await viewer.PostAsJsonAsync("/api/switches/test", new TestConnectionRequest { ManagementIp = "192.0.2.201", Credential = sw.Credential }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var url in new[] { "/api/users", "/api/settings", "/api/credential-profiles", "/api/admin/export/switches", "/api/admin/import/template", "/api/integrations" })
            (await viewer.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden, url + " is admin-only");
        (await viewer.PostAsJsonAsync("/api/users", new CreateUserRequest(Uniq("u"), "Another-Passphrase-1", UserRole.Admin), Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "no privilege escalation");
        // admin can
        (await admin.PostAsJsonAsync("/api/switches", new SwitchRequest { Name = "X2", ManagementIp = "192.0.2.202", Credential = sw.Credential }, Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] public async Task Repeated_failures_lock_the_account_for_a_while_even_for_the_right_password()
    {
        var admin = await Admin(); var u = await NewUser(admin, UserRole.Viewer); var c = Anon();
        for (var i = 0; i < 3; i++) (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(u.Name, "nope-nope-nope-nope"), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(u.Name, u.Pw), Json)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests, "locked: the correct password is refused too");
        await Task.Delay(2300);
        (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(u.Name, u.Pw), Json)).StatusCode.Should().Be(HttpStatusCode.OK, "the lock expires");
    }

    [Fact] public async Task Disabling_deleting_or_demoting_a_user_kills_their_open_session()
    {
        var admin = await Admin();
        var a = await NewUser(admin, UserRole.Viewer); var ca = await LoginAs(a.Name, a.Pw); (await ca.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync($"/api/users/{a.Id}", new UpdateUserRequest(null, true, null), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ca.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "disabled");
        (await Anon().PostAsJsonAsync("/api/auth/login", new LoginRequest(a.Name, a.Pw), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a disabled account cannot sign in");

        var b = await NewUser(admin, UserRole.Admin); var cb = await LoginAs(b.Name, b.Pw); (await cb.GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.OK);
        await admin.PutAsJsonAsync($"/api/users/{b.Id}", new UpdateUserRequest(UserRole.Viewer, null, null), Json);
        (await cb.GetAsync("/api/settings")).StatusCode.Should().BeOneOf([HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden], "a demoted admin must not keep admin rights");

        var d = await NewUser(admin, UserRole.Viewer); var cd = await LoginAs(d.Name, d.Pw);
        (await admin.DeleteAsync($"/api/users/{d.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await cd.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact] public async Task User_management_rules_policy_duplicates_and_the_last_administrator()
    {
        var admin = await Admin();
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(Uniq("p"), "short", UserRole.Viewer), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest("a b", "Valid-Passphrase-2026", UserRole.Viewer), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var u = await NewUser(admin, UserRole.Viewer);
        (await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(u.Name.ToUpperInvariant(), "Valid-Passphrase-2026", UserRole.Viewer), Json)).StatusCode.Should().Be(HttpStatusCode.Conflict, "usernames are case-insensitive");
        var list = (await admin.GetFromJsonAsync<List<UserDto>>("/api/users", Json))!;
        var self = list.Single(x => x.Username == "admin");
        foreach (var upd in new[] { new UpdateUserRequest(UserRole.Viewer, null, null), new UpdateUserRequest(null, true, null) })
            (await admin.PutAsJsonAsync($"/api/users/{self.Id}", upd, Json)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the last active administrator is protected");
        (await admin.DeleteAsync($"/api/users/{self.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        // a second admin makes the first removable
        var second = await NewUser(admin, UserRole.Admin);
        (await admin.PutAsJsonAsync($"/api/users/{second.Id}", new UpdateUserRequest(UserRole.Viewer, null, null), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] public async Task Users_can_change_their_own_password_only_with_the_current_one()
    {
        var admin = await Admin(); var u = await NewUser(admin, UserRole.Viewer); var c = await LoginAs(u.Name, u.Pw);
        (await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest("wrong-current-password", "Brand-New-Passphrase-1"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(u.Pw, "short"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await c.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(u.Pw, "Brand-New-Passphrase-1"), Json)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Anon().PostAsJsonAsync("/api/auth/login", new LoginRequest(u.Name, u.Pw), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old password no longer works");
        (await Anon().PostAsJsonAsync("/api/auth/login", new LoginRequest(u.Name, "Brand-New-Passphrase-1"), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact] public async Task Passwords_and_hashes_are_never_returned_or_stored_in_clear()
    {
        var admin = await Admin(); const string pw = "Leakcheck-Passphrase-9931";
        var u = await NewUser(admin, UserRole.Viewer, pw);
        var users = await admin.GetStringAsync("/api/users"); var me = await admin.GetStringAsync("/api/auth/me");
        users.Should().NotContain(pw).And.NotContain("v1.2").And.NotContainEquivalentOf("hash"); me.Should().NotContain(pw);
        var db = fx.DumpDatabaseText();
        db.Should().NotContain(pw).And.NotContain(AuthApiFixture.AdminPassword, "only salted PBKDF2 hashes are stored");
        db.Should().Contain("v1.210000.", "the stored value is a versioned PBKDF2 hash");
    }

    [Fact] public async Task Login_is_protected_against_cross_site_requests()
    {
        var c = Anon(); var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login") { Content = JsonContent.Create(new LoginRequest("admin", AuthApiFixture.AdminPassword), options: Json) };
        req.Headers.TryAddWithoutValidation("Origin", "http://evil.example");
        (await c.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact] public async Task Authentication_stays_off_by_default_and_login_is_then_a_clear_400()
    {
        using var plain = fx.Variant("Production", ("Authentication:Mode", "None")); var c = plain.CreateClient();
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await c.GetFromJsonAsync<MeDto>("/api/auth/me", Json))!.Should().Match<MeDto>(m => !m.AuthRequired && m.Authenticated && m.Role == UserRole.Admin);
        (await c.PostAsJsonAsync("/api/auth/login", new LoginRequest("a", "b"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
