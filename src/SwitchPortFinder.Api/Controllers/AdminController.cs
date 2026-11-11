using System.Security.Claims;
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
public class AdminController(SwitchImportService import, ISwitchRepository switches, Microsoft.Extensions.Options.IOptions<ImportOptions> options, Microsoft.Extensions.Options.IOptions<AuthOptions> authOptions) : ControllerBase
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("import/template")]
    public IActionResult Template([FromQuery] string format = "csv") =>
        File(ImportTemplateBuilder.Template(format), ContentType(format), $"switch-import-template.{Ext(format)}");

    [HttpPost("import/switches/preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Preview(IFormFile file, [FromQuery] ImportMode mode = ImportMode.SkipExisting, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) throw new ValidationFailedException("file", "Choose a .csv, .xlsx or encrypted .spfenc file.");
        if (file.Length > options.Value.MaxFileBytes) throw new ValidationFailedException("file", "File is too large.");
        await using var stream = file.OpenReadStream();   // processed in memory; never written to disk
        if (file.FileName.EndsWith(".spfenc", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(); await stream.CopyToAsync(ms, ct);
            await using var plain = new MemoryStream(SwitchPortFinder.Application.Import.SecureExport.Decrypt(ms.ToArray(), Request.Form["passphrase"].ToString()));   // credentials stay in memory only
            return Ok(await import.PreviewAsync(plain, "switches.csv", mode, ct));
        }
        return Ok(await import.PreviewAsync(stream, file.FileName, mode, ct));
    }

    public record CommitRequest(string PreviewId, ImportMode Mode);

    [HttpPost("import/switches/commit")]
    public async Task<IActionResult> Commit(CommitRequest req, CancellationToken ct) => Ok(await import.CommitAsync(req.PreviewId, req.Mode, ct));

    /// <summary>Exports the switch list WITHOUT credentials so it can be edited and re-imported.</summary>
    [HttpGet("export/switches")]
    public async Task<IActionResult> ExportSwitches([FromQuery] string format = "xlsx", CancellationToken ct = default) =>
        File(ImportTemplateBuilder.ExportSwitches(await switches.ListAsync(ct), format), ContentType(format), $"switches.{Ext(format)}");

    public record SecureExportRequest(string? Password, string Passphrase);

    /// <summary>
    /// Switches WITH credentials, only as an encrypted file (.spfenc) protected by a passphrase of the administrator's choosing. In Local mode the
    /// administrator's password must be entered again. Every use is written to the application log.
    /// </summary>
    [HttpPost("export/switches/secure"), Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("login")]
    public async Task<IActionResult> ExportSwitchesSecure(SecureExportRequest req, [FromServices] SwitchPortFinder.Application.Switches.SwitchService svc, [FromServices] SwitchPortFinder.Infrastructure.Security.UserService users,
        [FromServices] Microsoft.Extensions.Logging.ILogger<AdminController> log, CancellationToken ct)
    {
        if (authOptions.Value.IsLocal)
        {
            var id = int.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
            if (!await users.VerifyPasswordAsync(id, req.Password ?? "", ct)) throw new ValidationFailedException("password", "Your password is incorrect.");
        }
        var bytes = await svc.ExportEncryptedWithCredentialsAsync(req.Passphrase, ct);
        log.LogWarning("CredentialExport: the switch list with SNMP credentials was exported (encrypted) by {Actor}", User.Identity?.Name ?? "system");
        return File(bytes, "application/octet-stream", $"switches-with-credentials-{DateTime.UtcNow:yyyyMMdd-HHmm}.spfenc");
    }

    private static string Ext(string f) => f.Equals("xlsx", StringComparison.OrdinalIgnoreCase) ? "xlsx" : "csv";
    private static string ContentType(string f) => Ext(f) == "xlsx" ? Xlsx : "text/csv";
}
