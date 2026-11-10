using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SwitchPortFinder.Domain;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// One-time fill of Device.Vendor for rows created before the column existed, so they become searchable without waiting for a rescan.
/// Runs in the background after start-up, never blocks it, and does nothing once every device has a value. Failures are logged, not fatal
/// (for example when the database has not been migrated yet).
/// </summary>
public sealed class VendorBackfillService(IServiceScopeFactory scopes, ILogger<VendorBackfillService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var total = 0;
            while (!ct.IsCancellationRequested)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var batch = await db.Devices.Where(d => d.Vendor == null).OrderBy(d => d.Id).Take(1000).ToListAsync(ct);
                if (batch.Count == 0) break;
                foreach (var d in batch) d.Vendor = OuiLookup.Lookup(d.Mac) ?? "";
                await db.SaveChangesAsync(ct); total += batch.Count;
            }
            if (total > 0) log.LogInformation("VendorBackfill: filled the manufacturer for {Count} existing devices", total);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogWarning(ex, "VendorBackfill skipped (database not ready or not migrated yet); it will run on the next start."); }
    }
}
