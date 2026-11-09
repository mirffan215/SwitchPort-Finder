using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Snmp;

/// <summary>
/// Maps the IANA enterprise number in sysObjectID (1.3.6.1.4.1.N...) to a manufacturer.
/// These mappings are the commonly published enterprise numbers; they must be verified
/// against sysObjectID values from real devices (see docs/SNMP-SETUP.md).
/// A manually selected manufacturer always overrides detection.
/// </summary>
public static class VendorDetector
{
    private const string EnterprisePrefix = "1.3.6.1.4.1.";

    private static readonly Dictionary<int, Manufacturer> Map = new()
    {
        [9] = Manufacturer.Cisco,
        [11] = Manufacturer.HpeAruba,
        [25506] = Manufacturer.HpeAruba,
        [47196] = Manufacturer.HpeAruba,
        [674] = Manufacturer.Dell,
        [2636] = Manufacturer.Juniper,
        [1916] = Manufacturer.ExtremeNetworks,
        [4413] = Manufacturer.ExtremeNetworks,
        [2011] = Manufacturer.Huawei,
        [14988] = Manufacturer.MikroTik,
        [41112] = Manufacturer.Ubiquiti,
        [12356] = Manufacturer.Fortinet,
        [4526] = Manufacturer.Netgear,
        [11863] = Manufacturer.TpLink,
        [207] = Manufacturer.AlliedTelesis,
    };

    public static int? ExtractEnterpriseNumber(string? sysObjectId)
    {
        if (string.IsNullOrWhiteSpace(sysObjectId)) return null;
        var oid = sysObjectId.Trim().TrimStart('.');
        if (!oid.StartsWith(EnterprisePrefix, StringComparison.Ordinal)) return null;
        var rest = oid[EnterprisePrefix.Length..];
        var end = rest.IndexOf('.');
        var token = end < 0 ? rest : rest[..end];
        return int.TryParse(token, out var n) ? n : null;
    }

    /// <summary>Returns the detected vendor, or <see cref="Manufacturer.OtherGeneric"/> when unknown.</summary>
    public static Manufacturer Detect(string? sysObjectId) =>
        ExtractEnterpriseNumber(sysObjectId) is { } n && Map.TryGetValue(n, out var m) ? m : Manufacturer.OtherGeneric;

    /// <summary>Effective vendor: manual selection wins; AutoDetect falls back to detection.</summary>
    public static Manufacturer Effective(Manufacturer selected, Manufacturer? detected) =>
        selected != Manufacturer.AutoDetect ? selected : detected ?? Manufacturer.OtherGeneric;

    public static bool Mismatch(Manufacturer selected, Manufacturer? detected) =>
        selected is not (Manufacturer.AutoDetect or Manufacturer.OtherGeneric) && detected is { } d && d != Manufacturer.OtherGeneric && d != selected;

    public static string DisplayName(Manufacturer m) => m switch
    {
        Manufacturer.AutoDetect => "Auto-detect",
        Manufacturer.HpeAruba => "HPE / Aruba",
        Manufacturer.ExtremeNetworks => "Extreme Networks",
        Manufacturer.Fortinet => "Fortinet (FortiSwitch)",
        Manufacturer.TpLink => "TP-Link",
        Manufacturer.AlliedTelesis => "Allied Telesis",
        Manufacturer.OtherGeneric => "Other / Generic SNMP",
        _ => m.ToString()
    };

    /// <summary>Parses a user-typed manufacturer name (import files). Returns null if not recognised.</summary>
    public static Manufacturer? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Manufacturer.AutoDetect;
        var key = new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return key switch
        {
            "autodetect" or "auto" => Manufacturer.AutoDetect,
            "cisco" => Manufacturer.Cisco,
            "hpe" or "hpearuba" or "aruba" or "hp" or "hparuba" or "hewlettpackard" => Manufacturer.HpeAruba,
            "dell" => Manufacturer.Dell,
            "juniper" => Manufacturer.Juniper,
            "extreme" or "extremenetworks" => Manufacturer.ExtremeNetworks,
            "huawei" => Manufacturer.Huawei,
            "mikrotik" => Manufacturer.MikroTik,
            "ubiquiti" or "unifi" => Manufacturer.Ubiquiti,
            "fortinet" or "fortiswitch" or "fortinetfortiswitch" => Manufacturer.Fortinet,
            "netgear" => Manufacturer.Netgear,
            "tplink" => Manufacturer.TpLink,
            "alliedtelesis" or "allied" => Manufacturer.AlliedTelesis,
            "other" or "othergeneric" or "othergenericsnmp" or "generic" or "genericsnmp" => Manufacturer.OtherGeneric,
            _ => null
        };
    }
}
