using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Discovery.Parsing;

/// <summary>A decoded table cell: column number, row index sub-identifiers and the value.</summary>
public readonly record struct Cell(int Column, int[] Index, SnmpValue Value);

public static class OidTable
{
    /// <summary>Splits varbinds under <paramref name="root"/> into (column, index[], value). Rows outside the subtree or with exception values are dropped.</summary>
    public static List<Cell> Cells(IEnumerable<SnmpVarBind> vbs, string root)
    {
        var prefix = root.TrimStart('.') + ".";
        var list = new List<Cell>();
        foreach (var vb in vbs)
        {
            if (vb.Value.IsMissing && vb.Value.Kind != SnmpValueKind.Null) continue;
            if (!vb.Oid.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var parts = vb.Oid[prefix.Length..].Split('.');
            if (parts.Length < 2) continue;
            var nums = new int[parts.Length];
            var ok = true;
            for (var i = 0; i < parts.Length && ok; i++) ok = int.TryParse(parts[i], out nums[i]);
            if (!ok) continue;
            list.Add(new Cell(nums[0], nums[1..], vb.Value));
        }
        return list;
    }

    /// <summary>Last sub-identifier of each row under a single-column root (e.g. dot1dBasePortIfIndex.&lt;bridgePort&gt;).</summary>
    public static IEnumerable<(int Index, SnmpValue Value)> Column(IEnumerable<SnmpVarBind> vbs, string columnRoot)
    {
        var prefix = columnRoot.TrimStart('.') + ".";
        foreach (var vb in vbs)
        {
            if (vb.Value.IsMissing || !vb.Oid.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (int.TryParse(vb.Oid[prefix.Length..].Split('.')[^1], out var idx)) yield return (idx, vb.Value);
        }
    }
}
