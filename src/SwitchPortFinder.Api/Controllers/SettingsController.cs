using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Policy = Policies.AdminOnly)]
public class SettingsController(SettingsService settings) : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok(settings.Current);
    [HttpPut] public async Task<IActionResult> Put(RuntimeSettings s, CancellationToken ct) => Ok(await settings.UpdateAsync(s, ct));
}
