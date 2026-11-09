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
