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
public class AuthController(UserService users, IOptions<AuthOptions> options) : ControllerBase
{
    private AuthOptions Auth => options.Value;

    /// <summary>Tells the UI whether a login is required and who is signed in. Anonymous by design.</summary>
    [HttpGet("me"), AllowAnonymous]
    public IActionResult Me()
    {
        if (!Auth.IsLocal) return Ok(new MeDto(false, true, "system", UserRole.Admin));
        var u = HttpContext.User;
        return Ok(u.Identity?.IsAuthenticated == true
            ? new MeDto(true, true, u.Identity.Name, Enum.Parse<UserRole>(u.FindFirstValue(ClaimTypes.Role)!))
            : new MeDto(true, false, null, null));
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        var (outcome, user) = await users.AuthenticateAsync(req.Username, req.Password, ct);
        if (outcome == LoginOutcome.Locked) return Problem(statusCode: 429, title: "Too many failed sign-in attempts. Try again later.");
        if (outcome != LoginOutcome.Ok || user is null) return Problem(statusCode: 401, title: "Invalid username or password.");
        await HttpContext.SignInAsync(AuthSetup.Scheme, AuthSetup.Principal(user.Id, user.Username, user.Role),
            new AuthenticationProperties { IsPersistent = false, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(Auth.SessionMinutes) });
        return Ok(new MeDto(true, true, user.Username, user.Role));
    }

    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        if (Auth.IsLocal) await HttpContext.SignOutAsync(AuthSetup.Scheme);   // with authentication off there is no session to end
        return NoContent();
    }

    // No [Authorize] attribute: with authentication off there is no scheme to challenge (that threw a 500). In Local mode the fallback policy
    // already requires a signed-in user for every endpoint that is not [AllowAnonymous].
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest req, CancellationToken ct)
    {
        if (!Auth.IsLocal) return Problem(statusCode: 400, title: "Authentication is not enabled.");
        await users.ChangeOwnPasswordAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), req, ct);
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
