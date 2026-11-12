using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;
using SwitchPortFinder.Infrastructure.Persistence;

namespace SwitchPortFinder.Infrastructure.Integrations;

public sealed record IntegrationDto(string Kind, bool Enabled, Dictionary<string, object?> Config, bool HasSecret, DateTime? LastRunAt, bool? LastOk, string? LastMessage, int LastMatched);
public sealed record IntegrationRequest(bool Enabled, Dictionary<string, JsonElement>? Config, string? Secret);

/// <summary>
/// Optional integrations, all OFF by default and switched on by an administrator: Windows DHCP (names for unknown devices), Active Directory (computer name, OS, OU)
/// and Intune (device name, user, OS, compliance). Read-only. They only fill in what is unknown; an administrator's manual values are never overwritten.
/// </summary>
public class IntegrationService(AppDbContext db, ICredentialProtector protector, IDhcpLeaseSource dhcp, IDirectorySource directory, IIntuneSource intune, IOptions<SecurityOptions> security, ILogger<IntegrationService> log)
{
    public static readonly string[] Kinds = ["dhcp", "ad", "intune"];
    private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

    private async Task<Integration> RowAsync(string kind, CancellationToken ct)
    {
        if (!Kinds.Contains(kind)) throw new NotFoundException("Unknown integration.");
        var r = await db.Integrations.FirstOrDefaultAsync(x => x.Kind == kind, ct);
        if (r is null) { r = new Integration { Kind = kind, UpdatedAt = DateTime.UtcNow }; db.Integrations.Add(r); }
        return r;
    }

    private static IntegrationDto ToDto(Integration r)
    {
        var cfg = JsonSerializer.Deserialize<Dictionary<string, object?>>(r.ConfigJson, J) ?? [];
        return new(r.Kind, r.Enabled, cfg, !string.IsNullOrEmpty(r.EncryptedSecret), r.LastRunAt, r.LastOk, r.LastMessage, r.LastMatched);
    }

    public async Task<List<IntegrationDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Integrations.AsNoTracking().ToListAsync(ct);
        return Kinds.Select(k => ToDto(rows.FirstOrDefault(r => r.Kind == k) ?? new Integration { Kind = k })).ToList();
    }

    private static string Str(Dictionary<string, JsonElement>? c, string k) => c is not null && c.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

    public async Task<IntegrationDto> SaveAsync(string kind, IntegrationRequest q, string actor, CancellationToken ct)
    {
        var r = await RowAsync(kind, ct);
        object cfg; var c = q.Config;
        switch (kind)
        {
            case "dhcp":
                { var s = Str(c, "server"); if (q.Enabled && !Parse.IsHost(s)) throw new ValidationFailedException("server", "Enter the DHCP server's name or IP address."); CheckTarget(s); cfg = new DhcpConfig { Server = s }; break; }
            case "ad":
                {
                    var d = new DirectoryConfig { Server = Str(c, "server"), BaseDn = Str(c, "baseDn"), BindUser = Str(c, "bindUser"), TrustServerCertificate = c is not null && c.TryGetValue("trustServerCertificate", out var t) && t.ValueKind == JsonValueKind.True, Port = c is not null && c.TryGetValue("port", out var pv) && pv.TryGetInt32(out var pn) ? pn : 636 };
                    if (d.Port is < 1 or > 65535) throw new ValidationFailedException("port", "The port must be between 1 and 65535.");
                    if (q.Enabled && (!Parse.IsHost(d.Server))) throw new ValidationFailedException("server", "Enter the domain controller's name or IP address.");
                    if (q.Enabled && (d.BaseDn.Length is 0 or > 300 || !d.BaseDn.Contains("DC=", StringComparison.OrdinalIgnoreCase))) throw new ValidationFailedException("baseDn", "Enter the base DN, for example DC=corp,DC=local.");
                    if (q.Enabled && (d.BindUser.Length is 0 or > 200)) throw new ValidationFailedException("bindUser", "Enter the read-only service account (user@domain).");
                    CheckTarget(d.Server); cfg = d; break;
                }
            default:
                {
                    var i = new IntuneConfig { TenantId = Str(c, "tenantId"), ClientId = Str(c, "clientId") };
                    if (q.Enabled && (!Guid.TryParse(i.TenantId, out _) || !Guid.TryParse(i.ClientId, out _))) throw new ValidationFailedException("tenantId", "The tenant ID and client ID are GUIDs from the Entra app registration.");
                    cfg = i; break;
                }
        }
        if (q.Secret is not null) r.EncryptedSecret = q.Secret.Length == 0 ? null : protector.Protect(q.Secret);
        if (q.Enabled && kind != "dhcp" && string.IsNullOrEmpty(r.EncryptedSecret)) throw new ValidationFailedException("secret", kind == "ad" ? "Enter the service account's password." : "Enter the app registration's client secret.");
        r.Enabled = q.Enabled; r.ConfigJson = JsonSerializer.Serialize(cfg, J); r.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogWarning("IntegrationChanged: {Kind} saved by {Actor} (enabled: {Enabled})", kind, actor, r.Enabled);
        return ToDto(r);
    }

    private void CheckTarget(string host) { if (System.Net.IPAddress.TryParse(host, out var ip) && TargetPolicy.Check(ip.ToString(), security.Value.AllowLoopbackTargets) is { } bad) throw new ValidationFailedException("server", bad); }

    public async Task<IntegrationDto> RunAsync(string kind, CancellationToken ct)
    {
        var r = await RowAsync(kind, ct);
        if (!r.Enabled) throw new ValidationFailedException("kind", "This integration is switched off. Turn it on and save first.");
        try
        {
            (int seen, int matched) = kind switch { "dhcp" => await RunDhcp(r, ct), "ad" => await RunAd(r, ct), _ => await RunIntune(r, ct) };
            r.LastOk = true; r.LastMatched = matched; r.LastMessage = $"Read {seen} record{(seen == 1 ? "" : "s")}; {matched} known device{(matched == 1 ? "" : "s")} updated.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            r.LastOk = false; r.LastMatched = 0; r.LastMessage = Trunc(ex.Message, 480);
            log.LogWarning("Integration {Kind} failed: {Message}", kind, ex.Message);
        }
        r.LastRunAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToDto(r);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
    private static bool Unknown(Device d) => d.Hostname == "Unknown Device" || d.HostnameSource is HostnameSource.None;

    private async Task<(int, int)> RunDhcp(Integration r, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<DhcpConfig>(r.ConfigJson, J)!;
        var leases = await dhcp.ReadAsync(cfg.Server, ct);
        // per MAC: the lease and the reservation (a reservation's name is used when the lease has no host name)
        var byMac = leases.GroupBy(l => l.Mac).ToDictionary(g => g.Key, g => (Lease: g.LastOrDefault(x => !x.Reserved), Resv: g.LastOrDefault(x => x.Reserved)));
        var devices = await db.Devices.ToListAsync(ct); var n = 0; var now = DateTime.UtcNow;
        foreach (var d in devices)
        {
            if (!byMac.TryGetValue(d.Mac, out var m)) continue;
            var l = m.Lease ?? m.Resv!; var changed = false;
            var detail = Detail(m.Lease, m.Resv);
            if (d.DhcpDetail != detail) { d.DhcpDetail = detail; changed = true; }
            if (Parse.ShortName(l.HostName ?? m.Resv?.HostName) is { } name && d.HostnameSource != HostnameSource.Manual && (Unknown(d) || d.HostnameSource is HostnameSource.ReverseDns or HostnameSource.Dhcp) && d.Hostname != name) { d.Hostname = name; d.HostnameSource = HostnameSource.Dhcp; d.HostnameCheckedAt = now; changed = true; }
            if (string.IsNullOrEmpty(d.IpAddress)) { d.IpAddress = l.Ip; d.IpObservedAt = now; changed = true; }
            if (changed) n++;
        }
        await db.SaveChangesAsync(ct);
        return (leases.Count, n);
    }

    private static string Detail(DhcpLease? lease, DhcpLease? resv)
    {
        var p = new List<string>();
        if (resv is not null) p.Add($"reserved{(resv.HostName is null ? "" : $" as '{resv.HostName}'")}{(resv.Description is null ? "" : $" ({resv.Description})")}");
        var any = lease ?? resv!;
        p.Add($"scope {any.Scope ?? "?"}"); p.Add($"IP {any.Ip}");
        if (lease?.State is { } st) p.Add(st.ToLowerInvariant());
        if (lease?.Expires is { } ex) p.Add($"lease until {ex.Replace('T', ' ')}");
        var s = string.Join(" · ", p); return s.Length <= 300 ? s : s[..300];
    }

    private async Task<(int, int)> RunAd(Integration r, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<DirectoryConfig>(r.ConfigJson, J)!;
        var comps = await directory.ReadAsync(cfg, protector.Unprotect(r.EncryptedSecret!), ct);
        var byName = comps.GroupBy(c => c.Name.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First());
        var devices = await db.Devices.ToListAsync(ct); var n = 0; var now = DateTime.UtcNow;
        foreach (var d in devices)
        {
            if (Unknown(d) || !byName.TryGetValue(d.Hostname.ToUpperInvariant(), out var c)) continue;
            d.ExtSource = "Active Directory"; d.ExtOs = Trunc(c.Os ?? "", 100) is { Length: > 0 } os ? os : d.ExtOs; d.ExtDetail = c.Ou is null ? "Domain computer" : Trunc("OU: " + c.Ou, 300); d.ExtAt = now; n++;
        }
        await db.SaveChangesAsync(ct);
        return (comps.Count, n);
    }

    private async Task<(int, int)> RunIntune(Integration r, CancellationToken ct)
    {
        var cfg = JsonSerializer.Deserialize<IntuneConfig>(r.ConfigJson, J)!;
        var list = await intune.ReadAsync(cfg, protector.Unprotect(r.EncryptedSecret!), ct);
        var byMac = new Dictionary<string, IntuneDevice>();
        foreach (var i in list) foreach (var m in new[] { Parse.Mac(i.WifiMac), Parse.Mac(i.EthMac) }) if (m != null) byMac[m] = i;
        var devices = await db.Devices.ToListAsync(ct); var n = 0; var now = DateTime.UtcNow;
        foreach (var d in devices)
        {
            if (!byMac.TryGetValue(d.Mac, out var i)) continue;
            if (Parse.ShortName(i.Name) is { } name && d.HostnameSource != HostnameSource.Manual && Unknown(d)) { d.Hostname = name; d.HostnameSource = HostnameSource.Snmp; d.HostnameCheckedAt = now; }
            d.ExtSource = "Intune"; d.ExtUser = Trunc(i.User ?? "", 150) is { Length: > 0 } u ? u : d.ExtUser; d.ExtOs = Trunc(i.Os ?? "", 100) is { Length: > 0 } os ? os : d.ExtOs;
            d.ExtDetail = Trunc($"Compliance: {i.Compliance ?? "unknown"}{(string.IsNullOrEmpty(i.Serial) ? "" : " · Serial " + i.Serial)}", 300); d.ExtAt = now; n++;
        }
        await db.SaveChangesAsync(ct);
        return (list.Count, n);
    }
}
