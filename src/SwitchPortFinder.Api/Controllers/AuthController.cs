using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserService users, MfaService mfa, IOptions<AuthOptions> options) : ControllerBase
{
    private AuthOptions Auth => options.Value;

    /// <summary>Tells the UI whether a login is required and who is signed in. Anonymous by design.</summary>
    [HttpGet("me"), AllowAnonymous]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!Auth.IsLocal) return Ok(new MeDto(false, true, "system", UserRole.Admin));
        var u = HttpContext.User;
        if (u.Identity?.IsAuthenticated != true) return Ok(new MeDto(true, false, null, null));
        var role = Enum.Parse<UserRole>(u.FindFirstValue(ClaimTypes.Role)!);
        var enabled = int.TryParse(u.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && (await users.SessionStateAsync(id, ct)).Mfa;
        return Ok(new MeDto(true, true, u.Identity.Name, role, enabled, MfaSetupRequired: !enabled && mfa.Required(role)));
    }

    private async Task<MeDto> SignIn(int id, string name, UserRole role, int version, bool mfaOn)
    {
        await HttpContext.SignInAsync(AuthSetup.Scheme, AuthSetup.Principal(id, name, role, version),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(Auth.SessionMinutes) });
        return new MeDto(true, true, name, role, mfaOn, MfaSetupRequired: !mfaOn && mfa.Required(role));
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var (outcome, user) = await users.AuthenticateAsync(req.Username, req.Password, ct);
        if (outcome == LoginOutcome.Locked) return Problem(statusCode: 429, title: "Too many failed sign-in attempts. Try again later.");
        if (outcome != LoginOutcome.Ok || user is null) return Problem(statusCode: 401, title: "Invalid username or password.");
        // Two-factor: the password was right, but there is no session until a valid code is presented.
        if (user.MfaEnabled) return Ok(new MeDto(true, false, null, null, MfaEnabled: true, MfaRequired: true, MfaChallenge: mfa.MakeChallenge(user.Id, user.SessionVersion)));
        return Ok(await SignIn(user.Id, user.Username, user.Role, user.SessionVersion, false));
    }

    /// <summary>Second step of a two-factor login: the challenge from step one plus a 6-digit code (or a recovery code).</summary>
    [HttpPost("login/mfa"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> LoginMfa(MfaLoginRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var id = await mfa.ReadChallengeAsync(req.Challenge, ct);
        if (id is null) return Problem(statusCode: 401, title: "The sign-in expired. Enter your password again.");
        if (!await mfa.VerifyLoginAsync(id.Value, req.Code, ct)) return Problem(statusCode: 401, title: "That code is not valid.");
        await users.MarkLoginAsync(id.Value, ct);
        var user = await users.GetForSessionAsync(id.Value, ct);
        return Ok(await SignIn(user.Id, user.Username, user.Role, user.SessionVersion, true));
    }

    private int CurrentId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>Start enrolling an authenticator app (needs the current password). Returns the secret and the otpauth:// URI for the QR code.</summary>
    [HttpPost("mfa/setup")]
    public async Task<IActionResult> MfaSetup(MfaStartRequest req, CancellationToken ct) => !Auth.IsLocal ? Problem(statusCode: 400, title: "Authentication is not enabled.") : Ok(await mfa.StartSetupAsync(CurrentId, req.Password, ct));

    /// <summary>Confirm enrolment with a current code. The one-time recovery codes are returned ONLY here.</summary>
    [HttpPost("mfa/enable")]
    public async Task<IActionResult> MfaEnable(MfaCodeRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var (codes, version) = await mfa.EnableAsync(CurrentId, req.Code, ct);
        await SignIn(CurrentId, User.Identity!.Name!, Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!), version, true);   // keeps this browser signed in
        return Ok(new MfaEnabledDto(codes));
    }

    [HttpPost("mfa/disable")]
    public async Task<IActionResult> MfaDisable(MfaDisableRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var role = Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!);
        if (mfa.Required(role)) return Problem(statusCode: 400, title: "Two-factor authentication is required for administrators and cannot be turned off.");
        var version = await mfa.DisableAsync(CurrentId, req.Password, req.Code, ct);
        await SignIn(CurrentId, User.Identity!.Name!, role, version, false);
        return NoContent();
    }

    [HttpPost("mfa/recovery-codes")]
    public async Task<IActionResult> MfaNewRecoveryCodes(MfaDisableRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var (codes, _) = await mfa.RegenerateRecoveryCodesAsync(CurrentId, req.Password, req.Code, ct);
        return Ok(new MfaEnabledDto(codes));
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (Auth.IsLocal)
        {
            // Ending the session must also kill the cookie itself: a stolen/copied cookie is useless after logout.
            if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) await users.RevokeSessionsAsync(id, HttpContext.RequestAborted);
            await HttpContext.SignOutAsync(AuthSetup.Scheme);
        }
        return NoContent();
    }

    // No [Authorize] attribute: with authentication off there is no scheme to challenge (that threw a 500). In Local mode the fallback policy
    // already requires a signed-in user for every endpoint that is not [AllowAnonymous].
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var id = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var version = await users.ChangeOwnPasswordAsync(id, req, ct);   // every other session of this user is now revoked ...
        await HttpContext.SignInAsync(AuthSetup.Scheme, AuthSetup.Principal(id, User.Identity!.Name!, Enum.Parse<UserRole>(User.FindFirstValue(ClaimTypes.Role)!), version),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(Auth.SessionMinutes) });   // ... except this browser
        return NoContent();
    }
}

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public class UsersController(UserService users, SwitchPortFinder.Application.Abstractions.IUserAuthorizationService who) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await users.ListAsync(ct));
    [HttpPost] public async Task<IActionResult> Create(CreateUserRequest r, CancellationToken ct) => StatusCode(201, await users.CreateAsync(r, who.CurrentUserName, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdateUserRequest r, CancellationToken ct) => Ok(await users.UpdateAsync(id, r, who.CurrentUserName, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await users.DeleteAsync(id, who.CurrentUserName, ct); return NoContent(); }
}
