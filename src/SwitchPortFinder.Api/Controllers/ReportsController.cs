using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Reports;
using SwitchPortFinder.Infrastructure.Reports;

namespace SwitchPortFinder.Api.Controllers;

/// <summary>Report builder: datasets, running a query, Excel/CSV export and saved definitions. Running and exporting only read data, so viewers may use them; saving needs an administrator.</summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(ReportEngine engine, ReportService saved, SwitchNameLookup swRepo, SwitchPortFinder.Application.Abstractions.IUserAuthorizationService who) : ControllerBase
{
    [HttpGet("datasets")] public IActionResult Datasets() => Ok(ReportEngine.Datasets);

    [HttpPost("run"), EnableRateLimiting("report")]
    public async Task<IActionResult> Run(ReportQuery q, CancellationToken ct) => Ok(await engine.RunAsync(q, ct));

    public record ExportRequest(ReportQuery Query, string? Title);

    [HttpPost("export"), EnableRateLimiting("report")]
    public async Task<IActionResult> Export([FromBody] ExportRequest req, [FromQuery] string format = "xlsx", CancellationToken ct = default)
    {
        var r = await engine.RunAsync(req.Query, ct);
        var title = (req.Title ?? "Report").Trim(); if (title.Length > 100) title = title[..100];
        var safe = new string(title.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray()).Trim('-'); if (safe.Length == 0) safe = "report";
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmm");
        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase)) return File(ReportExportBuilder.Csv(r), "text/csv", $"{safe}-{stamp}.csv");
        var d = ReportEngine.Datasets.First(x => x.Key == req.Query.Dataset);
        var swNames = req.Query.SwitchIds is { Count: > 0 } ids ? string.Join(", ", await swRepo.NamesAsync(ids, ct)) : "all";
        var desc = $"Dataset: {d.Label}\nSwitches: {swNames}\nFilters: {(req.Query.Filters is { Count: > 0 } f ? string.Join("; ", f.Select(x => $"{x.Field} {x.Op} {x.Value}")) : "none")}\nGrouped by: {(req.Query.GroupBy is { Count: > 0 } g ? string.Join(", ", g) : "—")}\nPeriod: {(d.History ? $"{(req.Query.From ?? DateTime.UtcNow.AddDays(-7)):yyyy-MM-dd} to {(req.Query.To ?? DateTime.UtcNow):yyyy-MM-dd}" : "current state")}";
        return File(ReportExportBuilder.Xlsx(r, title, desc), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safe}-{stamp}.xlsx");
    }

    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await saved.ListAsync(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await saved.GetAsync(id, ct));
    [HttpPost] public async Task<IActionResult> Create(SaveReportRequest r, CancellationToken ct) => StatusCode(201, await saved.CreateAsync(r, who.CurrentUserName, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SaveReportRequest r, CancellationToken ct) => Ok(await saved.UpdateAsync(id, r, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await saved.DeleteAsync(id, ct); return NoContent(); }
}
