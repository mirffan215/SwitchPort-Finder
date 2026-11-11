using Microsoft.EntityFrameworkCore;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Reports;

/// <summary>Names of the switches picked in a report (for the Excel "Definition" sheet).</summary>
public class SwitchNameLookup(AppDbContext db)
{
    public async Task<List<string>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
    {
        var list = ids.Distinct().Take(500).ToList();
        return await db.Switches.AsNoTracking().Where(s => list.Contains(s.Id)).OrderBy(s => s.Name).Select(s => s.Name).ToListAsync(ct);
    }
}
