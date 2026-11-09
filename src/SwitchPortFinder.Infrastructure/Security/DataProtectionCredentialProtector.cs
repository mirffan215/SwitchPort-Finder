using Microsoft.AspNetCore.DataProtection;
using SwitchPortFinder.Application.Abstractions;

namespace SwitchPortFinder.Infrastructure.Security;

/// <summary>
/// Encrypts secrets with ASP.NET Core Data Protection. Keys are persisted to a configurable folder and,
/// on Windows, wrapped with machine-scope DPAPI (see DependencyInjection). Values are never logged.
/// </summary>
public class DataProtectionCredentialProtector(IDataProtectionProvider provider) : ICredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("SwitchPortFinder.SnmpCredentials.v1");
    public string Protect(string plaintext) => _protector.Protect(plaintext);
    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
