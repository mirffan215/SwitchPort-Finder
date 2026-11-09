namespace SwitchPortFinder.Application.Snmp;

public static class Oids
{
    public const string SysDescr = "1.3.6.1.2.1.1.1.0";
    public const string SysObjectId = "1.3.6.1.2.1.1.2.0";
    public const string SysUpTime = "1.3.6.1.2.1.1.3.0";
    public const string SysName = "1.3.6.1.2.1.1.5.0";

    // IF-MIB
    public const string IfTable = "1.3.6.1.2.1.2.2.1";
    public const string IfXTable = "1.3.6.1.2.1.31.1.1.1";
    // IP-MIB
    public const string IpForwarding = "1.3.6.1.2.1.4.1.0";
    public const string IpNetToPhysicalTable = "1.3.6.1.2.1.4.35.1";
    public const string IpNetToMediaTable = "1.3.6.1.2.1.4.22.1";
    // BRIDGE-MIB / Q-BRIDGE-MIB
    public const string Dot1dBasePortIfIndex = "1.3.6.1.2.1.17.1.4.1.2";
    public const string Dot1dTpFdbTable = "1.3.6.1.2.1.17.4.3.1";
    public const string Dot1qTpFdbTable = "1.3.6.1.2.1.17.7.1.2.2.1";
    public const string Dot1qVlanFdbId = "1.3.6.1.2.1.17.7.1.4.2.1.3";
    public const string Dot1qVlanStaticName = "1.3.6.1.2.1.17.7.1.4.3.1.1";
    public const string Dot1qPvid = "1.3.6.1.2.1.17.7.1.4.5.1.1";
    // ENTITY-MIB
    public const string EntPhysicalSerial = "1.3.6.1.2.1.47.1.1.1.1.11";
    public const string EntPhysicalModel = "1.3.6.1.2.1.47.1.1.1.1.13";
    // Cisco
    public const string CiscoVtpVlanState = "1.3.6.1.4.1.9.9.46.1.3.1.1.2";
    public const string CiscoVmVlan = "1.3.6.1.4.1.9.9.68.1.2.2.1.2";
}
