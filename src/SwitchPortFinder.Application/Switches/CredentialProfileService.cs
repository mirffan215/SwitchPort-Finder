using SwitchPortFinder.Application.Abstractions;
using SwitchPortFinder.Application.Common;
using SwitchPortFinder.Domain.Entities;

namespace SwitchPortFinder.Application.Switches;

public class CredentialProfileService(ICredentialProfileRepository repo, CredentialCodec codec)
{
    public async Task<List<CredentialProfileDto>> ListAsync(CancellationToken ct)
    {
        var list = await repo.ListAsync(ct);
        var result = new List<CredentialProfileDto>();
        foreach (var p in list) result.Add(await ToDto(p, ct));
        return result;
    }

    public async Task<CredentialProfileDto> CreateAsync(CredentialProfileRequest req, CancellationToken ct)
    {
        var name = Validate(req);
        if (await repo.GetByNameAsync(name, ct) is not null) throw new ConflictException($"A profile named '{name}' already exists.");
        var creds = CredentialCodec.Merge(req.Credential, null);
        var errs = CredentialCodec.Validate(creds);
        if (errs.Count > 0) throw new ValidationFailedException(errs);
        var now = DateTime.UtcNow;
        var p = new CredentialProfile { Name = name, Description = req.Description?.Trim(), CreatedAt = now, UpdatedAt = now };
        Fill(p, creds);
        await repo.AddAsync(p, ct);
        await repo.SaveAsync(ct);
        return await ToDto(p, ct);
    }

    public async Task<CredentialProfileDto> UpdateAsync(int id, CredentialProfileRequest req, CancellationToken ct)
    {
        var p = await repo.GetAsync(id, ct) ?? throw new NotFoundException("Credential profile not found.");
        var name = Validate(req);
        if (await repo.GetByNameAsync(name, ct) is { } other && other.Id != id) throw new ConflictException($"A profile named '{name}' already exists.");
        var existing = codec.Decrypt(p.Version, p.AuthProtocol, p.PrivProtocol, p.ContextName, p.EncryptedSecrets);
        var creds = CredentialCodec.Merge(req.Credential, existing);
        var errs = CredentialCodec.Validate(creds);
        if (errs.Count > 0) throw new ValidationFailedException(errs);
        p.Name = name; p.Description = req.Description?.Trim(); p.UpdatedAt = DateTime.UtcNow;
        Fill(p, creds);
        await repo.SaveAsync(ct);
        return await ToDto(p, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var p = await repo.GetAsync(id, ct) ?? throw new NotFoundException("Credential profile not found.");
        if (await repo.CountSwitchesUsingAsync(id, ct) is var n and > 0)
            throw new ConflictException($"Profile is used by {n} switch(es). Reassign them first.");
        await repo.DeleteAsync(p, ct);
        await repo.SaveAsync(ct);
    }

    private void Fill(CredentialProfile p, Snmp.SnmpCredentials c)
    {
        p.Version = c.Version; p.AuthProtocol = c.AuthProtocol; p.PrivProtocol = c.PrivProtocol; p.ContextName = c.ContextName;
        p.EncryptedSecrets = codec.Encrypt(c);
    }

    private static string Validate(CredentialProfileRequest r)
    {
        var name = r.Name?.Trim() ?? "";
        if (name.Length == 0) throw new ValidationFailedException("name", "Profile name is required.");
        if (name.Length > 100) throw new ValidationFailedException("name", "Profile name must be 100 characters or fewer.");
        return name;
    }

    private async Task<CredentialProfileDto> ToDto(CredentialProfile p, CancellationToken ct)
    {
        var c = codec.Decrypt(p.Version, p.AuthProtocol, p.PrivProtocol, p.ContextName, p.EncryptedSecrets);
        return new CredentialProfileDto(p.Id, p.Name, p.Description, CredentialCodec.Summarize(c), await repo.CountSwitchesUsingAsync(p.Id, ct), p.UpdatedAt);
    }
}
