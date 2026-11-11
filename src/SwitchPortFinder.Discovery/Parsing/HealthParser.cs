using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Discovery.Parsing;

/// <summary>Turns CPU / memory table walks into percentages. Pure functions: every vendor shape is unit-tested.</summary>
public static class HealthParser
{
    private static double Clamp(double v) => Math.Max(0, Math.Min(100, v));

    /// <summary>Highest per-CPU/per-card value of a "% busy" column (the busiest processor is what matters), or null when the column is empty.</summary>
    public static double? MaxColumn(IEnumerable<SnmpVarBind> walk, string entryRoot, int column)
    {
        var v = OidTable.Cells(walk, entryRoot).Where(c => c.Column == column && !c.Value.IsMissing).Select(c => (double)c.Value.Number).ToList();
        return v.Count == 0 ? null : Clamp(v.Max());
    }

    /// <summary>HOST-RESOURCES-MIB: average of hrProcessorLoad over all processors.</summary>
    public static double? HrCpu(IEnumerable<SnmpVarBind> walk)
    {
        var v = OidTable.Cells(walk, Oids.HrProcessorEntry).Where(c => c.Column == 2 && !c.Value.IsMissing).Select(c => (double)c.Value.Number).ToList();
        return v.Count == 0 ? null : Clamp(v.Average());
    }

    /// <summary>HOST-RESOURCES-MIB: used/size of the row whose hrStorageType is hrStorageRam, in allocation units.</summary>
    public static double? HrMemory(IEnumerable<SnmpVarBind> walk)
    {
        foreach (var row in OidTable.Cells(walk, Oids.HrStorageEntry).GroupBy(c => c.Index.Length > 0 ? c.Index[0] : -1))
        {
            var col = row.ToDictionary(c => c.Column, c => c.Value, EqualityComparer<int>.Default);
            if (!col.TryGetValue(2, out var type) || type.Text?.TrimStart('.') != Oids.HrStorageRam) continue;
            if (col.TryGetValue(5, out var size) && col.TryGetValue(6, out var used) && size.Number > 0) return Clamp(used.Number * 100.0 / size.Number);
        }
        return null;
    }

    /// <summary>CISCO-MEMORY-POOL-MIB: the pool with the most memory (the main "Processor" pool): used / (used + free).</summary>
    public static double? CiscoMemory(IEnumerable<SnmpVarBind> walk)
    {
        double? best = null; double bestTotal = 0;
        foreach (var row in OidTable.Cells(walk, Oids.CiscoMemoryPoolEntry).GroupBy(c => c.Index.Length > 0 ? c.Index[0] : -1))
        {
            var col = row.ToDictionary(c => c.Column, c => c.Value);
            if (!col.TryGetValue(5, out var used) || !col.TryGetValue(6, out var free)) continue;
            var total = (double)used.Number + free.Number;
            if (total > bestTotal) { bestTotal = total; best = Clamp(used.Number * 100.0 / total); }
        }
        return best;
    }
}
