using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwitchPortFinder.Api.Infrastructure;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Import;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Api.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Policy = Policies.AdminOnly)]
public class AdminController(SwitchImportService import, ISwitchRepository switches, Microsoft.Extensions.Options.IOptions<ImportOptions> options) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("import/template")]
    public IActionResult Template([FromQuery] string format = "csv") =>
        File(ImportTemplateBuilder.Template(format), ContentType(format), $"switch-import-template.{Ext(format)}");

    [HttpPost("import/switches/preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Preview(IFormFile file, [FromQuery] ImportMode mode = ImportMode.SkipExisting, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) throw new ValidationFailedException("file", "Choose a .csv or .xlsx file.");
        if (file.Length > options.Value.MaxFileBytes) throw new ValidationFailedException("file", "File is too large.");
        await using var stream = file.OpenReadStream();   // processed in memory; never written to disk
        return Ok(await import.PreviewAsync(stream, file.FileName, mode, ct));
    }

    public record CommitRequest(string PreviewId, ImportMode Mode);

    [HttpPost("import/switches/commit")]
    public async Task<IActionResult> Commit(CommitRequest req, CancellationToken ct) => Ok(await import.CommitAsync(req.PreviewId, req.Mode, ct));

    /// <summary>Exports the switch list WITHOUT credentials so it can be edited and re-imported.</summary>
    [HttpGet("export/switches")]
    public async Task<IActionResult> ExportSwitches([FromQuery] string format = "xlsx", CancellationToken ct = default) =>
        File(ImportTemplateBuilder.ExportSwitches(await switches.ListAsync(ct), format), ContentType(format), $"switches.{Ext(format)}");

    private static string Ext(string f) => f.Equals("xlsx", StringComparison.OrdinalIgnoreCase) ? "xlsx" : "csv";
    private static string ContentType(string f) => Ext(f) == "xlsx" ? Xlsx : "text/csv";
}
