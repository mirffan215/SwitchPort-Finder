using Microsoft.AspNetCore.Authorization;
using SwitchPortFinder.Application.Abstractions;

namespace SwitchPortFinder.Api.Infrastructure;

public class AdminOnlyRequirement : IAuthorizationRequirement;

/// <summary>Delegates to IUserAuthorizationService. V1 allows everyone; swap the service when login is added.</summary>
public class AdminOnlyHandler(IUserAuthorizationService auth) : AuthorizationHandler<AdminOnlyRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AdminOnlyRequirement requirement)
    {
        if (await auth.IsAdminAsync()) context.Succeed(requirement);
    }
}

public static class Policies { public const string AdminOnly = "AdminOnly"; }
