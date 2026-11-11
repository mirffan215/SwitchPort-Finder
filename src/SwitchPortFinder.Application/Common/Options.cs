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
    /// <summary>Send Strict-Transport-Security over HTTPS. Leave off with a self-signed certificate (browsers cannot override HSTS errors).</summary>
    public bool Hsts { get; set; }
}

/// <summary>Refuses SNMP and mail-server targets that can never be a switch (and would make the portal a probe for the server itself or for cloud metadata).</summary>
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
        // Link-local (169.254.0.0/16, fe80::/10) is where cloud metadata services live (169.254.169.254) and is never a managed device or mail server.
        if (!allowLoopback && (b.Length == 4 && b[0] == 169 && b[1] == 254 || b.Length == 16 && b[0] == 0xfe && (b[1] & 0xc0) == 0x80)) return "Link-local addresses (169.254.x.x, fe80::) are not allowed.";
        return null;
    }
}

public class AuthOptions
{
    public const string Section = "Authentication";
    /// <summary>"None" (default, as specified for V1: no login) or "Local" (accounts stored in the database, cookie sessions).</summary>
    public string Mode { get; set; } = "None";
    public bool IsLocal => string.Equals(Mode, "Local", StringComparison.OrdinalIgnoreCase);
    public int SessionMinutes { get; set; } = 480;
    public int MaxFailedLogins { get; set; } = 5;
    public int LockoutSeconds { get; set; } = 900;
    public int LoginsPerMinute { get; set; } = 10;
    /// <summary>Administrators must enrol an authenticator app (TOTP) before they can use the portal. Recommended; viewers may enrol voluntarily.</summary>
    public bool RequireMfaForAdmins { get; set; } = true;
    public string MfaIssuer { get; set; } = "SwitchPort Finder";
    /// <summary>Used ONLY when Local mode is on and no user exists yet. Change the password immediately after the first login.</summary>
    public BootstrapAdminOptions BootstrapAdmin { get; set; } = new();
}

public class BootstrapAdminOptions { public string Username { get; set; } = "admin"; public string? Password { get; set; } }
