using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Infrastructure.Persistence;

/// <summary>
/// Persists runtime settings and applies them LIVE by updating the singleton option instances in place (every service reads
/// IOptions&lt;T&gt;.Value on each use, so changes take effect on the next scan without a restart).
/// </summary>
public class SettingsService(IServiceScopeFactory scopes, IOptions<SnmpOptions> snmp, IOptions<DiscoveryOptions> discovery, ILogger<SettingsService> log)
{
    private const string Key = "runtime";
    private static readonly object Lock = new();

    public RuntimeSettings Current { get { lock (Lock) return RuntimeSettings.From(snmp.Value, discovery.Value); } }

    /// <summary>Called at startup after migrations; a missing table (migration not yet applied) just means "use appsettings.json".</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == Key, ct);
            if (row is null) return;
            var loaded = JsonSerializer.Deserialize<RuntimeSettings>(row.Value);
            if (loaded is null || loaded.Validate().Count > 0) { log.LogWarning("Stored settings are invalid and were ignored; using appsettings.json values."); return; }
            lock (Lock) loaded.ApplyTo(snmp.Value, discovery.Value);
            log.LogInformation("Runtime settings loaded from the database");
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not load runtime settings; using appsettings.json values"); }
    }

    public async Task<RuntimeSettings> UpdateAsync(RuntimeSettings s, CancellationToken ct)
    {
        var errors = s.Validate();
        if (errors.Count > 0) throw new ValidationFailedException(errors);
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (row is null) db.AppSettings.Add(row = new AppSetting { Key = Key });
        row.Value = JsonSerializer.Serialize(s); row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        lock (Lock) s.ApplyTo(snmp.Value, discovery.Value);
        log.LogInformation("Runtime settings updated");
        return Current;
    }
}
