namespace SwitchPortFinder.Application.Common;

public class SnmpOptions
{
    public const string Section = "Snmp";
    public int TimeoutMs { get; set; } = 5000;
    public int Retries { get; set; } = 1;
    public int Port { get; set; } = 161;
    public int MaxRepetitions { get; set; } = 10;
}

public class ImportOptions
{
    public const string Section = "Import";
    public int MaxRows { get; set; } = 10000;
    public int MaxFileBytes { get; set; } = 10 * 1024 * 1024;
    public int PreviewTtlMinutes { get; set; } = 30;
}

public class ExportOptions
{
    public const string Section = "Export";
    /// <summary>Largest device export allowed in one request (keeps memory bounded).</summary>
    public int MaxDeviceRows { get; set; } = 250_000;
}

public class SecurityOptions
{
    public const string Section = "Security";
    /// <summary>Loopback addresses are never a real switch; allowed only for local development/testing.</summary>
    public bool AllowLoopbackTargets { get; set; }
    /// <summary>Extra origins (scheme://host[:port]) allowed to make state-changing requests, e.g. when behind a reverse proxy that rewrites Host.</summary>
    public string[] TrustedOrigins { get; set; } = [];
}

/// <summary>Refuses SNMP targets that can never be a switch (and would make the portal a probe for the server itself).</summary>
public static class TargetPolicy
{
    public static string? Check(string ip, bool allowLoopback)
    {
        if (!System.Net.IPAddress.TryParse(ip, out var a)) return "Enter a valid IPv4 or IPv6 address.";
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        var b = a.GetAddressBytes();
        if (a.Equals(System.Net.IPAddress.Any) || a.Equals(System.Net.IPAddress.IPv6Any)) return "0.0.0.0 / :: is not a valid switch address.";
        if (a.Equals(System.Net.IPAddress.Broadcast)) return "The broadcast address is not a valid switch address.";
        if (b.Length == 4 && b[0] >= 224 && b[0] <= 239 || b.Length == 16 && b[0] == 0xff) return "Multicast addresses are not valid switch addresses.";
        if (System.Net.IPAddress.IsLoopback(a) && !allowLoopback) return "Loopback addresses cannot be switches. Use the switch's management IP.";
        return null;
    }
}
