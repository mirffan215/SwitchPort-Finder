using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>Executes one queued DiscoveryJob: System → Interfaces → VLANs → MAC table → ARP → correlation → DB update.</summary>
public class DiscoveryJobRunner(
    AppDbContext db, ISwitchRepository switches, SwitchService switchService, ISwitchScanner scanner,
    ScanResultWriter writer, ILogger<DiscoveryJobRunner> log)
{
    private static readonly string[] AllSteps = [Steps.System, Steps.Interfaces, Steps.Vlans, Steps.MacTable, Steps.Arp, Steps.Lldp, Steps.Cdp, Steps.Correlation];

    public async Task RunAsync(int jobId, CancellationToken ct)
    {
        var job = await db.DiscoveryJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != JobStatus.Queued) return;
        var sw = await switches.GetAsync(job.SwitchId, ct);
        var steps = AllSteps.Select(n => new StepResult(n, StepState.Pending)).ToList();

        async Task Save() { job.StepsJson = JsonSerializer.Serialize(steps); await db.SaveChangesAsync(CancellationToken.None); }
        async Task SetStep(StepResult s) { var i = steps.FindIndex(x => x.Name == s.Name); if (i >= 0) steps[i] = s; else steps.Add(s); await Save(); }

        job.Status = JobStatus.Running; job.StartedAt = DateTime.UtcNow;
        if (sw is null) { await Finish(job, JobStatus.Failed, "Switch no longer exists.", steps); return; }
        await Save();
        log.LogInformation("DiscoveryStarted for {Switch} ({Ip}) job {Job}", sw.Name, sw.ManagementIp, job.Id);

        try
        {
            var creds = switchService.ResolveCredentials(sw);
            if (creds is null) { await Finish(job, JobStatus.Failed, "No SNMP credentials configured.", steps); return; }
            var result = await scanner.ScanAsync(new ScanRequest(sw.ManagementIp, creds, sw.Manufacturer, sw.UseAsArpSource), SetStep, ct);
            foreach (var e in result.Errors) job.Errors.Add(new DiscoveryError { Step = e.Step, Code = e.Code, Message = Trunc(e.Message, 1000), CreatedAt = DateTime.UtcNow });

            if (!result.Reachable)
            {
                sw.Status = SwitchStatus.Unreachable; sw.LastScanAt = DateTime.UtcNow;
                db.SwitchMetrics.Add(new SwitchMetricSample { SwitchId = sw.Id, At = DateTime.UtcNow, Reachable = false });   // downtime is computed from these
                for (var i = 0; i < steps.Count; i++) if (steps[i].State is StepState.Pending) steps[i] = steps[i] with { State = StepState.Skipped };
                log.LogWarning("SwitchUnreachable: {Switch} ({Ip})", sw.Name, sw.ManagementIp);
                job.ErrorCount = job.Errors.Count;
                await Finish(job, JobStatus.Failed, result.Errors.FirstOrDefault()?.Message ?? "Switch unreachable.", steps);
                return;
            }

            await SetStep(new StepResult(Steps.Correlation, StepState.Running));
            var counts = await writer.WriteAsync(sw, result, ct);
            await SetStep(new StepResult(Steps.Correlation, StepState.Ok));
            job.PortsFound = counts.Ports; job.DevicesFound = counts.Devices; job.NeighborsFound = counts.Neighbors; job.ErrorCount = job.Errors.Count;
            var warn = steps.Any(s => s.State is StepState.Warning or StepState.Failed);
            log.LogInformation("DiscoveryCompleted for {Switch}: {Ports} ports, {Devices} devices, {Errors} issues", sw.Name, counts.Ports, counts.Devices, job.Errors.Count);
            await Finish(job, warn ? JobStatus.CompletedWithWarnings : JobStatus.Completed, "Discovery completed.", steps);
        }
        catch (OperationCanceledException)
        {
            await Finish(job, JobStatus.Failed, "Interrupted (server shutting down).", steps);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Discovery job {Job} failed unexpectedly", job.Id);
            job.Errors.Add(new DiscoveryError { Step = "Job", Code = "Unexpected", Message = "Unexpected error. See server log.", CreatedAt = DateTime.UtcNow });
            job.ErrorCount = job.Errors.Count;
            await Finish(job, JobStatus.Failed, "Discovery failed unexpectedly.", steps);
        }
    }

    private async Task Finish(DiscoveryJob job, JobStatus status, string summary, List<StepResult> steps)
    {
        job.Status = status; job.CompletedAt = DateTime.UtcNow; job.Summary = Trunc(summary, 500);
        job.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
