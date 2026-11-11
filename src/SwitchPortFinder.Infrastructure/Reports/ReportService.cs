using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Reports;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Reports;

/// <summary>Saved report definitions (name + JSON). The JSON is only ever read back by the UI and run through <see cref="ReportEngine"/>'s whitelist.</summary>
public class ReportService(AppDbContext db)
{
    private static SavedReportDto ToDto(ReportDefinition r) => new(r.Id, r.Name, r.Json, r.CreatedBy, r.UpdatedAt);

    public async Task<List<SavedReportDto>> ListAsync(CancellationToken ct) => (await db.Reports.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();
    public async Task<SavedReportDto> GetAsync(int id, CancellationToken ct) => ToDto(await db.Reports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new NotFoundException("Report not found."));

    private static (string Name, string Json) Check(SaveReportRequest r)
    {
        var name = r.Name?.Trim() ?? "";
        if (name.Length is < 1 or > 150 || name.Any(char.IsControl)) throw new ValidationFailedException("name", "Give the report a name (up to 150 characters).");
        if ((r.Json?.Length ?? 0) is 0 or > 20_000) throw new ValidationFailedException("json", "The report definition is empty or too large.");
        try { using var d = JsonDocument.Parse(r.Json!); if (d.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
        catch (JsonException) { throw new ValidationFailedException("json", "The report definition is not valid JSON."); }
        return (name, r.Json!);
    }

    public async Task<SavedReportDto> CreateAsync(SaveReportRequest r, string actor, CancellationToken ct)
    {
        var (name, json) = Check(r);
        if (await db.Reports.CountAsync(ct) >= 500) throw new ConflictException("Too many saved reports. Delete some first.");
        var e = new ReportDefinition { Name = name, Json = json, CreatedBy = actor, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Reports.Add(e); await db.SaveChangesAsync(ct); return ToDto(e);
    }

    public async Task<SavedReportDto> UpdateAsync(int id, SaveReportRequest r, CancellationToken ct)
    {
        var (name, json) = Check(r);
        var e = await db.Reports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report not found.");
        e.Name = name; e.Json = json; e.UpdatedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); return ToDto(e);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var e = await db.Reports.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Report not found.");
        db.Reports.Remove(e); await db.SaveChangesAsync(ct);
    }
}
