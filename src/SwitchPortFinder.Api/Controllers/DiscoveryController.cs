using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Infrastructure.Discovery;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
public class DiscoveryController(AccessPointService accessPoints, ManualDeviceService manual, SwitchPortFinder.Application.Abstractions.IUserAuthorizationService who, DiscoveryService discovery, DeviceQueryService devices, SearchService search, TraceService trace, TopologyService topology, DashboardService dashboard, Microsoft.Extensions.Options.IOptions<SwitchPortFinder.Application.Common.ExportOptions> exportOpt) : ControllerBase
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

    /// <summary>Access points: location, status and the MACs learned on their ports.</summary>
    [HttpGet("api/access-points")]
    public async Task<IActionResult> AccessPoints(CancellationToken ct) => Ok(await accessPoints.GetAsync(ct));

    /// <summary>Add a device by hand (administrator): MAC, name, owner, notes and optionally the switch port it is plugged into.</summary>
    [HttpPost("api/devices")]
    public async Task<IActionResult> AddDevice(ManualDeviceRequest r, CancellationToken ct) { var id = await manual.CreateAsync(r, who.CurrentUserName, ct); return CreatedAtAction(nameof(Device), new { id }, await devices.GetAsync(id, ct)); }

    /// <summary>Set or clear the manual name/owner/notes and the pinned switch port of any device (administrator).</summary>
    [HttpPut("api/devices/{id:int}/manual")]
    public async Task<IActionResult> EditDevice(int id, ManualDeviceRequest r, CancellationToken ct) { await manual.UpdateAsync(id, r, who.CurrentUserName, ct); return Ok(await devices.GetAsync(id, ct)); }

    /// <summary>Delete a device that was added by hand and never seen by a scan.</summary>
    [HttpDelete("api/devices/{id:int}")]
    public async Task<IActionResult> DeleteDevice(int id, CancellationToken ct) { await manual.DeleteAsync(id, who.CurrentUserName, ct); return NoContent(); }

    /// <summary>Hop list from the root switch down to the device's edge port; marked partial when a hop cannot be proven.</summary>
    [HttpGet("api/devices/{id:int}/trace")]
    public async Task<IActionResult> Trace(int id, CancellationToken ct) => Ok(await trace.TraceAsync(id, ct));

    /// <summary>One box for MAC (any format), IP, hostname, switch name/IP and port name.</summary>
    [HttpGet("api/search")]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct) => Ok(await search.SearchAsync(q, ct));

    [HttpGet("api/topology")]
    public async Task<IActionResult> Topology(CancellationToken ct) => Ok(await topology.GetAsync(ct));

    [HttpGet("api/dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await dashboard.GetAsync(ct));

    /// <summary>Top ports by utilization or by packet drops, from the counters of the last two scans. Read-only.</summary>
    [HttpGet("api/metrics/top-ports")]
    public async Task<IActionResult> TopPorts([FromQuery] string metric = "utilization", [FromQuery] int n = 5, CancellationToken ct = default)
    {
        if (metric is not ("utilization" or "drops")) return BadRequest(new { message = "metric must be 'utilization' or 'drops'." });
        return Ok(await dashboard.TopPortsAsync(metric, Math.Clamp(n, 1, 20), ct));
    }

    /// <summary>Uptime of up to N switches (core first). Read-only.</summary>
    [HttpGet("api/metrics/switch-uptime")]
    public async Task<IActionResult> SwitchUptime([FromQuery] int n = 5, CancellationToken ct = default) => Ok(await dashboard.SwitchUptimeAsync(Math.Clamp(n, 1, 20), ct));

    /// <summary>CSV or Excel export of the devices matching the same filters as the list (q, switchId, state, sort, desc). Never paged.</summary>
    [HttpGet("api/export/devices")]
    public async Task<IActionResult> ExportDevices([FromQuery] DeviceQuery query, [FromQuery] string format = "csv", CancellationToken ct = default)
    {
        var (rows, _) = await devices.ExportAsync(query, exportOpt.Value.MaxDeviceRows, ct);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        return string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)
            ? File(DeviceExportBuilder.Xlsx(rows), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"devices-{stamp}.xlsx")
            : File(DeviceExportBuilder.Csv(rows), "text/csv", $"devices-{stamp}.csv");
    }
}
