using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
[Route("api/switches")]
public class SwitchesController(SwitchService svc, DiscoveryService discovery, PortQueryService ports, ClassificationService classification) : ControllerBase
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
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await svc.DeleteAsync(id, ct);
        await classification.RunExclusiveAsync(null, ct);   // ports that pointed at the deleted switch are re-classified
        return NoContent();
    }

    /// <summary>Test credentials typed into the Add/Edit form. Nothing is saved.</summary>
    [HttpPost("test")] public async Task<IActionResult> Test(TestConnectionRequest req, CancellationToken ct) => Ok(await svc.TestUnsavedAsync(req, ct));

    [HttpPost("{id:int}/test")] public async Task<IActionResult> TestSaved(int id, CancellationToken ct) => Ok(await svc.TestSavedAsync(id, ct));

    /// <summary>Queues a discovery job and returns immediately (202 + job id).</summary>
    [HttpPost("{id:int}/scan")]
    public async Task<IActionResult> Scan(int id, CancellationToken ct) { var jobId = await discovery.EnqueueAsync(id, ct); return Accepted($"/api/discovery/jobs/{jobId}", new { jobId }); }

    [HttpPost("scan-all")]
    public async Task<IActionResult> ScanAll(CancellationToken ct) { var ids = await discovery.EnqueueAllAsync(ct); return Accepted(new { jobIds = ids }); }

    [HttpGet("{id:int}/neighbors")] public async Task<IActionResult> Neighbors(int id, CancellationToken ct) => Ok(await ports.NeighborsAsync(id, ct));

    [HttpGet("{id:int}/ports")] public async Task<IActionResult> Ports(int id, CancellationToken ct) => Ok(await ports.GetAsync(id, ct));
}
