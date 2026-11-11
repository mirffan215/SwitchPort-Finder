namespace SwitchPortFinder.Domain.Enums;

public enum Manufacturer
{
    AutoDetect = 0,
    Cisco = 1,
    HpeAruba = 2,
    Dell = 3,
    Juniper = 4,
    ExtremeNetworks = 5,
    Huawei = 6,
    MikroTik = 7,
    Ubiquiti = 8,
    Fortinet = 9,
    Netgear = 10,
    TpLink = 11,
    AlliedTelesis = 12,
    OtherGeneric = 13
}

public enum SnmpVersion { V2c = 2, V3 = 3 }

public enum SnmpAuthProtocol { None = 0, Md5 = 1, Sha1 = 2, Sha256 = 3, Sha384 = 4, Sha512 = 5 }

public enum SnmpPrivProtocol { None = 0, Des = 1, Aes128 = 2, Aes192 = 3, Aes256 = 4 }

public enum SwitchStatus { Unknown = 0, Reachable = 1, Unreachable = 2 }

/// <summary>What kind of device it is. Auto-detected from rules (neighbor info, hostname, VLAN, manufacturer) or set by an administrator.</summary>
public enum DeviceCategory { Unclassified = 0, AccessPoint = 1, IpPhone = 2, Printer = 3, Camera = 4, Server = 5, Other = 6 }

public enum ImportMode { SkipExisting = 0, UpdateExisting = 1 }

public enum PortType { Unknown = 0, Endpoint = 1, SwitchUplink = 2, Down = 3 }

public enum DeviceState { Active = 0, Offline = 1, Stale = 2 }

public enum JobStatus { Queued = 0, Running = 1, Completed = 2, CompletedWithWarnings = 3, Failed = 4 }

public enum HostnameSource { None = 0, ReverseDns = 1, Dhcp = 2, Snmp = 3, Manual = 4 }

public enum NeighborProtocol { Lldp = 0, Cdp = 1, Inferred = 2 }

/// <summary>Neighbor / uplink confidence, strongest last.</summary>
public enum NeighborConfidence { Unknown = 0, Endpoint = 1, LikelySwitch = 2, ConfirmedSwitch = 3 }

/// <summary>LLDP system-capability bits (bit i = 1 &lt;&lt; i, LLDP bit 2 = bridge, bit 4 = router). CDP capabilities are mapped onto the same flags.</summary>
[Flags]
public enum NeighborCaps
{
    None = 0, Other = 1, Repeater = 2, Bridge = 4, WlanAp = 8, Router = 16, Telephone = 32, Docsis = 64, Station = 128
}

/// <summary>Supporting (never sufficient alone) evidence that a port may be an inter-switch link.</summary>
[Flags]
public enum PortSupport { None = 0, StpRootPort = 1, TaggedVlans = 2, DescriptionHint = 4 }

public enum UserRole { Viewer = 0, Admin = 1 }
