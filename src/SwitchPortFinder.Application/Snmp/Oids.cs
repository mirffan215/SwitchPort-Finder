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
    public const string Dot1dStpRootPort = "1.3.6.1.2.1.17.2.7.0";
    public const string Dot1qVlanStaticEgressPorts = "1.3.6.1.2.1.17.7.1.4.3.1.2";
    public const string Dot1qVlanStaticUntaggedPorts = "1.3.6.1.2.1.17.7.1.4.3.1.4";
    public const string Dot1qPvid = "1.3.6.1.2.1.17.7.1.4.5.1.1";
    // Health: HOST-RESOURCES-MIB (any vendor), CISCO-PROCESS-MIB / CISCO-MEMORY-POOL-MIB, JUNIPER-MIB
    public const string HrProcessorEntry = "1.3.6.1.2.1.25.3.3.1";          // column 2 = hrProcessorLoad (%)
    public const string HrStorageEntry = "1.3.6.1.2.1.25.2.3.1";            // 2 type, 4 allocation unit, 5 size, 6 used
    public const string HrStorageRam = "1.3.6.1.2.1.25.2.1.2";
    public const string CiscoCpuTotalEntry = "1.3.6.1.4.1.9.9.109.1.1.1.1"; // column 8 = cpmCPUTotal5minRev (%)
    public const string CiscoMemoryPoolEntry = "1.3.6.1.4.1.9.9.48.1.1.1";  // column 5 = used, 6 = free (bytes)
    public const string JuniperOperatingEntry = "1.3.6.1.4.1.2636.3.1.13.1"; // column 8 = jnxOperatingCPU (%), 11 = jnxOperatingBuffer (memory %)
    // ENTITY-MIB
    public const string EntPhysicalSerial = "1.3.6.1.2.1.47.1.1.1.1.11";
    public const string EntPhysicalModel = "1.3.6.1.2.1.47.1.1.1.1.13";
    // LLDP-MIB
    public const string LldpLocChassisIdSubtype = "1.0.8802.1.1.2.1.3.1.0";
    public const string LldpLocChassisId = "1.0.8802.1.1.2.1.3.2.0";
    public const string LldpLocPortTable = "1.0.8802.1.1.2.1.3.7.1";
    public const string LldpRemTable = "1.0.8802.1.1.2.1.4.1.1";
    public const string LldpRemManAddrTable = "1.0.8802.1.1.2.1.4.2.1";
    // Cisco
    public const string CdpCacheTable = "1.3.6.1.4.1.9.9.23.1.2.1.1";
    public const string CiscoVtpVlanState = "1.3.6.1.4.1.9.9.46.1.3.1.1.2";
    public const string CiscoVmVlan = "1.3.6.1.4.1.9.9.68.1.2.2.1.2";
}
