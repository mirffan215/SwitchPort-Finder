using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Security;

namespace SwitchPortFinder.Api.Infrastructure;

/// <summary>Authentication off (default): everyone is "system" and an administrator. Local mode: the signed-in cookie principal.</summary>
public class HttpContextUserAuthorizationService(IHttpContextAccessor http, IOptions<AuthOptions> options) : IUserAuthorizationService
{
    private ClaimsPrincipal? User => http.HttpContext?.User;
    public bool IsAuthenticated => !options.Value.IsLocal || User?.Identity?.IsAuthenticated == true;
    public string CurrentUserName => !options.Value.IsLocal ? "system" : User?.Identity?.Name ?? "anonymous";
    public Task<bool> IsAdminAsync(CancellationToken ct = default) => Task.FromResult(!options.Value.IsLocal || User?.IsInRole(nameof(UserRole.Admin)) == true);
}

public static class AuthSetup
{
    public const string Scheme = "spf.cookie";

    public const string VersionClaim = "spf:sv";

    public static ClaimsPrincipal Principal(int id, string name, UserRole role, int version) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.Role, role.ToString()), new Claim(VersionClaim, version.ToString())], Scheme));

    public static void Configure(CookieAuthenticationOptions o, AuthOptions auth)
    {
        o.Cookie.Name = "spf.session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;   // Secure automatically once the portal is served over HTTPS
        o.ExpireTimeSpan = TimeSpan.FromMinutes(auth.SessionMinutes); o.SlidingExpiration = true;
        // API clients get status codes, never redirects to a login page.
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = async ctx =>
        {
            // Re-check the account on every request (cached 15 s): disabled, deleted or re-roled users lose their session quickly.
            var ok = int.TryParse(ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
            var state = ok ? await ctx.HttpContext.RequestServices.GetRequiredService<UserService>().SessionStateAsync(id, ctx.HttpContext.RequestAborted) : (false, UserRole.Viewer, 0, false);
            // a cookie minted before the last logout / password change / role change carries an older version and is refused
            if (!state.Item1 || ctx.Principal?.FindFirstValue(ClaimTypes.Role) != state.Item2.ToString() || ctx.Principal?.FindFirstValue(VersionClaim) != state.Item3.ToString())
            { ctx.RejectPrincipal(); await ctx.HttpContext.SignOutAsync(Scheme); }
        };
    }
}

/// <summary>
/// Policy "administrators must use two-factor authentication": an administrator who has not enrolled yet is signed in but can only reach /api/auth/*
/// (to enrol, read their state or sign out) until enrolment is complete. Everything else answers 403 with mfaSetupRequired.
/// </summary>
public class MfaEnrollmentGateMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    public async Task InvokeAsync(HttpContext ctx, UserService users)
    {
        var o = options.Value;
        if (o.IsLocal && o.RequireMfaForAdmins && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/auth")
            && ctx.User.Identity?.IsAuthenticated == true && ctx.User.IsInRole(nameof(UserRole.Admin))
            && int.TryParse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && !(await users.SessionStateAsync(id, ctx.RequestAborted)).Mfa)
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = "Two-factor authentication required", status = 403, detail = "Set up an authenticator app to continue.", mfaSetupRequired = true });
            return;
        }
        await next(ctx);
    }
}

/// <summary>Local mode: any state-changing API call needs the Admin role (Viewers are read-only). Unauthenticated calls are stopped earlier (401).</summary>
public class WriteRequiresAdminMiddleware(RequestDelegate next, IOptions<AuthOptions> options)
{
    private static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public async Task InvokeAsync(HttpContext ctx)
    {
        if (options.Value.IsLocal && ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/auth")
            && !Safe.Contains(ctx.Request.Method) && ctx.User.Identity?.IsAuthenticated == true && !ctx.User.IsInRole(nameof(UserRole.Admin)))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = "Forbidden", status = 403, detail = "Your account is read-only." });
            return;
        }
        await next(ctx);
    }
}
