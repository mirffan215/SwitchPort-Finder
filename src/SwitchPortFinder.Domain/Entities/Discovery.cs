using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Domain.Entities;

public class SwitchPort
{
    public int Id { get; set; }
    public int SwitchId { get; set; }
    public Switch? Switch { get; set; }
    public int IfIndex { get; set; }
    public string Name { get; set; } = "";
    public string? Descr { get; set; }
    public string? Alias { get; set; }
    public bool AdminUp { get; set; }
    public bool OperUp { get; set; }
    public long SpeedMbps { get; set; }
    public string? PhysAddress { get; set; }
    public int Mtu { get; set; }
    public int? Vlan { get; set; }
    public PortType PortType { get; set; }
    public NeighborConfidence Confidence { get; set; }
    public PortSupport Support { get; set; }
    /// <summary>Number of VLANs this port is a member of (Q-BRIDGE static egress ports).</summary>
    public int VlanCount { get; set; }
    /// <summary>The winning neighbor row that explains this port's classification (for uplinks).</summary>
    public long? NeighborId { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>A sighting of a MAC in one switch's forwarding table (one row per switch+MAC, refreshed each scan).</summary>
public class DeviceObservation
{
    public long Id { get; set; }
    public string Mac { get; set; } = "";
    public int SwitchId { get; set; }
    public int PortId { get; set; }
    public SwitchPort? Port { get; set; }
    public int? Vlan { get; set; }
    public bool IsStatic { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

public class Device
{
    public int Id { get; set; }
    public string Mac { get; set; } = "";
    public string Hostname { get; set; } = "Unknown Device";
    public HostnameSource HostnameSource { get; set; }
    public DateTime? HostnameCheckedAt { get; set; }
    public string? IpAddress { get; set; }
    public int? IpSourceSwitchId { get; set; }
    public DateTime? IpObservedAt { get; set; }
    public int? SwitchId { get; set; }
    public Switch? Switch { get; set; }
    public int? PortId { get; set; }
    public SwitchPort? Port { get; set; }
    public int? Vlan { get; set; }
    /// <summary>True when the location was chosen with the "fewest MACs on the port" tie-break (lower confidence).</summary>
    public bool LowConfidence { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>An IP→MAC mapping read from an ARP source, with where and when it was seen.</summary>
public class ArpEntry
{
    public long Id { get; set; }
    public int SourceSwitchId { get; set; }
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public DateTime ObservedAt { get; set; }
}

public class DiscoveryJob
{
    public int Id { get; set; }
    public int SwitchId { get; set; }
    public Switch? Switch { get; set; }
    public JobStatus Status { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? StepsJson { get; set; }
    public int PortsFound { get; set; }
    public int DevicesFound { get; set; }
    public int NeighborsFound { get; set; }
    public int ErrorCount { get; set; }
    public string? Summary { get; set; }
    public List<DiscoveryError> Errors { get; set; } = [];
}

public class DiscoveryError
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public string Step { get; set; } = "";
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>A neighbor seen on a port via LLDP or CDP, or an inferred switch link (Protocol = Inferred, from the FDB heuristic).</summary>
public class SwitchNeighbor
{
    public long Id { get; set; }
    public int SwitchId { get; set; }
    public int PortId { get; set; }
    public SwitchPort? Port { get; set; }
    public NeighborProtocol Protocol { get; set; }
    public string RemoteKey { get; set; } = "";
    public string? ChassisId { get; set; }
    public string? ChassisMac { get; set; }
    public string? RemotePortId { get; set; }
    public string? RemotePortDescr { get; set; }
    public string? SysName { get; set; }
    public string? SysDescr { get; set; }
    public string? Platform { get; set; }
    public NeighborCaps Caps { get; set; }
    public string? MgmtIp { get; set; }
    /// <summary>The registered switch this neighbor resolved to (by management IP, chassis MAC or system name), if any.</summary>
    public int? MatchedSwitchId { get; set; }
    public string? Evidence { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
}

/// <summary>Every MAC a switch itself owns (all ifPhysAddress values, LLDP local chassis MAC). Used to recognise switches in other FDBs.</summary>
public class SwitchMac
{
    public int Id { get; set; }
    public int SwitchId { get; set; }
    public string Mac { get; set; } = "";
}

/// <summary>Runtime-editable settings, stored as one JSON document under a key.</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A local account (only used when Authentication:Mode = Local). Only a salted PBKDF2 hash is stored, never the password.</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    /// <summary>Lower-cased username: unique, case-insensitive on every database.</summary>
    public string UsernameNormalized { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public bool Disabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
