using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Application.Switches;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
[Route("api/switches")]
public class SwitchesController(SwitchService svc) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await svc.ListAsync(ct));

    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await svc.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create(SwitchRequest req, CancellationToken ct)
    {
        var created = await svc.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SwitchRequest req, CancellationToken ct) => Ok(await svc.UpdateAsync(id, req, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) { await svc.DeleteAsync(id, ct); return NoContent(); }

    /// <summary>Test credentials typed into the Add/Edit form. Nothing is saved.</summary>
    [HttpPost("test")] public async Task<IActionResult> Test(TestConnectionRequest req, CancellationToken ct) => Ok(await svc.TestUnsavedAsync(req, ct));

    [HttpPost("{id:int}/test")] public async Task<IActionResult> TestSaved(int id, CancellationToken ct) => Ok(await svc.TestSavedAsync(id, ct));
}
