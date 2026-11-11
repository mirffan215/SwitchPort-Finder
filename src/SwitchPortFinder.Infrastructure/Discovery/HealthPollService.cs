using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>
/// Lightweight read-only poll of every switch (default every 5 minutes): uptime, latency, CPU, memory and the interface counters. It gives the trend reports and the
/// alerts their data between full discovery scans and is what downtime is measured from (a failed poll is an "unreachable" sample). Also prunes old history.
/// </summary>
public sealed class HealthPollService(IServiceScopeFactory scopes, IOptions<DiscoveryOptions> opt, ILogger<HealthPollService> log) : BackgroundService
{
    private DateTime _lastPrune = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stop);
            var last = DateTime.MinValue;
            while (!stop.IsCancellationRequested)
            {
                var every = opt.Value.HealthPollMinutes;
                if (every > 0 && DateTime.UtcNow - last >= TimeSpan.FromMinutes(every))
                {
                    last = DateTime.UtcNow;
                    try { await PollAllAsync(stop); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Health poll failed"); }
                }
                if (DateTime.UtcNow - _lastPrune > TimeSpan.FromHours(6)) { _lastPrune = DateTime.UtcNow; await PruneAsync(stop); }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, opt.Value.SchedulePollSeconds)), stop);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task PollAllAsync(CancellationToken ct)
    {
        List<int> ids;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            ids = await db.Switches.AsNoTracking().Where(s => s.Credential != null || s.CredentialProfileId != null).Select(s => s.Id).ToListAsync(ct);
        }
        using var gate = new SemaphoreSlim(Math.Max(1, Math.Min(opt.Value.MaxParallelScans, 10)));
        await Task.WhenAll(ids.Select(async id =>
        {
            await gate.WaitAsync(ct);
            try { await PollOneAsync(id, ct); }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Health poll of switch {Id} failed", id); }
            finally { gate.Release(); }
        }));
    }

    public async Task PollOneAsync(int switchId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        if (await db.DiscoveryJobs.AnyAsync(j => j.SwitchId == switchId && (j.Status == JobStatus.Running || j.Status == JobStatus.Queued), ct)) return;   // a full scan is reading this switch right now
        var sw = await sp.GetRequiredService<ISwitchRepository>().GetAsync(switchId, ct); if (sw is null) return;
        var creds = sp.GetRequiredService<SwitchService>().ResolveCredentials(sw); if (creds is null) return;
        var vendor = VendorDetector.Effective(sw.Manufacturer, sw.DetectedManufacturer);
        var h = await sp.GetRequiredService<IHealthProbe>().ProbeAsync(new HealthRequest(sw.ManagementIp, creds, vendor, true), ct);
        var now = DateTime.UtcNow;
        if (!h.Reachable)
        {
            sw.Status = SwitchStatus.Unreachable;
            db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = sw.Id, At = now, Reachable = false });
            await db.SaveChangesAsync(ct); return;
        }
        sw.Status = SwitchStatus.Reachable;
        if (h.UptimeSeconds is { } up) { sw.UptimeSeconds = up; sw.UptimeAt = now; }
        sw.CpuPct = h.CpuPercent; sw.MemPct = h.MemoryPercent; sw.LatencyMs = h.LatencyMs; sw.HealthAt = now;
        var samples = new List<(SwitchPort, DateTime)>();
        if (h.Interfaces is { Count: > 0 } ifaces)
        {
            var ports = await db.SwitchPorts.Where(p => p.SwitchId == sw.Id).ToDictionaryAsync(p => p.IfIndex, ct);
            foreach (var i in ifaces)
            {
                if (!ports.TryGetValue(i.IfIndex, out var p)) continue;   // ports are created by a full scan
                if (i.OperKnown) { p.AdminUp = i.AdminUp; p.OperUp = i.OperUp; }
                if (i.Counters is { } c && ScanResultWriter.UpdateRates(p, c, (h.UptimeSeconds ?? 0) * 100, now)) samples.Add((p, now));
            }
        }
        await db.SaveChangesAsync(ct);
        ScanResultWriter.AddSamples(db, sw, h, samples, now);
        await db.SaveChangesAsync(ct);
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-Math.Max(7, opt.Value.MetricsRetentionDays));
            var a = await db.SwitchMetrics.Where(x => x.At < cutoff).ExecuteDeleteAsync(ct);
            var b = await db.PortMetrics.Where(x => x.At < cutoff).ExecuteDeleteAsync(ct);
            var live = db.Switches.Select(s => s.Id);
            var c = await db.PortMetrics.Where(x => !live.Contains(x.SwitchId)).ExecuteDeleteAsync(ct);   // samples of deleted switches
            if (a + b + c > 0) log.LogInformation("MetricsPruned: {Switch} switch and {Port} port samples removed", a, b + c);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Pruning old metrics failed"); }
    }
}
