using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.Api.Controllers;

/// <summary>The rules that decide whether a device is an access point, an IP phone, and so on. Administrators only.</summary>
[ApiController]
[Route("api/categories")]
[Authorize(Policy = Policies.AdminOnly)]
public class CategoriesController(DeviceCategoryService categories) : ControllerBase
{
    [HttpGet("rules")] public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await categories.RulesAsync(ct));
    [HttpPost("rules")] public async Task<IActionResult> Create(CategoryRuleRequest r, CancellationToken ct) => StatusCode(201, await categories.CreateAsync(r, ct));
    [HttpPut("rules/{id:int}")] public async Task<IActionResult> Update(int id, CategoryRuleRequest r, CancellationToken ct) => Ok(await categories.UpdateAsync(id, r, ct));
    [HttpDelete("rules/{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await categories.DeleteAsync(id, ct); return NoContent(); }
    /// <summary>Re-run the rules over every device without a manual category.</summary>
    [HttpPost("reapply")] public async Task<IActionResult> Reapply(CancellationToken ct) => Ok(new { changed = await categories.ReapplyAsync(ct) });
}
