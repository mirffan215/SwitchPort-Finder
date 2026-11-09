using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;

namespace SwitchPortFinder.Api.Infrastructure;

/// <summary>
/// V1 has no login, so the main browser-borne risk is another web page (any site a user visits) making the user's browser call this portal.
/// 1. State-changing requests that carry an Origin header must come from the portal's own origin (or a configured trusted origin).
/// 2. Security headers: no sniffing, no framing, no referrer, strict CSP in production, API responses are never cached.
/// There is deliberately NO CORS configuration: browsers then block cross-origin reads of API responses.
/// </summary>
public class SecurityMiddleware(RequestDelegate next, IOptions<SecurityOptions> options, IHostEnvironment env)
{
    private static readonly HashSet<string> Safe = new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS" };

    public async Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff"; h["X-Frame-Options"] = "DENY"; h["Referrer-Policy"] = "no-referrer";
        // Swagger UI (Development only) needs inline scripts, so the strict CSP applies to production.
        if (!env.IsDevelopment())
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        if (ctx.Request.Path.StartsWithSegments("/api")) h["Cache-Control"] = "no-store";

        if (!Safe.Contains(ctx.Request.Method) && ctx.Request.Headers.Origin.ToString() is { Length: > 0 } origin && !IsAllowed(ctx, origin))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            await ctx.Response.WriteAsJsonAsync(new { title = "Forbidden", status = 403, detail = "Cross-origin request blocked." });
            return;
        }
        await next(ctx);
    }

    private bool IsAllowed(HttpContext ctx, string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var o)) return false;
        var host = ctx.Request.Host.Value;
        if (string.Equals(o.Authority, host, StringComparison.OrdinalIgnoreCase)) return true;
        // behind a reverse proxy the original host may arrive in X-Forwarded-Host
        if (ctx.Request.Headers["X-Forwarded-Host"].ToString() is { Length: > 0 } fwd && string.Equals(o.Authority, fwd, StringComparison.OrdinalIgnoreCase)) return true;
        return options.Value.TrustedOrigins.Any(t => string.Equals(t.TrimEnd('/'), origin.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
    }
}
