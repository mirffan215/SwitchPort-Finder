using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Domain.Entities;

/// <summary>
/// Credential material. Everything secret (community, username, passwords) lives in
/// <see cref="EncryptedSecrets"/>, which is produced by ICredentialProtector. Only
/// non-secret protocol choices are stored in the clear.
/// </summary>
public class SwitchCredential
{
    public int Id { get; set; }
    public int SwitchId { get; set; }
    public Switch? Switch { get; set; }
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public SnmpAuthProtocol AuthProtocol { get; set; }
    public SnmpPrivProtocol PrivProtocol { get; set; }
    public string? ContextName { get; set; }
    public string EncryptedSecrets { get; set; } = string.Empty;
}

public class CredentialProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public SnmpAuthProtocol AuthProtocol { get; set; }
    public SnmpPrivProtocol PrivProtocol { get; set; }
    public string? ContextName { get; set; }
    public string EncryptedSecrets { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Switch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ManagementIp { get; set; } = string.Empty;
    public Manufacturer Manufacturer { get; set; } = Manufacturer.AutoDetect;
    /// <summary>Free-text vendor name when Manufacturer = OtherGeneric.</summary>
    public string? ManufacturerOther { get; set; }
    /// <summary>Vendor derived from sysObjectID on the last successful SNMP contact.</summary>
    public Manufacturer? DetectedManufacturer { get; set; }
    public string? Model { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public bool UseAsArpSource { get; set; }
    /// <summary>Set by discovery when the device routes (ipForwarding=1) and has an ARP table.</summary>
    public bool ArpAutoDetected { get; set; }
    public string? SerialNumber { get; set; }
    public string? SysObjectId { get; set; }
    public string? SysName { get; set; }
    /// <summary>sysUpTime at the last successful scan, in seconds (SNMP TimeTicks wrap after ~497 days, so very long uptimes read low), and when it was read.</summary>
    public long? UptimeSeconds { get; set; }
    /// <summary>Latest CPU / memory / SNMP round-trip latency (null = the switch does not expose it).</summary>
    public double? CpuPct { get; set; }
    public double? MemPct { get; set; }
    public int? LatencyMs { get; set; }
    public DateTime? HealthAt { get; set; }
    public DateTime? UptimeAt { get; set; }
    public string? SysDescr { get; set; }
    public SwitchStatus Status { get; set; } = SwitchStatus.Unknown;
    public DateTime? LastScanAt { get; set; }
    public int? CredentialProfileId { get; set; }
    public CredentialProfile? CredentialProfile { get; set; }
    public SwitchCredential? Credential { get; set; }
    public int? ImportBatchId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ImportBatch
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public ImportMode Mode { get; set; }
    public string ImportedBy { get; set; } = "system";
    public DateTime CreatedAt { get; set; }
    public int TotalRows { get; set; }
    public int CreatedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public int ErrorCount { get; set; }
}
