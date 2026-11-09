using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Switches;

namespace SwitchPortFinder.Api.Controllers;

/// <summary>Profiles are returned without secrets (only "has password" flags).</summary>
[ApiController]
[Route("api/credential-profiles")]
[Authorize(Policy = Policies.AdminOnly)]
public class CredentialProfilesController(CredentialProfileService svc) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));
    [HttpPost] public async Task<IActionResult> Create(CredentialProfileRequest req, CancellationToken ct) => StatusCode(201, await svc.CreateAsync(req, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, CredentialProfileRequest req, CancellationToken ct) => Ok(await svc.UpdateAsync(id, req, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }
}
