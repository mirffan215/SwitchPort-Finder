namespace SwitchPortFinder.Domain;

/// <summary>
/// MAC address -> manufacturer, from the IEEE OUI registry embedded in this assembly (works offline).
/// A MAC identifies who MADE the network chip/device, not the product model. Randomised ("private") MACs - used by modern phones and
/// laptops for Wi-Fi privacy - have the locally-administered bit set and carry no manufacturer information at all.
/// </summary>
public static class OuiLookup
{
    public const string PrivateMac = "Private (randomized MAC)";
    private static readonly Lazy<Dictionary<string, string>> Table = new(Load);

    public static int Count => Table.Value.Count;

    /// <summary>Normalised 12-hex-digit MAC (see <see cref="MacAddress.Normalize"/>). Null when the manufacturer is unknown.</summary>
    public static string? Lookup(string? mac)
    {
        if (mac is null || mac.Length != 12 || !mac.All(Uri.IsHexDigit)) return null;
        var first = Convert.ToInt32(mac[..2], 16);
        if ((first & 0x01) != 0) return null;                        // multicast/group address: never a device
        if ((first & 0x02) != 0) return PrivateMac;                  // locally administered = randomised/virtual
        return Table.Value.TryGetValue(mac[..6].ToUpperInvariant(), out var v) ? v : null;
    }

    private static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(40_000, StringComparer.Ordinal);
        using var s = typeof(OuiLookup).Assembly.GetManifestResourceStream("SwitchPortFinder.Domain.oui.tsv");
        if (s is null) return d;
        using var r = new StreamReader(s);
        for (string? line; (line = r.ReadLine()) is not null;)
        {
            var t = line.IndexOf('\t');
            if (t == 6) d[line[..6]] = line[7..];
        }
        return d;
    }
}
