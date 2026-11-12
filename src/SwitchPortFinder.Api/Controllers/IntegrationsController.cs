using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Infrastructure.Integrations;

namespace SwitchPortFinder.Api.Controllers;

/// <summary>Optional DHCP / Active Directory / Intune integrations: administrators only; every one is off until turned on. Secrets are never returned.</summary>
[ApiController]
[Route("api/integrations")]
[Authorize(Policy = Policies.AdminOnly)]
public class IntegrationsController(IntegrationService svc, IUserAuthorizationService who) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));
    [HttpPut("{kind}")] public async Task<IActionResult> Save(string kind, IntegrationRequest r, CancellationToken ct) => Ok(await svc.SaveAsync(kind, r, who.CurrentUserName, ct));
    [HttpPost("{kind}/run"), EnableRateLimiting("snmp-test")] public async Task<IActionResult> Run(string kind, CancellationToken ct) => Ok(await svc.RunAsync(kind, ct));
}

/// <summary>Company name, link and logo. Reading is public (the sign-in page shows the logo); changing needs an administrator.</summary>
[ApiController]
[Route("api/branding")]
public class BrandingController(BrandingService svc) : ControllerBase
{
    [HttpGet, AllowAnonymous] public async Task<IActionResult> Get(CancellationToken ct) => Ok(await svc.GetAsync(ct));

    [HttpGet("logo"), AllowAnonymous]
    public async Task<IActionResult> Logo(CancellationToken ct)
    {
        if (await svc.LogoAsync(ct) is not { } l) return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff"; Response.Headers["Cache-Control"] = "public, max-age=300";
        return File(l.Bytes, l.Type);
    }

    [HttpPut, Authorize(Policy = Policies.AdminOnly)] public async Task<IActionResult> Put(BrandingRequest r, CancellationToken ct) => Ok(await svc.SaveAsync(r.CompanyName, r.LinkUrl, ct));

    [HttpPost("logo"), Authorize(Policy = Policies.AdminOnly), RequestSizeLimit(BrandingService.MaxLogoBytes + 4096)]
    public async Task<IActionResult> Upload(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length is 0 or > BrandingService.MaxLogoBytes) throw new ValidationFailedException("logo", "The logo must be an image of up to 512 KB.");
        using var ms = new MemoryStream(); await file.CopyToAsync(ms, ct);
        return Ok(await svc.SetLogoAsync(ms.ToArray(), ct));
    }

    [HttpDelete("logo"), Authorize(Policy = Policies.AdminOnly)] public async Task<IActionResult> Remove(CancellationToken ct) => Ok(await svc.RemoveLogoAsync(ct));
}

public sealed record BrandingRequest(string? CompanyName, string? LinkUrl);
