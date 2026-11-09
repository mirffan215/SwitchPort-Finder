namespace SwitchPortFinder.Domain;

/// <summary>MAC handling. Canonical storage form is 12 uppercase hex chars; display form is AA:BB:CC:DD:EE:FF.</summary>
public static class MacAddress
{
    /// <summary>Accepts AA:BB:.., AA-BB-.., AABB.CCDD.EEFF, AABBCCDDEEFF in any case. Returns null if not exactly 12 hex digits.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        Span<char> buf = stackalloc char[12]; var n = 0;
        foreach (var ch in input.Trim())
        {
            if (ch is ':' or '-' or '.' or ' ') continue;
            if (!Uri.IsHexDigit(ch) || n == 12) return null;
            buf[n++] = char.ToUpperInvariant(ch);
        }
        return n == 12 ? new string(buf) : null;
    }

    public static string? FromBytes(ReadOnlySpan<byte> b) => b.Length == 6 ? Convert.ToHexString(b) : null;

    public static string Format(string normalized) =>
        normalized.Length == 12 ? string.Join(':', Enumerable.Range(0, 6).Select(i => normalized.Substring(i * 2, 2))) : normalized;

    /// <summary>Partial search key: hex digits only, at least 4. Returns null when it cannot be a (partial) MAC.</summary>
    public static string? PartialKey(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in input.Trim())
        {
            if (ch is ':' or '-' or '.') continue;
            if (!Uri.IsHexDigit(ch)) return null;
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.Length is >= 4 and <= 12 ? sb.ToString() : null;
    }

    public static bool IsMulticastOrBroadcast(string normalized) =>
        normalized.Length == 12 && (Convert.ToByte(normalized[..2], 16) & 1) == 1;

    public static bool IsZero(string normalized) => normalized == "000000000000";
}
