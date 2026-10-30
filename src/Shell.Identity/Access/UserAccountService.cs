using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shell.Identity.Audit;
using Shell.Identity.Data;

namespace Shell.Identity.Access;

/// <summary>Platform-level account operations, run as host commands (<c>dotnet Host.dll disable-user ...</c>).</summary>
public sealed class UserAccountService(
    AuthDbContext db,
    UserManager<User> users,
    GrantRevoker grants,
    TenantAccessService access,
    AuditLog audit)
{
    /// <summary>
    /// Disabling blocks sign-in, revokes every grant of every tenant, and changes the security stamp so existing sessions on the
    /// authorization server end within the stamp validation interval. API access tokens stop at the next membership check.
    /// </summary>
    public async Task<bool> SetDisabledAsync(string email, bool disabled, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            return false;
        }

        user.IsDisabled = disabled;
        audit.Add(disabled ? AuditEvents.UserDisabled : AuditEvents.UserEnabled, user.Id);
        await users.UpdateSecurityStampAsync(user); // Saves the user and the audit event.

        if (disabled)
        {
            await grants.RevokeAsync(user.Id, tenantId: null, ct);
        }

        foreach (var tenantId in await db.Memberships.Where(m => m.UserId == user.Id).Select(m => m.TenantId).ToListAsync(ct))
        {
            access.Invalidate(tenantId, user.Id);
        }

        return true;
    }
}
