namespace Shell.Abstractions.Security;

/// <summary>
/// The caller of the current request, as an active member of the current tenant (scoped).
/// Always set in module endpoints (they require <see cref="TenantPolicies.Member"/>); empty outside a request (e.g. background jobs).
/// </summary>
public interface ICurrentUser
{
    /// <summary>Stable user id (token <c>sub</c>). Store this, never the email, to reference a user from business data.</summary>
    Guid? UserId { get; }

    /// <summary>Role in the current tenant (<see cref="TenantRoles"/>).</summary>
    string? Role { get; }

    bool IsAuthenticated => UserId is not null;
}

/// <summary>Roles a member can have in a tenant.</summary>
public static class TenantRoles
{
    public const string Owner = "owner";
    public const string Admin = "admin";
    public const string Member = "member";

    public static readonly IReadOnlyList<string> All = [Owner, Admin, Member];
}

/// <summary>
/// Authorization policies for module endpoints. Every <c>/api/{module}</c> group already requires <see cref="Member"/>;
/// add <c>.RequireAuthorization(TenantPolicies.Admin)</c> on endpoints reserved to tenant owners and admins.
/// </summary>
public static class TenantPolicies
{
    public const string Member = "tenant-member";
    public const string Admin = "tenant-admin";
}
