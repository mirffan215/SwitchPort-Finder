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

public enum ImportMode { SkipExisting = 0, UpdateExisting = 1 }
