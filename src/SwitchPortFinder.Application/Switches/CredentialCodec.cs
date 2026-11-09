using System.Text.Json;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Switches;

/// <summary>Converts between input DTOs, encrypted storage form and runtime credentials, and validates them.</summary>
public class CredentialCodec(ICredentialProtector protector)
{
    private sealed record Secrets(string? Community, string? Username, string? AuthPassword, string? PrivPassword);

    public string Encrypt(SnmpCredentials c) =>
        protector.Protect(JsonSerializer.Serialize(new Secrets(c.Community, c.Username, c.AuthPassword, c.PrivPassword)));

    public SnmpCredentials Decrypt(SnmpVersion version, SnmpAuthProtocol auth, SnmpPrivProtocol priv, string? context, string encrypted)
    {
        var s = string.IsNullOrEmpty(encrypted)
            ? new Secrets(null, null, null, null)
            : JsonSerializer.Deserialize<Secrets>(protector.Unprotect(encrypted)) ?? new Secrets(null, null, null, null);
        return new SnmpCredentials
        {
            Version = version, AuthProtocol = auth, PrivProtocol = priv, ContextName = context,
            Community = s.Community, Username = s.Username, AuthPassword = s.AuthPassword, PrivPassword = s.PrivPassword
        };
    }

    /// <summary>
    /// Builds credentials from input. Blank secret fields inherit from <paramref name="existing"/> when the
    /// SNMP version is unchanged ("unchanged" placeholder semantics on edit forms).
    /// </summary>
    public static SnmpCredentials Merge(CredentialInput input, SnmpCredentials? existing)
    {
        var inherit = existing is not null && existing.Version == input.Version;
        string? Pick(string? supplied, string? old) => !string.IsNullOrEmpty(supplied) ? supplied : inherit ? old : null;
        return new SnmpCredentials
        {
            Version = input.Version,
            Community = input.Version == SnmpVersion.V2c ? Pick(input.Community, existing?.Community) : null,
            Username = input.Version == SnmpVersion.V3 ? Pick(input.Username, existing?.Username) : null,
            AuthProtocol = input.Version == SnmpVersion.V3 ? input.AuthProtocol : SnmpAuthProtocol.None,
            AuthPassword = input.Version == SnmpVersion.V3 && input.AuthProtocol != SnmpAuthProtocol.None ? Pick(input.AuthPassword, existing?.AuthPassword) : null,
            PrivProtocol = input.Version == SnmpVersion.V3 ? input.PrivProtocol : SnmpPrivProtocol.None,
            PrivPassword = input.Version == SnmpVersion.V3 && input.PrivProtocol != SnmpPrivProtocol.None ? Pick(input.PrivPassword, existing?.PrivPassword) : null,
            ContextName = input.Version == SnmpVersion.V3 && !string.IsNullOrWhiteSpace(input.ContextName) ? input.ContextName.Trim() : null
        };
    }

    public static Dictionary<string, string[]> Validate(SnmpCredentials c, string prefix = "credential")
    {
        var e = new Dictionary<string, string[]>();
        if (c.Version == SnmpVersion.V2c)
        {
            if (string.IsNullOrEmpty(c.Community)) e[$"{prefix}.community"] = ["SNMPv2c requires a community string."];
        }
        else
        {
            if (string.IsNullOrEmpty(c.Username)) e[$"{prefix}.username"] = ["SNMPv3 requires a username."];
            if (c.AuthProtocol != SnmpAuthProtocol.None && (c.AuthPassword?.Length ?? 0) < 8)
                e[$"{prefix}.authPassword"] = ["Authentication password must be at least 8 characters."];
            if (c.PrivProtocol != SnmpPrivProtocol.None)
            {
                if (c.AuthProtocol == SnmpAuthProtocol.None) e[$"{prefix}.privProtocol"] = ["Privacy requires an authentication protocol."];
                if ((c.PrivPassword?.Length ?? 0) < 8) e[$"{prefix}.privPassword"] = ["Privacy password must be at least 8 characters."];
            }
        }
        return e;
    }

    public static CredentialSummary Summarize(SnmpCredentials c) => new(
        c.Version, c.AuthProtocol, c.PrivProtocol, c.ContextName,
        !string.IsNullOrEmpty(c.Community), !string.IsNullOrEmpty(c.Username),
        !string.IsNullOrEmpty(c.AuthPassword), !string.IsNullOrEmpty(c.PrivPassword));
}
