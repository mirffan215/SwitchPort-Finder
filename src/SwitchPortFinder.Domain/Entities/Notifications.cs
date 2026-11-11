namespace SwitchPortFinder.Domain.Entities;

public enum SmtpSecurity { None = 0, StartTls = 1, SslOnConnect = 2 }

/// <summary>What an alert rule watches. Port rules read the traffic history, switch rules the health history.</summary>
public enum AlertRuleType { UplinkUtilization = 0, PortUtilization = 1, SwitchUnreachable = 2, HighCpu = 3, HighMemory = 4, HighLatency = 5, PacketDrops = 6, PortErrors = 7, SwitchRestarted = 8 }

public enum AlertState { Firing = 0, Resolved = 1 }

/// <summary>The one mail-server configuration (row Id = 1). The password is encrypted with Data Protection and never returned by any API.</summary>
public class NotificationConfig
{
    public int Id { get; set; } = 1;
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string? Username { get; set; }
    public string? EncryptedPassword { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "SwitchPort Finder";
    /// <summary>Semicolon- or comma-separated.</summary>
    public string Recipients { get; set; } = "";
    /// <summary>Accept a self-signed / unknown-CA certificate of the mail server. Off by default: only for an internal relay you trust.</summary>
    public bool TrustServerCertificate { get; set; }
    /// <summary>Optional https://host:port used for the link in e-mails.</summary>
    public string? PublicBaseUrl { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AlertRule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public AlertRuleType Type { get; set; }
    public bool Enabled { get; set; } = true;
    /// <summary>% for utilization/CPU/memory, ms for latency, packets/s for drops and errors. Not used by unreachable/restart.</summary>
    public double Threshold { get; set; }
    /// <summary>The condition must hold for this long (0 = a single reading is enough). For SwitchUnreachable: minutes without an answer; for SwitchRestarted: how recent the restart must be.</summary>
    public int DurationMinutes { get; set; } = 10;
    /// <summary>Null = every switch.</summary>
    public int? SwitchId { get; set; }
    /// <summary>While the alert stays open, remind again after this many minutes (0 = never).</summary>
    public int CooldownMinutes { get; set; } = 240;
    public bool NotifyOnResolve { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class AlertEvent
{
    public long Id { get; set; }
    public int RuleId { get; set; }
    public AlertRule? Rule { get; set; }
    /// <summary>"switch:12" or "port:345": one open event per rule and subject.</summary>
    public string Subject { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public double? Value { get; set; }
    public AlertState State { get; set; }
    public DateTime FirstAt { get; set; }
    public DateTime LastAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public DateTime? LastNotifiedAt { get; set; }
    public int NotifyCount { get; set; }
    /// <summary>The "resolved" e-mail has been sent (or none is wanted).</summary>
    public bool ResolveNotified { get; set; }
}

/// <summary>Audit of every e-mail attempt (no body, no secrets).</summary>
public class NotificationLogEntry
{
    public long Id { get; set; }
    public DateTime At { get; set; }
    public string Recipients { get; set; } = "";
    public string Subject { get; set; } = "";
    public bool Success { get; set; }
    public string? Error { get; set; }
}
