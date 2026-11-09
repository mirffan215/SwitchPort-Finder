using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Application.Snmp;
using SwitchPortFinder.Application.Switches;
using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Domain.Enums;

namespace SwitchPortFinder.Application.Import;

public class SwitchImportService(
    ISwitchRepository switches, ICredentialProfileRepository profiles, IImportBatchRepository batches,
    IImportPreviewStore store, CredentialCodec codec, IOptions<ImportOptions> options, IOptions<SecurityOptions> security, IUserAuthorizationService auth,
    ILogger<SwitchImportService> log)
{
    public async Task<ImportPreviewDto> PreviewAsync(Stream file, string fileName, ImportMode mode, CancellationToken ct)
    {
        var opt = options.Value;
        var raw = ImportFileReader.Read(file, fileName, opt.MaxRows);
        var profileList = await profiles.ListAsync(ct);
        var parsed = ImportRowValidator.Validate(raw, profileList.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
            ip => TargetPolicy.Check(ip, security.Value.AllowLoopbackTargets));
        var existing = await switches.GetByIpsAsync(parsed.Where(p => p.Errors.Count == 0).Select(p => p.Ip), ct);
        var results = Classify(parsed, existing, mode);
        var id = store.Put(new StoredImport(Path.GetFileName(fileName), parsed), TimeSpan.FromMinutes(opt.PreviewTtlMinutes));
        log.LogInformation("Import preview for {File}: {Rows} rows", Path.GetFileName(fileName), parsed.Count);
        return Summarize(id, Path.GetFileName(fileName), mode, results);
    }

    public async Task<ImportCommitResult> CommitAsync(string previewId, ImportMode mode, CancellationToken ct)
    {
        var stored = store.Take(previewId) ?? throw new NotFoundException("Import preview expired or not found. Upload the file again.");
        var profileMap = (await profiles.ListAsync(ct)).ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var existing = await switches.GetByIpsAsync(stored.Rows.Where(r => r.Errors.Count == 0).Select(r => r.Ip), ct);
        var statuses = Classify(stored.Rows, existing, mode);
        var now = DateTime.UtcNow;
        int created = 0, updated = 0, skipped = 0, errors = 0;
        var touched = new List<Switch>();

        foreach (var (row, res) in stored.Rows.Zip(statuses))
        {
            switch (res.Status)
            {
                case ImportRowStatus.Error: errors++; continue;
                case ImportRowStatus.Skipped: skipped++; continue;
            }
            var sw = res.Status == ImportRowStatus.Update ? existing[row.Ip] : new Switch { CreatedAt = now, ManagementIp = row.Ip };
            sw.Name = row.Name; sw.Manufacturer = row.Manufacturer;
            if (row.Manufacturer != Manufacturer.OtherGeneric) sw.ManufacturerOther = null;
            if (row.Model is not null) sw.Model = row.Model;
            if (row.Location is not null) sw.Location = row.Location;
            if (row.Notes is not null) sw.Notes = row.Notes;
            if (row.UseAsArpSource is { } arp) sw.UseAsArpSource = arp;
            sw.UpdatedAt = now;
            if (row.ProfileName is not null)
            {
                var p = profileMap[row.ProfileName];
                sw.CredentialProfileId = p.Id; sw.CredentialProfile = p; sw.Credential = null;
            }
            else if (row.InlineCredentials is { } inline)
            {
                var merged = inline;
                if (sw.Credential is { } cur && cur.Version == inline.Version)
                {
                    var old = codec.Decrypt(cur.Version, cur.AuthProtocol, cur.PrivProtocol, cur.ContextName, cur.EncryptedSecrets);
                    merged = inline with
                    {
                        Community = inline.Community ?? old.Community, Username = inline.Username ?? old.Username,
                        AuthPassword = inline.AuthPassword ?? old.AuthPassword, PrivPassword = inline.PrivPassword ?? old.PrivPassword
                    };
                }
                sw.CredentialProfileId = null; sw.CredentialProfile = null;
                sw.Credential ??= new SwitchCredential();
                sw.Credential.Version = merged.Version; sw.Credential.AuthProtocol = merged.AuthProtocol;
                sw.Credential.PrivProtocol = merged.PrivProtocol; sw.Credential.ContextName = merged.ContextName;
                sw.Credential.EncryptedSecrets = codec.Encrypt(merged);
            }
            if (res.Status == ImportRowStatus.Update) updated++;
            else { created++; await switches.AddAsync(sw, ct); }
            touched.Add(sw);
        }

        var batch = new ImportBatch
        {
            FileName = stored.FileName, Mode = mode, ImportedBy = auth.CurrentUserName, CreatedAt = now,
            TotalRows = stored.Rows.Count, CreatedCount = created, UpdatedCount = updated, SkippedCount = skipped, ErrorCount = errors
        };
        await batches.AddAsync(batch, ct);
        await switches.SaveAsync(ct);   // single batched save for the whole file
        foreach (var s in touched) s.ImportBatchId = batch.Id;
        await switches.SaveAsync(ct);
        log.LogInformation("Import committed from {File}: {Created} created, {Updated} updated, {Skipped} skipped, {Errors} errors",
            stored.FileName, created, updated, skipped, errors);
        return new ImportCommitResult(batch.Id, batch.TotalRows, created, updated, skipped, errors, touched.Select(s => s.Id).ToList());
    }

    private static List<ImportRowResult> Classify(IReadOnlyList<ParsedImportRow> rows, IReadOnlyDictionary<string, Switch> existing, ImportMode mode)
    {
        var list = new List<ImportRowResult>(rows.Count);
        foreach (var r in rows)
        {
            var messages = new List<string>(r.Errors); messages.AddRange(r.Warnings);
            ImportRowStatus status;
            if (r.Errors.Count > 0) status = ImportRowStatus.Error;
            else if (existing.TryGetValue(r.Ip, out var sw))
            {
                if (mode == ImportMode.SkipExisting) { status = ImportRowStatus.Skipped; messages.Add($"Switch already exists ('{sw.Name}'); skipped."); }
                else status = ImportRowStatus.Update;
            }
            else
            {
                status = ImportRowStatus.New;
                if (r.ProfileName is null && r.InlineCredentials is null)
                { status = ImportRowStatus.Error; messages.Add("New switch needs SNMP credentials or a CredentialProfile."); }
            }
            var version = r.ProfileName is not null ? "profile" : r.InlineCredentials?.Version switch { SnmpVersion.V2c => "v2c", SnmpVersion.V3 => "v3", _ => "—" };
            list.Add(new ImportRowResult(r.RowNumber, r.Name, r.Ip, VendorDetector.DisplayName(r.Manufacturer), version, r.ProfileName, status, messages));
        }
        return list;
    }

    private static ImportPreviewDto Summarize(string id, string file, ImportMode mode, List<ImportRowResult> rows) => new(
        id, file, mode, rows.Count,
        rows.Count(r => r.Status == ImportRowStatus.New), rows.Count(r => r.Status == ImportRowStatus.Update),
        rows.Count(r => r.Status == ImportRowStatus.Skipped), rows.Count(r => r.Status == ImportRowStatus.Error), rows);
}
