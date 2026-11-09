using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>Consumes the scan queue with bounded parallelism, recovers jobs after a restart, and optionally schedules scan-all.</summary>
public sealed class DiscoveryWorker(IServiceScopeFactory scopes, ScanQueue queue, IOptions<DiscoveryOptions> opt, ILogger<DiscoveryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Recover(stop);
        if (opt.Value.ScanIntervalMinutes > 0) _ = Schedule(stop);
        using var gate = new SemaphoreSlim(Math.Max(1, opt.Value.MaxParallelScans));
        var running = new List<Task>();
        try
        {
            await foreach (var jobId in queue.Reader.ReadAllAsync(stop))
            {
                await gate.WaitAsync(stop);
                var t = Task.Run(async () =>
                {
                    try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<DiscoveryJobRunner>().RunAsync(jobId, stop); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { log.LogError(ex, "Job {Job} crashed", jobId); }
                    finally { gate.Release(); }
                }, CancellationToken.None);
                lock (running) { running.RemoveAll(x => x.IsCompleted); running.Add(t); }
            }
        }
        catch (OperationCanceledException) { }
        Task[] pending; lock (running) pending = running.ToArray();
        await Task.WhenAll(pending);
    }

    /// <summary>Jobs left Running by a crash are failed; Queued jobs are re-queued.</summary>
    private async Task Recover(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var j in await db.DiscoveryJobs.Where(j => j.Status == JobStatus.Running).ToListAsync(ct))
            { j.Status = JobStatus.Failed; j.CompletedAt = DateTime.UtcNow; j.Summary = "Interrupted by a server restart."; }
            await db.SaveChangesAsync(ct);
            foreach (var id in await db.DiscoveryJobs.Where(j => j.Status == JobStatus.Queued).OrderBy(j => j.Id).Select(j => j.Id).ToListAsync(ct))
                await queue.EnqueueAsync(id, ct);
        }
        catch (Exception ex) { log.LogWarning(ex, "Could not recover discovery jobs (database not ready?)"); }
    }

    private async Task Schedule(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(opt.Value.ScanIntervalMinutes));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using var scope = scopes.CreateScope();
                var n = (await scope.ServiceProvider.GetRequiredService<DiscoveryService>().EnqueueAllAsync(ct)).Count;
                log.LogInformation("Scheduled scan queued for {Count} switches", n);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.LogError(ex, "Scheduled scan loop stopped"); }
    }
}
