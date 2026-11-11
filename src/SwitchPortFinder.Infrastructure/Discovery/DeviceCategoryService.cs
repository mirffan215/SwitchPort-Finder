using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Discovery;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Discovery;

/// <summary>What the rules look at for one device.</summary>
public sealed record CategoryContext(string? Vendor, string Hostname, int? Vlan, string? PortDescription, string NeighborText, NeighborCaps NeighborCaps);

/// <summary>Classifies devices (access point, IP phone, ...) with editable rules. Manual categories always win.</summary>
public class DeviceCategoryService(AppDbContext db, ILogger<DeviceCategoryService> log)
{
    private static bool Has(string? hay, string? needle) => !string.IsNullOrEmpty(needle) && hay is not null && hay.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Pure: the first matching rule by priority, or (Unclassified, "").</summary>
    public static (DeviceCategory Category, string Reason) Evaluate(IEnumerable<CategoryRule> rules, CategoryContext c)
    {
        foreach (var r in rules.Where(r => r.Enabled).OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var any = false; var ok = true;
            void Need(bool conditionSet, bool matches) { if (!conditionSet) return; any = true; ok &= matches; }
            Need(!string.IsNullOrWhiteSpace(r.VendorContains), Has(c.Vendor, r.VendorContains));
            Need(!string.IsNullOrWhiteSpace(r.HostnameStartsWith), c.Hostname.StartsWith(r.HostnameStartsWith ?? "", StringComparison.OrdinalIgnoreCase));
            Need(!string.IsNullOrWhiteSpace(r.HostnameContains), Has(c.Hostname, r.HostnameContains));
            Need(!string.IsNullOrWhiteSpace(r.PortDescriptionContains), Has(c.PortDescription, r.PortDescriptionContains));
            Need(!string.IsNullOrWhiteSpace(r.NeighborContains), Has(c.NeighborText, r.NeighborContains));
            Need(!string.IsNullOrWhiteSpace(r.NeighborCapability), Enum.TryParse<NeighborCaps>(r.NeighborCapability, true, out var cap) && cap != NeighborCaps.None && c.NeighborCaps.HasFlag(cap));
            Need(r.Vlan is not null, r.Vlan == c.Vlan);
            if (any && ok) return (r.Category, r.Name);
        }
        return (DeviceCategory.Unclassified, "");
    }

    public static string? RuleProblem(CategoryRuleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length > 150) return "Give the rule a name.";
        if (!Enum.TryParse<DeviceCategory>(r.Category, true, out var cat) || cat == DeviceCategory.Unclassified) return "Choose a category.";
        if (r.Priority is < 1 or > 10_000) return "Priority must be 1 to 10000 (lower runs first).";
        string?[] texts = [r.VendorContains, r.HostnameStartsWith, r.HostnameContains, r.PortDescriptionContains, r.NeighborContains];
        if (texts.Any(t => t is { Length: > 100 })) return "Text conditions are limited to 100 characters.";
        if (!string.IsNullOrWhiteSpace(r.NeighborCapability) && !(Enum.TryParse<NeighborCaps>(r.NeighborCapability, true, out var cp) && cp is NeighborCaps.WlanAp or NeighborCaps.Telephone or NeighborCaps.Router or NeighborCaps.Bridge)) return "Neighbor capability must be WlanAp, Telephone, Bridge or Router.";
        if (r.Vlan is < 1 or > 4094) return "VLAN must be 1 to 4094.";
        if (texts.All(string.IsNullOrWhiteSpace) && string.IsNullOrWhiteSpace(r.NeighborCapability) && r.Vlan is null) return "Fill in at least one condition.";
        return null;
    }

    // ------------------------------------------------------------------ rules
    private static string? Nz(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static CategoryRuleDto ToDto(CategoryRule r) => new(r.Id, r.Name, r.Category.ToString(), r.Enabled, r.Priority, r.VendorContains, r.HostnameStartsWith, r.HostnameContains, r.PortDescriptionContains, r.NeighborContains, r.NeighborCapability, r.Vlan);
    private static void Apply(CategoryRule e, CategoryRuleRequest r)
    {
        e.Name = r.Name.Trim(); e.Category = Enum.Parse<DeviceCategory>(r.Category, true); e.Enabled = r.Enabled; e.Priority = r.Priority;
        e.VendorContains = Nz(r.VendorContains); e.HostnameStartsWith = Nz(r.HostnameStartsWith); e.HostnameContains = Nz(r.HostnameContains); e.PortDescriptionContains = Nz(r.PortDescriptionContains);
        e.NeighborContains = Nz(r.NeighborContains); e.NeighborCapability = Nz(r.NeighborCapability) is { } c ? Enum.Parse<NeighborCaps>(c, true).ToString() : null; e.Vlan = r.Vlan;
    }

    public async Task<List<CategoryRuleDto>> RulesAsync(CancellationToken ct) => (await db.CategoryRules.AsNoTracking().OrderBy(r => r.Priority).ThenBy(r => r.Id).ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<CategoryRuleDto> CreateAsync(CategoryRuleRequest r, CancellationToken ct)
    {
        if (RuleProblem(r) is { } p) throw new ValidationFailedException("rule", p);
        if (await db.CategoryRules.CountAsync(ct) >= 200) throw new ConflictException("Too many rules.");
        var e = new CategoryRule(); Apply(e, r); db.CategoryRules.Add(e); await db.SaveChangesAsync(ct); await ReapplyAsync(ct); return ToDto(e);
    }

    public async Task<CategoryRuleDto> UpdateAsync(int id, CategoryRuleRequest r, CancellationToken ct)
    {
        if (RuleProblem(r) is { } p) throw new ValidationFailedException("rule", p);
        var e = await db.CategoryRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Rule not found."); Apply(e, r); await db.SaveChangesAsync(ct); await ReapplyAsync(ct); return ToDto(e);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var e = await db.CategoryRules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Rule not found."); db.CategoryRules.Remove(e); await db.SaveChangesAsync(ct); await ReapplyAsync(ct);
    }

    /// <summary>Default rules, created once. They describe common Cisco / Grandstream / Ruijie setups and are meant to be edited (VLAN 50 etc.).</summary>
    public static List<CategoryRule> DefaultRules()
    {
        CategoryRule R(string n, DeviceCategory c, int pr, string? vendor = null, string? hostStarts = null, string? hostHas = null, string? neighbor = null, string? cap = null, int? vlan = null) =>
            new() { Name = n, Category = c, Priority = pr, VendorContains = vendor, HostnameStartsWith = hostStarts, HostnameContains = hostHas, NeighborContains = neighbor, NeighborCapability = cap, Vlan = vlan };
        return
        [
            R("Announces itself as a wireless access point (LLDP)", DeviceCategory.AccessPoint, 10, cap: "WlanAp"),
            R("Announces itself as a telephone (LLDP)", DeviceCategory.IpPhone, 11, cap: "Telephone"),
            R("Cisco access point model name in CDP/LLDP (AIR-, Aironet)", DeviceCategory.AccessPoint, 20, neighbor: "AIR-"),
            R("Neighbor description says access point", DeviceCategory.AccessPoint, 21, neighbor: "Access Point"),
            R("Neighbor description says IP phone", DeviceCategory.IpPhone, 22, neighbor: "IP Phone"),
            R("Cisco phone host name (SEP + MAC)", DeviceCategory.IpPhone, 30, vendor: "Cisco", hostStarts: "SEP"),
            R("Host name starts with AP-", DeviceCategory.AccessPoint, 31, hostStarts: "AP-"),
            R("Host name starts with WAP", DeviceCategory.AccessPoint, 32, hostStarts: "WAP"),
            R("Grandstream (GWN access points)", DeviceCategory.AccessPoint, 40, vendor: "Grandstream"),
            R("Ruijie in the access-point VLAN", DeviceCategory.AccessPoint, 41, vendor: "Ruijie", vlan: 50),
            R("Anything in VLAN 50 (the access-point VLAN, mostly)", DeviceCategory.AccessPoint, 90, vlan: 50),
        ];
    }

    public async Task EnsureDefaultRulesAsync(CancellationToken ct)
    {
        if (await db.CategoryRules.AnyAsync(ct)) return;
        db.CategoryRules.AddRange(DefaultRules());
        await db.SaveChangesAsync(ct);
    }

    // ------------------------------------------------------------------ applying
    /// <summary>(Re)classifies the given devices (all non-manual ones when null). A manual category is never touched.</summary>
    public async Task<int> ApplyAsync(IEnumerable<Device>? devices, CancellationToken ct)
    {
        var rules = await db.CategoryRules.AsNoTracking().Where(r => r.Enabled).ToListAsync(ct);
        var list = (devices ?? await db.Devices.Where(d => !d.CategoryManual).ToListAsync(ct)).Where(d => !d.CategoryManual).ToList();
        if (list.Count == 0) return 0;
        var portIds = list.Where(d => d.PortId != null).Select(d => d.PortId!.Value).Distinct().ToList();
        var ports = new Dictionary<int, string?>(); var neighbors = new Dictionary<int, (string Text, NeighborCaps Caps)>();
        foreach (var chunk in portIds.Chunk(800))
        {
            foreach (var p in await db.SwitchPorts.AsNoTracking().Where(p => chunk.Contains(p.Id)).Select(p => new { p.Id, Text = (p.Alias ?? "") + " " + (p.Descr ?? "") }).ToListAsync(ct)) ports[p.Id] = p.Text;
            foreach (var g in (await db.Neighbors.AsNoTracking().Where(n => chunk.Contains(n.PortId) && n.Protocol != NeighborProtocol.Inferred).Select(n => new { n.PortId, n.SysName, n.SysDescr, n.Platform, n.Caps }).ToListAsync(ct)).GroupBy(n => n.PortId))
                neighbors[g.Key] = (string.Join(" | ", g.Select(n => $"{n.SysName} {n.Platform} {n.SysDescr}")), g.Aggregate(NeighborCaps.None, (a, n) => a | n.Caps));
        }
        var changed = 0;
        foreach (var d in list)
        {
            var (nText, nCaps) = d.PortId is { } pid && neighbors.TryGetValue(pid, out var nb) ? nb : ("", NeighborCaps.None);
            var ctx = new CategoryContext(VendorText.Of(d.Vendor, d.Mac), d.HostnameSource == HostnameSource.None ? "" : d.Hostname, d.Vlan, d.PortId is { } p2 ? ports.GetValueOrDefault(p2) : null, nText, nCaps);
            var (cat, reason) = Evaluate(rules, ctx);
            if (d.Category != cat || d.CategoryReason != reason) changed++;
            d.Category = cat; d.CategoryReason = reason;
        }
        await db.SaveChangesAsync(ct);
        return changed;
    }

    public async Task<int> ReapplyAsync(CancellationToken ct)
    {
        var n = await ApplyAsync(null, ct); log.LogInformation("CategoriesReapplied: {Changed} device(s) changed category", n); return n;
    }
}
