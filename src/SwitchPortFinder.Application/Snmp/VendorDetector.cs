using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Snmp;

/// <summary>
/// Maps sysObjectID (1.3.6.1.4.1.N...) to a manufacturer. Every entry below was checked against the community-maintained device-fingerprint
/// database of LibreNMS (resources/definitions/os_detection/*.yaml, i.e. sysObjectID values seen on real devices), and the Huawei root against
/// its vendor MIB (HUAWEI-MIB: enterprises 2011). See docs/SNMP-SETUP.md for the verification notes.
/// A manually selected manufacturer always overrides detection.
/// </summary>
public static class VendorDetector
{
    private const string EnterprisePrefix = "1.3.6.1.4.1.";

    private static readonly Dictionary<int, Manufacturer> Map = new()
    {
        [9] = Manufacturer.Cisco,                 // ios, iosxe, nxos
        [11] = Manufacturer.HpeAruba,             // procurve (HP): 11.2.3.7.8.5 / 11.2.3.7.11
        [25506] = Manufacturer.HpeAruba,          // comware (H3C / HPE)
        [47196] = Manufacturer.HpeAruba,          // arubaos-cx
        [14823] = Manufacturer.HpeAruba,          // Aruba Networks (ArubaOS)
        [674] = Manufacturer.Dell,                // powerconnect, dell-os10
        [6027] = Manufacturer.Dell,               // Dell Force10 (FTOS / DNOS)
        [2636] = Manufacturer.Juniper,            // junos
        [1916] = Manufacturer.ExtremeNetworks,    // EXOS / VOSS (voss: 1916.2)
        [2272] = Manufacturer.ExtremeNetworks,    // VOSS (Avaya/Nortel heritage)
        [2011] = Manufacturer.Huawei,             // HUAWEI-MIB root
        [14988] = Manufacturer.MikroTik,          // routeros
        [41112] = Manufacturer.Ubiquiti,          // unifi, airos, edgeos
        [10002] = Manufacturer.Ubiquiti,          // older UBNT products (unifi/airos)
        [12356] = Manufacturer.Fortinet,          // fortiswitch
        [4526] = Manufacturer.Netgear,            // netgear
        [11863] = Manufacturer.TpLink,            // jetstream, tplink
        [16972] = Manufacturer.TpLink,            // tplink
        [207] = Manufacturer.AlliedTelesis,       // awplus
    };

    /// <summary>More specific prefixes win over the enterprise number.</summary>
    private static readonly (string Prefix, Manufacturer M)[] Prefixes =
    [
        ("1.3.6.1.4.1.347.2.5730", Manufacturer.ExtremeNetworks),   // EXOS (exos.yaml)
    ];

    /// <summary>
    /// 4413 is the Broadcom FASTPATH enterprise number shared by several vendors (it is NOT Extreme). LibreNMS only treats it as Ubiquiti EdgeSwitch
    /// when sysDescr says so; without that evidence it stays Generic.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex EdgeSwitchDescr = new(@"^(EdgeSwitch|EdgePoint|USW[ -])", System.Text.RegularExpressions.RegexOptions.Compiled);

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

    /// <summary>Returns the detected vendor, or <see cref="Manufacturer.OtherGeneric"/> when unknown. <paramref name="sysDescr"/> disambiguates shared numbers.</summary>
    public static Manufacturer Detect(string? sysObjectId, string? sysDescr = null)
    {
        if (string.IsNullOrWhiteSpace(sysObjectId)) return Manufacturer.OtherGeneric;
        var oid = sysObjectId.Trim().TrimStart('.');
        foreach (var (prefix, m) in Prefixes) if (oid == prefix || oid.StartsWith(prefix + ".", StringComparison.Ordinal)) return m;
        var n = ExtractEnterpriseNumber(oid);
        if (n == 4413) return sysDescr is not null && EdgeSwitchDescr.IsMatch(sysDescr.TrimStart()) ? Manufacturer.Ubiquiti : Manufacturer.OtherGeneric;
        return n is { } e && Map.TryGetValue(e, out var v) ? v : Manufacturer.OtherGeneric;
    }

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
