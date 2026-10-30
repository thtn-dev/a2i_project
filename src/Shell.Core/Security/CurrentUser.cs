using Microsoft.AspNetCore.Authorization;
using Shell.Abstractions.Security;

namespace Shell.Core.Security;

/// <summary>Scoped holder of the caller. Set only by the authentication layer, once the token and membership are checked.</summary>
public sealed class CurrentUser : ICurrentUser
{
    public Guid? UserId { get; private set; }

    public string? Role { get; private set; }

    public void Set(Guid userId, string role)
    {
        if (UserId is not null && UserId != userId)
        {
            throw new InvalidOperationException($"The user of this scope is already {UserId}; it cannot change to {userId}.");
        }

        UserId = userId;
        Role = role;
    }
}

/// <summary>Requirement behind <see cref="TenantPolicies"/>.</summary>
/// <param name="AdminOnly">True: owner or admin of the tenant. False: any active member.</param>
internal sealed record TenantAccessRequirement(bool AdminOnly) : IAuthorizationRequirement;

/// <summary>
/// Active members of the current tenant pass (owners and admins only for <see cref="TenantPolicies.Admin"/>).
/// <see cref="ICurrentUser"/> is only set once the token and the membership have been checked against the tenant.
/// </summary>
internal sealed class TenantAccessHandler(ICurrentUser currentUser) : AuthorizationHandler<TenantAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantAccessRequirement requirement)
    {
        if (currentUser.IsAuthenticated && (!requirement.AdminOnly || currentUser.Role is TenantRoles.Owner or TenantRoles.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
