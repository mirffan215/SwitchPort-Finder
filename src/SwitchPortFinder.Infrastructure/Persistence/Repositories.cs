using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Infrastructure.Persistence;

public class SwitchRepository(AppDbContext db) : ISwitchRepository
{
    private IQueryable<Switch> Full => db.Switches.Include(s => s.Credential).Include(s => s.CredentialProfile);
    public Task<List<Switch>> ListAsync(CancellationToken ct) => Full.AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct);
    public Task<Switch?> GetAsync(int id, CancellationToken ct) => Full.FirstOrDefaultAsync(s => s.Id == id, ct);
    public Task<Switch?> GetByIpAsync(string ip, CancellationToken ct) => Full.FirstOrDefaultAsync(s => s.ManagementIp == ip, ct);

    public Task<Dictionary<int, int>> PortCountsAsync(CancellationToken ct) =>
        db.SwitchPorts.GroupBy(p => p.SwitchId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.N, ct);

    public async Task<Dictionary<string, Switch>> GetByIpsAsync(IEnumerable<string> ips, CancellationToken ct)
    {
        var result = new Dictionary<string, Switch>();
        foreach (var chunk in ips.Distinct().Chunk(500))   // keep IN-lists within SQL Server parameter limits
            foreach (var s in await Full.Where(s => chunk.Contains(s.ManagementIp)).ToListAsync(ct))
                result[s.ManagementIp] = s;
        return result;
    }

    public async Task AddAsync(Switch sw, CancellationToken ct) => await db.Switches.AddAsync(sw, ct);
    /// <summary>Devices located on this switch keep their record but lose the location (SQL Server forbids a second cascade path).</summary>
    public async Task DeleteAsync(Switch sw, CancellationToken ct)
    {
        await db.Devices.Where(d => d.SwitchId == sw.Id || d.IpSourceSwitchId == sw.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.PortId, (int?)null).SetProperty(d => d.SwitchId, (int?)null).SetProperty(d => d.IpSourceSwitchId, (int?)null), ct);
        db.Switches.Remove(sw);
    }
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public class CredentialProfileRepository(AppDbContext db) : ICredentialProfileRepository
{
    public Task<List<CredentialProfile>> ListAsync(CancellationToken ct) => db.CredentialProfiles.OrderBy(p => p.Name).ToListAsync(ct);
    public Task<CredentialProfile?> GetAsync(int id, CancellationToken ct) => db.CredentialProfiles.FirstOrDefaultAsync(p => p.Id == id, ct);
    public Task<CredentialProfile?> GetByNameAsync(string name, CancellationToken ct) => db.CredentialProfiles.FirstOrDefaultAsync(p => p.Name == name, ct);
    public Task<int> CountSwitchesUsingAsync(int profileId, CancellationToken ct) => db.Switches.CountAsync(s => s.CredentialProfileId == profileId, ct);
    public async Task AddAsync(CredentialProfile p, CancellationToken ct) => await db.CredentialProfiles.AddAsync(p, ct);
    public Task DeleteAsync(CredentialProfile p, CancellationToken ct) { db.CredentialProfiles.Remove(p); return Task.CompletedTask; }
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}

public class ImportBatchRepository(AppDbContext db) : IImportBatchRepository
{
    public async Task AddAsync(ImportBatch batch, CancellationToken ct) => await db.ImportBatches.AddAsync(batch, ct);
    public Task SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}
