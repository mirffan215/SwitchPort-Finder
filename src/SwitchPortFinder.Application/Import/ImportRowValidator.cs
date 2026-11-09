using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Import;

/// <summary>Pure validation of raw rows. No I/O, so it is easy to unit test.</summary>
public static class ImportRowValidator
{
    public static List<ParsedImportRow> Validate(IEnumerable<RawImportRow> raw, ISet<string> knownProfileNames)
    {
        var result = new List<ParsedImportRow>();
        var seenIps = new Dictionary<string, int>();
        foreach (var r in raw)
        {
            string C(string col) => r.Cells.TryGetValue(col, out var v) ? v.Trim() : "";
            var errors = new List<string>(); var warnings = new List<string>();

            var name = C("Name");
            if (name.Length == 0) errors.Add("Name is required.");
            else if (name.Length > 100) errors.Add("Name must be 100 characters or fewer.");

            var ip = SwitchService.NormalizeIp(C("ManagementIP"));
            if (ip is null) errors.Add($"ManagementIP '{C("ManagementIP")}' is not a valid IPv4/IPv6 address.");
            else if (seenIps.TryGetValue(ip, out var firstRow)) errors.Add($"Duplicate ManagementIP {ip} (first seen on row {firstRow}).");
            else seenIps[ip] = r.RowNumber;

            var manu = VendorDetector.Parse(C("Manufacturer"));
            if (manu is null) { warnings.Add($"Unknown manufacturer '{C("Manufacturer")}'; using Auto-detect."); manu = Manufacturer.AutoDetect; }

            bool? arp = null;
            if (C("UseAsArpSource").Length > 0)
            {
                arp = ParseBool(C("UseAsArpSource"));
                if (arp is null) errors.Add("UseAsArpSource must be true or false.");
            }

            var profile = C("CredentialProfile");
            string? profileName = null;
            if (profile.Length > 0)
            {
                var match = knownProfileNames.FirstOrDefault(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase));
                if (match is null) errors.Add($"Credential profile '{profile}' does not exist. Create it under Settings first.");
                else profileName = match;
            }

            SnmpCredentials? inline = null;
            var hasInline = new[] { "Community", "V3Username", "V3AuthPassword", "V3PrivPassword", "V3Context", "V3AuthProtocol", "V3PrivProtocol" }.Any(c => C(c).Length > 0);
            var versionText = C("SnmpVersion");
            if (profileName is not null && (hasInline))
                warnings.Add("Both a credential profile and inline credentials are present; the profile is used and inline values are ignored.");
            else if (hasInline || (profileName is null && versionText.Length > 0))
                inline = BuildInline(C, versionText, errors);

            var row = new ParsedImportRow
            {
                RowNumber = r.RowNumber, Name = name, Ip = ip ?? C("ManagementIP"), Manufacturer = manu.Value,
                Model = NullIfBlank(C("Model")), Location = NullIfBlank(C("Location")), Notes = NullIfBlank(C("Notes")),
                UseAsArpSource = arp, ProfileName = profileName, InlineCredentials = profileName is null ? inline : null
            };
            row.Errors.AddRange(errors); row.Warnings.AddRange(warnings);
            result.Add(row);
        }
        return result;
    }

    private static SnmpCredentials? BuildInline(Func<string, string> C, string versionText, List<string> errors)
    {
        SnmpVersion? version = versionText.ToLowerInvariant() switch
        {
            "v2c" or "2c" or "2" or "v2" => SnmpVersion.V2c,
            "v3" or "3" => SnmpVersion.V3,
            "" => C("V3Username").Length > 0 ? SnmpVersion.V3 : C("Community").Length > 0 ? SnmpVersion.V2c : null,
            _ => null
        };
        if (version is null)
        {
            errors.Add(versionText.Length == 0 ? "SnmpVersion is required (v2c or v3)." : $"SnmpVersion '{versionText}' is invalid; use v2c or v3.");
            return null;
        }
        var auth = ParseAuth(C("V3AuthProtocol")); var priv = ParsePriv(C("V3PrivProtocol"));
        if (auth is null) { errors.Add($"V3AuthProtocol '{C("V3AuthProtocol")}' is invalid (none, MD5, SHA, SHA256, SHA384, SHA512)."); auth = SnmpAuthProtocol.None; }
        if (priv is null) { errors.Add($"V3PrivProtocol '{C("V3PrivProtocol")}' is invalid (none, DES, AES128, AES192, AES256)."); priv = SnmpPrivProtocol.None; }
        var creds = new SnmpCredentials
        {
            Version = version.Value, Community = NullIfBlank(C("Community")), Username = NullIfBlank(C("V3Username")),
            AuthProtocol = version == SnmpVersion.V3 ? auth.Value : SnmpAuthProtocol.None,
            AuthPassword = NullIfBlank(C("V3AuthPassword")),
            PrivProtocol = version == SnmpVersion.V3 ? priv.Value : SnmpPrivProtocol.None,
            PrivPassword = NullIfBlank(C("V3PrivPassword")), ContextName = version == SnmpVersion.V3 ? NullIfBlank(C("V3Context")) : null
        };
        foreach (var kv in CredentialCodec.Validate(creds)) errors.AddRange(kv.Value.Select(m => m));
        return creds;
    }

    public static bool? ParseBool(string s) => s.Trim().ToLowerInvariant() switch
    {
        "true" or "yes" or "y" or "1" => true,
        "false" or "no" or "n" or "0" => false,
        _ => null
    };

    public static SnmpAuthProtocol? ParseAuth(string s) => s.Trim().ToLowerInvariant().Replace("-", "") switch
    {
        "" or "none" or "noauth" => SnmpAuthProtocol.None,
        "md5" => SnmpAuthProtocol.Md5,
        "sha" or "sha1" => SnmpAuthProtocol.Sha1,
        "sha256" => SnmpAuthProtocol.Sha256,
        "sha384" => SnmpAuthProtocol.Sha384,
        "sha512" => SnmpAuthProtocol.Sha512,
        _ => null
    };

    public static SnmpPrivProtocol? ParsePriv(string s) => s.Trim().ToLowerInvariant().Replace("-", "") switch
    {
        "" or "none" or "nopriv" => SnmpPrivProtocol.None,
        "des" => SnmpPrivProtocol.Des,
        "aes" or "aes128" => SnmpPrivProtocol.Aes128,
        "aes192" => SnmpPrivProtocol.Aes192,
        "aes256" => SnmpPrivProtocol.Aes256,
        _ => null
    };

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
