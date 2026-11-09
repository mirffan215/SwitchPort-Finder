using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Switches;

/// <summary>Inbound credential data. Secret fields are write-only: they are never echoed back.</summary>
public class CredentialInput
{
    public SnmpVersion Version { get; set; } = SnmpVersion.V2c;
    public string? Community { get; set; }
    public string? Username { get; set; }
    public SnmpAuthProtocol AuthProtocol { get; set; }
    public string? AuthPassword { get; set; }
    public SnmpPrivProtocol PrivProtocol { get; set; }
    public string? PrivPassword { get; set; }
    public string? ContextName { get; set; }
    public override string ToString() => "CredentialInput(<redacted>)";
}

/// <summary>Outbound credential description. Contains no secret values, only whether they are set.</summary>
public record CredentialSummary(
    SnmpVersion Version, SnmpAuthProtocol AuthProtocol, SnmpPrivProtocol PrivProtocol, string? ContextName,
    bool HasCommunity, bool HasUsername, bool HasAuthPassword, bool HasPrivPassword);

public class SwitchRequest
{
    public string Name { get; set; } = string.Empty;
    public string ManagementIp { get; set; } = string.Empty;
    public Manufacturer Manufacturer { get; set; } = Manufacturer.AutoDetect;
    public string? ManufacturerOther { get; set; }
    public string? Model { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public bool UseAsArpSource { get; set; }
    public int? CredentialProfileId { get; set; }
    public CredentialInput? Credential { get; set; }
}

public record SwitchSummaryDto(
    int Id, string Name, string ManagementIp, Manufacturer Manufacturer, string ManufacturerName,
    Manufacturer? DetectedManufacturer, bool VendorMismatch, string? Model, SwitchStatus Status,
    DateTime? LastScanAt, bool UseAsArpSource, string? Location, int? CredentialProfileId, string? CredentialProfileName,
    int PortCount);

public record SwitchDetailDto(
    int Id, string Name, string ManagementIp, Manufacturer Manufacturer, string ManufacturerName, string? ManufacturerOther,
    Manufacturer? DetectedManufacturer, bool VendorMismatch, string? Model, string? Location, string? Notes, bool UseAsArpSource,
    SwitchStatus Status, DateTime? LastScanAt, string? SysName, string? SysDescr, string? SysObjectId,
    int? CredentialProfileId, string? CredentialProfileName, CredentialSummary? Credential, DateTime CreatedAt, DateTime UpdatedAt);

public class TestConnectionRequest
{
    public string ManagementIp { get; set; } = string.Empty;
    public int? CredentialProfileId { get; set; }
    public CredentialInput? Credential { get; set; }
    /// <summary>When editing: blank secret fields fall back to the stored credential of this switch.</summary>
    public int? SwitchId { get; set; }
    public Manufacturer Manufacturer { get; set; } = Manufacturer.AutoDetect;
}

public record TestConnectionResult(
    bool Success, string Message, string? ErrorCode, string? SysName, string? SysDescr, string? SysObjectId,
    long? UptimeTicks, Manufacturer? DetectedManufacturer, string? DetectedManufacturerName, bool VendorMismatch, long ElapsedMs);

public class CredentialProfileRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CredentialInput Credential { get; set; } = new();
}

public record CredentialProfileDto(
    int Id, string Name, string? Description, CredentialSummary Credential, int SwitchCount, DateTime UpdatedAt);
