using System.Net;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Switches;

public class SwitchService(
    ISwitchRepository switches, ICredentialProfileRepository profiles, CredentialCodec codec, SnmpConnectionService snmp, Microsoft.Extensions.Options.IOptions<SecurityOptions> security)
{
    public static string? NormalizeIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var t = ip.Trim();
        // IPAddress.TryParse accepts "10" or "1.2" shorthand; require a real dotted-quad or IPv6 literal.
        if (!t.Contains(':') && t.Count(c => c == '.') != 3) return null;
        return IPAddress.TryParse(t, out var a) ? a.ToString() : null;
    }

    public async Task<List<SwitchSummaryDto>> ListAsync(CancellationToken ct)
    {
        var counts = await switches.PortCountsAsync(ct);
        return (await switches.ListAsync(ct)).Select(s => ToSummary(s) with { PortCount = counts.GetValueOrDefault(s.Id) }).ToList();
    }

    public async Task<SwitchDetailDto> GetAsync(int id, CancellationToken ct) =>
        ToDetail(await Load(id, ct));

    public async Task<SwitchDetailDto> CreateAsync(SwitchRequest req, CancellationToken ct)
    {
        var errors = ValidateBasics(req);
        var ip = NormalizeIp(req.ManagementIp);
        if (ip is not null && await switches.GetByIpAsync(ip, ct) is not null)
            throw new ConflictException($"A switch with management IP {ip} already exists.");

        var now = DateTime.UtcNow;
        var sw = new Switch { CreatedAt = now, UpdatedAt = now };
        Apply(sw, req, ip);
        await ApplyCredentialAsync(sw, req, errors, ct);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
        await switches.AddAsync(sw, ct);
        await switches.SaveAsync(ct);
        return ToDetail(sw);
    }

    public async Task<SwitchDetailDto> UpdateAsync(int id, SwitchRequest req, CancellationToken ct)
    {
        var sw = await Load(id, ct);
        var errors = ValidateBasics(req);
        var ip = NormalizeIp(req.ManagementIp);
        if (ip is not null && await switches.GetByIpAsync(ip, ct) is { } other && other.Id != id)
            throw new ConflictException($"A switch with management IP {ip} already exists.");
        Apply(sw, req, ip);
        await ApplyCredentialAsync(sw, req, errors, ct);
        if (errors.Count > 0) throw new ValidationFailedException(errors);
        sw.UpdatedAt = DateTime.UtcNow;
        await switches.SaveAsync(ct);
        return ToDetail(sw);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var sw = await Load(id, ct);
        await switches.DeleteAsync(sw, ct);
        await switches.SaveAsync(ct);
    }

    /// <summary>Tests credentials typed into the Add/Edit form (nothing is saved).</summary>
    public async Task<TestConnectionResult> TestUnsavedAsync(TestConnectionRequest req, CancellationToken ct)
    {
        var ip = NormalizeIp(req.ManagementIp) ?? throw new ValidationFailedException("managementIp", "Enter a valid IPv4 or IPv6 address.");
        if (TargetPolicy.Check(ip, security.Value.AllowLoopbackTargets) is { } blocked) throw new ValidationFailedException("managementIp", blocked);
        SnmpCredentials creds;
        if (req.CredentialProfileId is { } pid)
        {
            var p = await profiles.GetAsync(pid, ct) ?? throw new ValidationFailedException("credentialProfileId", "Credential profile not found.");
            creds = codec.Decrypt(p.Version, p.AuthProtocol, p.PrivProtocol, p.ContextName, p.EncryptedSecrets);
        }
        else
        {
            SnmpCredentials? existing = null;
            if (req.SwitchId is { } sid && await switches.GetAsync(sid, ct) is { } existingSw)
                existing = ResolveCredentials(existingSw);
            var input = req.Credential ?? throw new ValidationFailedException("credential", "Credentials are required.");
            creds = CredentialCodec.Merge(input, existing);
            var errs = CredentialCodec.Validate(creds);
            if (errs.Count > 0) throw new ValidationFailedException(errs);
        }
        return await snmp.TestAsync(ip, creds, req.Manufacturer, ct);
    }

    /// <summary>Tests a saved switch with its stored credentials and records the result.</summary>
    public async Task<TestConnectionResult> TestSavedAsync(int id, CancellationToken ct)
    {
        var sw = await Load(id, ct);
        var creds = ResolveCredentials(sw) ?? throw new ValidationFailedException("credential", "This switch has no credentials configured.");
        var result = await snmp.TestAsync(sw.ManagementIp, creds, sw.Manufacturer, ct);
        sw.Status = result.Success ? SwitchStatus.Reachable : SwitchStatus.Unreachable;
        if (result.Success)
        {
            sw.SysName = result.SysName; sw.SysDescr = Trunc(result.SysDescr, 1000); sw.SysObjectId = result.SysObjectId;
            sw.DetectedManufacturer = result.DetectedManufacturer;
        }
        await switches.SaveAsync(ct);
        return result;
    }

    /// <summary>All switches with their effective credentials as an AES-256-GCM encrypted file (see <see cref="Import.SecureExport"/>).</summary>
    public async Task<byte[]> ExportEncryptedWithCredentialsAsync(string passphrase, CancellationToken ct)
    {
        if (Import.SecureExport.PassphraseProblem(passphrase) is { } bad) throw new ValidationFailedException("passphrase", bad);
        var list = await switches.ListAsync(ct);
        return Import.SecureExport.Encrypt(Import.ImportTemplateBuilder.ExportSwitchesWithCredentials(list.Select(s => (s, ResolveCredentials(s)))), passphrase);
    }

    public SnmpCredentials? ResolveCredentials(Switch sw)
    {
        if (sw.CredentialProfile is { } p) return codec.Decrypt(p.Version, p.AuthProtocol, p.PrivProtocol, p.ContextName, p.EncryptedSecrets);
        if (sw.Credential is { } c) return codec.Decrypt(c.Version, c.AuthProtocol, c.PrivProtocol, c.ContextName, c.EncryptedSecrets);
        return null;
    }

    // ---- internals ----

    private async Task<Switch> Load(int id, CancellationToken ct) =>
        await switches.GetAsync(id, ct) ?? throw new NotFoundException($"Switch {id} not found.");

    private static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];

    private Dictionary<string, string[]> ValidateBasics(SwitchRequest r)
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(r.Name)) e["name"] = ["Name is required."];
        else if (r.Name.Trim().Length > 100) e["name"] = ["Name must be 100 characters or fewer."];
        if (NormalizeIp(r.ManagementIp) is not { } nip) e["managementIp"] = ["Enter a valid IPv4 or IPv6 address."];
        else if (TargetPolicy.Check(nip, security.Value.AllowLoopbackTargets) is { } bad) e["managementIp"] = [bad];
        if (!Enum.IsDefined(r.Manufacturer)) e["manufacturer"] = ["Unknown manufacturer."];
        if (r.Manufacturer == Manufacturer.OtherGeneric && r.ManufacturerOther?.Length > 100) e["manufacturerOther"] = ["Too long."];
        return e;
    }

    private static void Apply(Switch sw, SwitchRequest r, string? ip)
    {
        sw.Name = r.Name.Trim();
        sw.ManagementIp = ip ?? sw.ManagementIp;
        sw.Manufacturer = r.Manufacturer;
        sw.ManufacturerOther = r.Manufacturer == Manufacturer.OtherGeneric ? r.ManufacturerOther?.Trim() : null;
        sw.Model = Blank(r.Model); sw.Location = Blank(r.Location); sw.Notes = Blank(r.Notes);
        sw.UseAsArpSource = r.UseAsArpSource; sw.IsWirelessController = r.IsWirelessController;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private async Task ApplyCredentialAsync(Switch sw, SwitchRequest req, Dictionary<string, string[]> errors, CancellationToken ct)
    {
        if (req.CredentialProfileId is { } pid)
        {
            var p = await profiles.GetAsync(pid, ct);
            if (p is null) { errors["credentialProfileId"] = ["Credential profile not found."]; return; }
            sw.CredentialProfileId = pid; sw.CredentialProfile = p;
            return;
        }
        sw.CredentialProfileId = null; sw.CredentialProfile = null;
        if (req.Credential is null)
        {
            if (sw.Credential is null) errors["credential"] = ["SNMP credentials or a credential profile are required."];
            return;
        }
        var existing = sw.Credential is { } c ? codec.Decrypt(c.Version, c.AuthProtocol, c.PrivProtocol, c.ContextName, c.EncryptedSecrets) : null;
        var creds = CredentialCodec.Merge(req.Credential, existing);
        foreach (var kv in CredentialCodec.Validate(creds)) errors[kv.Key] = kv.Value;
        if (errors.Count > 0) return;
        sw.Credential ??= new SwitchCredential();
        sw.Credential.Version = creds.Version; sw.Credential.AuthProtocol = creds.AuthProtocol;
        sw.Credential.PrivProtocol = creds.PrivProtocol; sw.Credential.ContextName = creds.ContextName;
        sw.Credential.EncryptedSecrets = codec.Encrypt(creds);
    }

    internal static SwitchSummaryDto ToSummary(Switch s) => new(
        s.Id, s.Name, s.ManagementIp, s.Manufacturer, ManufacturerLabel(s), s.DetectedManufacturer,
        VendorDetector.Mismatch(s.Manufacturer, s.DetectedManufacturer), s.Model, s.Status, s.LastScanAt,
        s.UseAsArpSource, s.Location, s.CredentialProfileId, s.CredentialProfile?.Name, 0, s.IsWirelessController);

    private SwitchDetailDto ToDetail(Switch s)
    {
        CredentialSummary? cs = null;
        var creds = ResolveCredentials(s);
        if (creds is not null) cs = CredentialCodec.Summarize(creds);
        return new SwitchDetailDto(
            s.Id, s.Name, s.ManagementIp, s.Manufacturer, ManufacturerLabel(s), s.ManufacturerOther, s.DetectedManufacturer,
            VendorDetector.Mismatch(s.Manufacturer, s.DetectedManufacturer), s.Model, s.Location, s.Notes, s.UseAsArpSource,
            s.Status, s.LastScanAt, s.SysName, s.SysDescr, s.SysObjectId, s.CredentialProfileId, s.CredentialProfile?.Name, cs,
            s.CreatedAt, s.UpdatedAt, s.IsWirelessController);
    }

    private static string ManufacturerLabel(Switch s) =>
        s.Manufacturer == Manufacturer.OtherGeneric && !string.IsNullOrWhiteSpace(s.ManufacturerOther)
            ? s.ManufacturerOther!
            : s.Manufacturer == Manufacturer.AutoDetect && s.DetectedManufacturer is { } d
                ? $"{VendorDetector.DisplayName(d)} (detected)"
                : VendorDetector.DisplayName(s.Manufacturer);
}
