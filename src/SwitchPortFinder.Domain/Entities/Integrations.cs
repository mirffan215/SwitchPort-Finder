namespace SwitchPortFinder.Domain.Entities;

/// <summary>One optional integration (kind "dhcp", "ad" or "intune"). Off until an administrator turns it on. Secrets are stored encrypted and never returned.</summary>
public class Integration
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; }
    /// <summary>Non-secret settings as JSON (server name, base DN, tenant id, ...).</summary>
    public string ConfigJson { get; set; } = "{}";
    public string? EncryptedSecret { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool? LastOk { get; set; }
    public string? LastMessage { get; set; }
    public int LastMatched { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Company name, link and logo shown in the header and on the sign-in page. One row (Id = 1).</summary>
public class Branding
{
    public int Id { get; set; } = 1;
    public string? CompanyName { get; set; }
    public string? LinkUrl { get; set; }
    public byte[]? LogoBytes { get; set; }
    public string? LogoContentType { get; set; }
    public DateTime UpdatedAt { get; set; }
}
