using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.IntegrationTests;

/// <summary>Authenticator-app (TOTP) two-factor login, enrolment policy for administrators, recovery codes and lockout.</summary>
public class MfaTests(MfaApiFixture fx) : IClassFixture<MfaApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private static int _n; private const string Pw = "Another-Strong-Passphrase-77";
    private static string Uniq(string p) => $"{p}{Interlocked.Increment(ref _n)}";

    private async Task<(HttpResponseMessage R, MeDto? Me)> Login(HttpClient c, string user, string pw)
    { var r = await c.PostAsJsonAsync("/api/auth/login", new LoginRequest(user, pw), Json); return (r, r.IsSuccessStatusCode ? await r.Content.ReadFromJsonAsync<MeDto>(Json) : null); }

    /// <summary>Signs the bootstrap admin in and walks through enrolment; returns the client, the TOTP secret and the recovery codes.</summary>
    private async Task<(HttpClient C, byte[] Secret, List<string> Codes, string User)> EnrolledAdmin()
    {
        var c = fx.CreateClient();
        var login = await Login(c, "admin", MfaApiFixture.AdminPassword);
        if (login.Me!.MfaRequired) throw new InvalidOperationException("bootstrap admin was already enrolled by another test");
        return await Enrol(c, "admin", MfaApiFixture.AdminPassword);
    }

    private async Task<(HttpClient C, byte[] Secret, List<string> Codes, string User)> Enrol(HttpClient c, string user, string pw)
    {
        var setup = await c.PostAsJsonAsync("/api/auth/mfa/setup", new MfaStartRequest(pw), Json); setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var s = (await setup.Content.ReadFromJsonAsync<MfaSetupDto>(Json))!; var secret = Totp.FromBase32(s.Secret)!;
        var enable = await c.PostAsJsonAsync("/api/auth/mfa/enable", new MfaCodeRequest(Totp.Code(secret, Totp.StepOf(DateTimeOffset.UtcNow))), Json);
        enable.StatusCode.Should().Be(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync());
        return (c, secret, (await enable.Content.ReadFromJsonAsync<MfaEnabledDto>(Json))!.RecoveryCodes.ToList(), user);
    }

    private async Task<HttpClient> NewUser(HttpClient admin, UserRole role, string? name = null)
    {
        name ??= Uniq("mfa-user"); var r = await admin.PostAsJsonAsync("/api/users", new CreateUserRequest(name, Pw, role), Json); r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync());
        var c = fx.CreateClient(); (await Login(c, name, Pw)).R.StatusCode.Should().Be(HttpStatusCode.OK); c.DefaultRequestHeaders.Add("X-Test-User", name); return c;
    }

    private static string Next(byte[] secret) => Totp.Code(secret, Totp.StepOf(DateTimeOffset.UtcNow) + 1);   // the step after enrolment: the enrolment code itself is single-use

    [Fact]
    public async Task An_unenrolled_administrator_can_only_reach_the_enrolment_endpoints_and_then_everything_works()
    {
        var boss = await EnrolledAdminOrLogin();
        var name = Uniq("new-admin"); (await boss.C.PostAsJsonAsync("/api/users", new CreateUserRequest(name, Pw, UserRole.Admin), Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        var c = fx.CreateClient();
        var login = await Login(c, name, Pw);
        login.Me!.Should().Match<MeDto>(m => m.Authenticated && m.MfaSetupRequired && !m.MfaEnabled);
        var blocked = await c.GetAsync("/api/switches");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden); (await blocked.Content.ReadAsStringAsync()).Should().Contain("mfaSetupRequired");
        (await c.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync("/api/switches/scan-all", new { }, Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await c.PostAsJsonAsync("/api/auth/mfa/setup", new MfaStartRequest("wrong-password-123"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "re-entering the password is required to start");
        var ok = await Enrol(c, name, Pw);
        ok.Codes.Should().HaveCount(10);
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK, "enrolment keeps this browser signed in and lifts the restriction");
        (await c.GetFromJsonAsync<MeDto>("/api/auth/me", Json))!.Should().Match<MeDto>(m => m.MfaEnabled && !m.MfaSetupRequired);
    }

    [Fact]
    public async Task A_wrong_confirmation_code_does_not_enable_mfa_and_the_secret_is_never_returned_again()
    {
        var admin = await NewUser((await EnrolledAdminOrLogin()).C, UserRole.Viewer);
        var setup = await admin.PostAsJsonAsync("/api/auth/mfa/setup", new MfaStartRequest(Pw), Json); var s = (await setup.Content.ReadFromJsonAsync<MfaSetupDto>(Json))!;
        s.OtpauthUri.Should().StartWith("otpauth://totp/").And.Contain(s.Secret);
        (await admin.PostAsJsonAsync("/api/auth/mfa/enable", new MfaCodeRequest("000000"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetFromJsonAsync<MeDto>("/api/auth/me", Json))!.MfaEnabled.Should().BeFalse();
        var users = await admin.GetAsync("/api/users"); // viewers cannot list users, so check the secret never appears in /me
        (await admin.GetStringAsync("/api/auth/me")).Should().NotContain(s.Secret);
    }

    [Fact]
    public async Task Login_with_mfa_needs_a_valid_code_and_gives_no_session_until_then()
    {
        var a = await EnrolledAdminOrLogin(); var viewer = await NewUser(a.C, UserRole.Viewer, null);
        var name = viewer.DefaultRequestHeaders.GetValues("X-Test-User").Single(); var v = await Enrol(viewer, name, Pw); await v.C.PostAsync("/api/auth/logout", null);

        var c = fx.CreateClient(); var step1 = await Login(c, name, Pw);
        step1.Me!.Should().Match<MeDto>(m => m.MfaRequired && !m.Authenticated && m.MfaChallenge != null);
        step1.R.Headers.Contains("Set-Cookie").Should().BeFalse("no session cookie before the second factor");
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var ch = step1.Me.MfaChallenge!;
        (await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, "123456"), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest("forged" + ch, Next(v.Secret)), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a tampered challenge is refused");
        var good = await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, Next(v.Secret)), Json);
        good.StatusCode.Should().Be(HttpStatusCode.OK); (await good.Content.ReadFromJsonAsync<MeDto>(Json))!.Authenticated.Should().BeTrue();
        (await c.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.OK);
        // the same code cannot be used twice (replay)
        var c2 = fx.CreateClient(); var again = (await Login(c2, name, Pw)).Me!.MfaChallenge!;
        (await c2.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(again, Next(v.Secret)), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a TOTP code is single-use");
    }

    [Fact]
    public async Task Recovery_codes_work_once_and_the_codes_are_stored_hashed()
    {
        var a = await EnrolledAdminOrLogin(); var viewer = await NewUser(a.C, UserRole.Viewer); var name = viewer.DefaultRequestHeaders.GetValues("X-Test-User").Single();
        var v = await Enrol(viewer, name, Pw); await viewer.PostAsync("/api/auth/logout", null);
        var rc = v.Codes[3];
        var c = fx.CreateClient(); var ch = (await Login(c, name, Pw)).Me!.MfaChallenge!;
        (await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, rc.ToLowerInvariant().Replace("-", "")), Json)).StatusCode.Should().Be(HttpStatusCode.OK, "case and dashes do not matter");
        var c2 = fx.CreateClient(); var ch2 = (await Login(c2, name, Pw)).Me!.MfaChallenge!;
        (await c2.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch2, rc), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a recovery code is single-use");
    }

    [Fact]
    public async Task Wrong_codes_lock_the_account_even_for_the_right_code()
    {
        var a = await EnrolledAdminOrLogin(); var viewer = await NewUser(a.C, UserRole.Viewer); var name = viewer.DefaultRequestHeaders.GetValues("X-Test-User").Single();
        var v = await Enrol(viewer, name, Pw); await viewer.PostAsync("/api/auth/logout", null);
        var c = fx.CreateClient(); var ch = (await Login(c, name, Pw)).Me!.MfaChallenge!;
        for (var i = 0; i < 3; i++) (await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, "000000"), Json)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, Next(v.Secret)), Json)).StatusCode.Should().NotBe(HttpStatusCode.OK, "locked: guessing a 6-digit code must be impossible");
        (await Login(fx.CreateClient(), name, Pw)).R.StatusCode.Should().NotBe(HttpStatusCode.OK, "the password step is locked too");
    }

    [Fact]
    public async Task Administrators_cannot_turn_mfa_off_but_viewers_can_and_an_admin_can_reset_a_lost_device()
    {
        var a = await EnrolledAdminOrLogin();
        (await a.C.PostAsJsonAsync("/api/auth/mfa/disable", new MfaDisableRequest(MfaApiFixture.AdminPassword, Next(a.Secret)), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "policy: administrators must keep MFA on");

        var viewer = await NewUser(a.C, UserRole.Viewer); var name = viewer.DefaultRequestHeaders.GetValues("X-Test-User").Single(); var v = await Enrol(viewer, name, Pw);
        (await viewer.PostAsJsonAsync("/api/auth/mfa/disable", new MfaDisableRequest("wrong-password-xx", Next(v.Secret)), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await viewer.PostAsJsonAsync("/api/auth/mfa/disable", new MfaDisableRequest(Pw, Next(v.Secret)), Json)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Login(fx.CreateClient(), name, Pw)).Me!.MfaRequired.Should().BeFalse();

        // admin reset for a lost device
        var v2 = await NewUser(a.C, UserRole.Viewer); var n2 = v2.DefaultRequestHeaders.GetValues("X-Test-User").Single(); await Enrol(v2, n2, Pw);
        var id = (await a.C.GetFromJsonAsync<List<UserDto>>("/api/users", Json))!.Single(u => u.Username == n2); id.MfaEnabled.Should().BeTrue();
        (await a.C.PutAsJsonAsync($"/api/users/{id.Id}", new UpdateUserRequest(null, null, null, ResetMfa: true), Json)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await v2.GetAsync("/api/switches")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the old session ends with the reset");
        (await Login(fx.CreateClient(), n2, Pw)).Me!.MfaRequired.Should().BeFalse();
    }

    /// <summary>The bootstrap admin can be enrolled only once per fixture: the first test to need it does it, the others log in with the code.</summary>
    private async Task<(HttpClient C, byte[] Secret, List<string> Codes, string User)> EnrolledAdminOrLogin()
    {
        lock (Shared) { _shared ??= EnrolledAdmin(); }
        var e = await _shared!;
        var c = fx.CreateClient(); var ch = (await Login(c, "admin", MfaApiFixture.AdminPassword)).Me!.MfaChallenge!;
        // each caller gets a fresh session; use a recovery code so concurrent TOTP replay protection cannot interfere
        var code = e.Codes[Interlocked.Increment(ref _rc)]; var r = await c.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(ch, code), Json); r.StatusCode.Should().Be(HttpStatusCode.OK, await r.Content.ReadAsStringAsync());
        return (c, e.Secret, e.Codes, "admin");
    }
    [Fact]
    public async Task One_account_can_have_several_authenticator_apps_and_any_of_them_signs_in()
    {
        var boss = await EnrolledAdminOrLogin();
        var name = Uniq("shared-admin"); (await boss.C.PostAsJsonAsync("/api/users", new CreateUserRequest(name, Pw, UserRole.Admin), Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        var c = fx.CreateClient(); await Login(c, name, Pw);
        var first = await Enrol(c, name, Pw);                                         // device 1 (the first admin's phone)

        // a second phone is refused without a code from an existing one
        (await c.PostAsJsonAsync("/api/auth/mfa/setup", new MfaStartRequest(Pw, "Second admin phone"), Json)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var setup = await c.PostAsJsonAsync("/api/auth/mfa/setup", new MfaStartRequest(Pw, "Second admin phone", Next(first.Secret)), Json);
        setup.StatusCode.Should().Be(HttpStatusCode.OK, await setup.Content.ReadAsStringAsync());
        var secondSecret = Totp.FromBase32((await setup.Content.ReadFromJsonAsync<MfaSetupDto>(Json))!.Secret)!;
        var enable = await c.PostAsJsonAsync("/api/auth/mfa/enable", new MfaCodeRequest(Totp.Code(secondSecret, Totp.StepOf(DateTimeOffset.UtcNow))), Json);
        enable.StatusCode.Should().Be(HttpStatusCode.OK, await enable.Content.ReadAsStringAsync());
        (await enable.Content.ReadFromJsonAsync<MfaEnabledDto>(Json))!.RecoveryCodes.Should().BeEmpty("recovery codes are issued only with the first authenticator");

        var list = (await c.GetFromJsonAsync<List<MfaDeviceDto>>("/api/auth/mfa/devices", Json))!;
        list.Select(d => d.Name).Should().BeEquivalentTo(["Authenticator 1", "Second admin phone"]);
        (await c.GetStringAsync("/api/auth/mfa/devices")).Should().NotContain(Convert.ToBase64String(secondSecret)).And.NotContain("secret", "secrets are never returned");

        // the second phone signs in on its own
        var c2 = fx.CreateClient(); var step1 = await Login(c2, name, Pw);
        var ok = await c2.PostAsJsonAsync("/api/auth/login/mfa", new MfaLoginRequest(step1.Me!.MfaChallenge!, Totp.Code(secondSecret, Totp.StepOf(DateTimeOffset.UtcNow) + 1)), Json);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);

        // remove it again (needs password + a current code; a recovery code is used here so the test need not wait for the next 30-second step); the last one cannot be removed
        var second = list.Single(d => d.Name == "Second admin phone");
        (await c.PostAsJsonAsync($"/api/auth/mfa/devices/{second.Id}/remove", new MfaDisableRequest(Pw, first.Codes[0]), Json)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var left = (await c.GetFromJsonAsync<List<MfaDeviceDto>>("/api/auth/mfa/devices", Json))!; left.Should().ContainSingle();
        (await c.PostAsJsonAsync($"/api/auth/mfa/devices/{left[0].Id}/remove", new MfaDisableRequest(Pw, first.Codes[1]), Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static readonly object Shared = new(); private static Task<(HttpClient C, byte[] Secret, List<string> Codes, string User)>? _shared; private static int _rc;
}
