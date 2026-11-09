using SwitchPortFinder.Domain.Entities;
using SwitchPortFinder.Application.Snmp;

namespace SwitchPortFinder.Application.Abstractions;

/// <summary>Encrypts/decrypts secrets at rest. Implementations must never log the values.</summary>
public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedText);
}

/// <summary>Hook for future authentication. V1 allows everything.</summary>
public interface IUserAuthorizationService
{
    Task<bool> IsAdminAsync(CancellationToken ct = default);
    string CurrentUserName { get; }
    /// <summary>True when the caller is a signed-in user (always true when authentication is off).</summary>
    bool IsAuthenticated { get; }
}

public interface ISwitchRepository
{
    Task<List<Switch>> ListAsync(CancellationToken ct);
    Task<Switch?> GetAsync(int id, CancellationToken ct);
    Task<Switch?> GetByIpAsync(string ip, CancellationToken ct);
    Task<Dictionary<int, int>> PortCountsAsync(CancellationToken ct);
    Task<Dictionary<string, Switch>> GetByIpsAsync(IEnumerable<string> ips, CancellationToken ct);
    Task AddAsync(Switch sw, CancellationToken ct);
    Task DeleteAsync(Switch sw, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public interface ICredentialProfileRepository
{
    Task<List<CredentialProfile>> ListAsync(CancellationToken ct);
    Task<CredentialProfile?> GetAsync(int id, CancellationToken ct);
    Task<CredentialProfile?> GetByNameAsync(string name, CancellationToken ct);
    Task<int> CountSwitchesUsingAsync(int profileId, CancellationToken ct);
    Task AddAsync(CredentialProfile p, CancellationToken ct);
    Task DeleteAsync(CredentialProfile p, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

public interface IImportBatchRepository
{
    Task AddAsync(ImportBatch batch, CancellationToken ct);
    Task SaveAsync(CancellationToken ct);
}

/// <summary>
/// Read-only SNMP access. There is intentionally NO Set method: the application must never
/// write to network equipment.
/// </summary>
public interface ISnmpClient
{
    Task<IReadOnlyList<SnmpVarBind>> GetAsync(SnmpTarget target, IEnumerable<string> oids, CancellationToken ct);
    Task<IReadOnlyList<SnmpVarBind>> GetNextAsync(SnmpTarget target, IEnumerable<string> oids, CancellationToken ct);
    /// <summary>Walks the subtree below <paramref name="rootOid"/> (GETBULK, GETNEXT fallback).</summary>
    Task<IReadOnlyList<SnmpVarBind>> WalkAsync(SnmpTarget target, string rootOid, CancellationToken ct);
}

public interface IHostnameResolver
{
    /// <summary>Reverse DNS with timeout and caching. Returns null on any failure; never throws.</summary>
    Task<string?> ResolveAsync(string ip, CancellationToken ct);
}

public interface IScanQueue
{
    ValueTask EnqueueAsync(int jobId, CancellationToken ct = default);
}
