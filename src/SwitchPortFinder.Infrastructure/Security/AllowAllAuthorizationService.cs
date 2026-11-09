using SwitchPortFinder.Application.Abstractions;

namespace SwitchPortFinder.Infrastructure.Security;

/// <summary>V1: no login, everything is allowed. Replace this implementation when authentication is added.</summary>
public class AllowAllAuthorizationService : IUserAuthorizationService
{
    public Task<bool> IsAdminAsync(CancellationToken ct = default) => Task.FromResult(true);
    public string CurrentUserName => "system";
}
