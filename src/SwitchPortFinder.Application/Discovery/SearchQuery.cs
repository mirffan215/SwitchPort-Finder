using System.Text.RegularExpressions;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain;

namespace SwitchPortFinder.Application.Discovery;

/// <summary>
/// A parsed global-search query. One box handles MAC (any format, partial ≥ 4 hex chars), IP (full or partial), hostname,
/// switch name/IP and port name, so the parser only extracts the MAC/IP interpretations; text matching is always attempted too.
/// </summary>
public sealed record ParsedQuery(string Text, string? MacKey, bool MacExact, string? IpExact, bool LooksLikeIp)
{
    private static readonly Regex Ipv4Fragment = new(@"^\d{1,3}(\.\d{0,3}){1,3}$", RegexOptions.Compiled);
    private static readonly Regex Ipv6Fragment = new(@"^[0-9a-fA-F:]*:[0-9a-fA-F:]*$", RegexOptions.Compiled);
    /// <summary>AA:BB:CC… — colon groups of exactly two hex digits are MAC-style, never IPv6.</summary>
    private static readonly Regex MacStyle = new(@"^([0-9a-fA-F]{2}:){1,5}[0-9a-fA-F]{0,2}$", RegexOptions.Compiled);

    public static ParsedQuery? Parse(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t) || t.Length < 2) return null;
        var ip = SwitchService.NormalizeIp(t);
        var ipLike = ip is not null || Ipv4Fragment.IsMatch(t) || (Ipv6Fragment.IsMatch(t) && !MacStyle.IsMatch(t));
        // "10.10.50" is hex-digit-only after stripping dots, but it is an IP fragment, not a MAC fragment.
        var macKey = ipLike && MacAddress.Normalize(t) is null ? null : MacAddress.PartialKey(t);
        return new ParsedQuery(t, macKey, MacAddress.Normalize(t) is not null, ip, ipLike);
    }
}

public record SwitchHit(int Id, string Name, string ManagementIp, string ManufacturerName, string Status, string? Model);
public record PortHit(int SwitchId, string SwitchName, string SwitchIp, int PortId, int IfIndex, string PortName, string? Alias, string Status, Domain.Enums.PortType Type);
public record SearchResponse(string Query, IReadOnlyList<DeviceDto> Devices, IReadOnlyList<SwitchHit> Switches, IReadOnlyList<PortHit> Ports, bool Truncated);
