using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
public class DiscoveryController(DiscoveryService discovery, DeviceQueryService devices, SearchService search, TraceService trace) : ControllerBase
{
    [HttpGet("api/discovery/jobs")]
    public async Task<IActionResult> Jobs([FromQuery] int take = 50, [FromQuery] int? switchId = null, CancellationToken ct = default) => Ok(await discovery.ListAsync(take, switchId, ct));

    [HttpGet("api/discovery/jobs/{id:int}")]
    public async Task<IActionResult> Job(int id, CancellationToken ct) => Ok(await discovery.GetAsync(id, ct));

    /// <summary>Server-side paged, sortable, filterable device list (never returns everything).</summary>
    [HttpGet("api/devices")]
    public async Task<IActionResult> Devices([FromQuery] DeviceQuery query, CancellationToken ct) => Ok(await devices.ListAsync(query, ct));   // not named 'q': it would swallow the ?q= filter as a model prefix

    [HttpGet("api/devices/{id:int}")]
    public async Task<IActionResult> Device(int id, CancellationToken ct) => Ok(await devices.GetAsync(id, ct));

    /// <summary>Hop list from the root switch down to the device's edge port; marked partial when a hop cannot be proven.</summary>
    [HttpGet("api/devices/{id:int}/trace")]
    public async Task<IActionResult> Trace(int id, CancellationToken ct) => Ok(await trace.TraceAsync(id, ct));

    /// <summary>One box for MAC (any format), IP, hostname, switch name/IP and port name.</summary>
    [HttpGet("api/search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct) => Ok(await search.SearchAsync(q, ct));
}
