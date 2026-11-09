using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

public class DiscoveryService(AppDbContext db, IScanQueue queue)
{
    /// <summary>Queues a scan and returns immediately. If one is already queued/running for the switch, that job is returned.</summary>
    public async Task<int> EnqueueAsync(int switchId, CancellationToken ct)
    {
        if (!await db.Switches.AnyAsync(s => s.Id == switchId, ct)) throw new NotFoundException($"Switch {switchId} not found.");
        var active = await db.DiscoveryJobs.Where(j => j.SwitchId == switchId && (j.Status == JobStatus.Queued || j.Status == JobStatus.Running))
            .OrderByDescending(j => j.Id).Select(j => (int?)j.Id).FirstOrDefaultAsync(ct);
        if (active is { } id) return id;
        var job = new DiscoveryJob { SwitchId = switchId, Status = JobStatus.Queued, QueuedAt = DateTime.UtcNow };
        db.DiscoveryJobs.Add(job);
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(job.Id, ct);
        return job.Id;
    }

    public async Task<List<int>> EnqueueAllAsync(CancellationToken ct, IEnumerable<int>? only = null)
    {
        var ids = only?.ToList() ?? await db.Switches.Select(s => s.Id).ToListAsync(ct);
        var jobs = new List<int>();
        foreach (var id in ids) jobs.Add(await EnqueueAsync(id, ct));
        return jobs;
    }

    public async Task<List<JobSummaryDto>> ListAsync(int take, int? switchId, CancellationToken ct)
    {
        var q = db.DiscoveryJobs.AsNoTracking().Include(j => j.Switch).AsQueryable();
        if (switchId is { } sid) q = q.Where(j => j.SwitchId == sid);
        return (await q.OrderByDescending(j => j.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)).Select(ToDto).ToList();
    }

    public async Task<JobDetailDto> GetAsync(int id, CancellationToken ct)
    {
        var j = await db.DiscoveryJobs.AsNoTracking().Include(x => x.Switch).Include(x => x.Errors).FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException($"Job {id} not found.");
        var steps = string.IsNullOrEmpty(j.StepsJson) ? [] : JsonSerializer.Deserialize<List<StepResult>>(j.StepsJson) ?? [];
        return new JobDetailDto(ToDto(j), steps, j.Errors.OrderBy(e => e.Id).Select(e => new JobErrorDto(e.Step, e.Code, e.Message, e.CreatedAt)).ToList());
    }

    private static JobSummaryDto ToDto(DiscoveryJob j) => new(
        j.Id, j.SwitchId, j.Switch?.Name ?? "", j.Status, j.QueuedAt, j.StartedAt, j.CompletedAt,
        j.StartedAt is { } s && j.CompletedAt is { } c ? (c - s).TotalSeconds : null,
        j.PortsFound, j.DevicesFound, j.NeighborsFound, j.ErrorCount, j.Summary);
}
