using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Shell.Abstractions.Security;
using Shell.Core.Tenancy;
using Shell.Identity.Audit;
using Shell.Identity.Data;
using Shell.Identity.Email;

namespace Shell.Identity.Access;

public enum IamOutcome
{
    Ok,
    Invalid,
    NotFound,
    Forbidden,
    Conflict,
}

public sealed record IamResult(IamOutcome Outcome, string? Detail = null)
{
    public static readonly IamResult Ok = new(IamOutcome.Ok);
}

/// <param name="ActorId">Member performing the change.</param>
/// <param name="ActorRole">Role of the actor in the tenant (only owners may touch owners).</param>
public sealed record IamActor(Guid ActorId, string ActorRole)
{
    public bool IsOwner => ActorRole == TenantRoles.Owner;
}

/// <summary>
/// Lifecycle of tenant memberships and invitations. Rules: only owners grant, change or remove the owner role; a tenant always
/// keeps at least one active owner; revoking a member also revokes that member's grants for the tenant.
/// </summary>
public sealed class MembershipService(
    AuthDbContext db,
    UserManager<User> users,
    TenantAccessService access,
    GrantRevoker grants,
    TenantStore tenants,
    IAccountEmailSender emails,
    AccountLinks links,
    AuditLog audit,
    TimeProvider time)
{
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

    public async Task<IamResult> ChangeRoleAsync(Guid tenantId, Guid userId, string role, IamActor actor, CancellationToken ct)
    {
        if (!TenantRoles.All.Contains(role))
        {
            return new(IamOutcome.Invalid, $"Role must be one of: {string.Join(", ", TenantRoles.All)}.");
        }

        var membership = await db.Memberships.FindAsync([tenantId, userId], ct);
        if (membership is null)
        {
            return new(IamOutcome.NotFound);
        }

        if (membership.Role == role)
        {
            return IamResult.Ok;
        }

        if ((membership.Role == TenantRoles.Owner || role == TenantRoles.Owner) && !actor.IsOwner)
        {
            return new(IamOutcome.Forbidden, "Only an owner can grant or remove the owner role.");
        }

        if (await IsLastActiveOwnerAsync(membership, ct))
        {
            return new(IamOutcome.Conflict, "The tenant must keep at least one active owner.");
        }

        audit.Add(AuditEvents.MembershipRoleChanged, userId, tenantId, actor.ActorId, new { from = membership.Role, to = role });
        membership.Role = role;
        membership.UpdatedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        access.Invalidate(tenantId, userId);
        return IamResult.Ok;
    }

    /// <summary>Removes access now: the membership, the user's refresh tokens for this tenant, and (within the cache delay) its access tokens.</summary>
    public async Task<IamResult> RevokeAsync(Guid tenantId, Guid userId, IamActor actor, CancellationToken ct)
    {
        var membership = await db.Memberships.FindAsync([tenantId, userId], ct);
        if (membership is null)
        {
            return new(IamOutcome.NotFound);
        }

        if (membership.Role == TenantRoles.Owner && !actor.IsOwner)
        {
            return new(IamOutcome.Forbidden, "Only an owner can remove an owner.");
        }

        if (await IsLastActiveOwnerAsync(membership, ct))
        {
            return new(IamOutcome.Conflict, "The tenant must keep at least one active owner.");
        }

        if (membership.Status != MembershipStatus.Revoked)
        {
            membership.Status = MembershipStatus.Revoked;
            membership.UpdatedAt = time.GetUtcNow();
            audit.Add(AuditEvents.MembershipRevoked, userId, tenantId, actor.ActorId);
            await db.SaveChangesAsync(ct);
        }

        await grants.RevokeAsync(userId, tenantId, ct);
        access.Invalidate(tenantId, userId);
        return IamResult.Ok;
    }

    public async Task<IamResult> RestoreAsync(Guid tenantId, Guid userId, IamActor actor, CancellationToken ct)
    {
        var membership = await db.Memberships.FindAsync([tenantId, userId], ct);
        if (membership is null)
        {
            return new(IamOutcome.NotFound);
        }

        if (membership.Role == TenantRoles.Owner && !actor.IsOwner)
        {
            return new(IamOutcome.Forbidden, "Only an owner can restore an owner.");
        }

        if (membership.Status != MembershipStatus.Active)
        {
            membership.Status = MembershipStatus.Active;
            membership.UpdatedAt = time.GetUtcNow();
            audit.Add(AuditEvents.MembershipRestored, userId, tenantId, actor.ActorId);
            await db.SaveChangesAsync(ct);
        }

        access.Invalidate(tenantId, userId);
        return IamResult.Ok;
    }

    /// <summary>Creates an invitation (replacing a pending one for the same email) and emails its link. Works for new and existing accounts.</summary>
    public async Task<(IamResult Result, TenantInvitation? Invitation)> InviteAsync(
        Guid tenantId, string email, string role, IamActor actor, CancellationToken ct)
    {
        email = email.Trim();
        if (email.Length is < 3 or > 256 || !email.Contains('@'))
        {
            return (new(IamOutcome.Invalid, "A valid email is required."), null);
        }

        if (!TenantRoles.All.Contains(role))
        {
            return (new(IamOutcome.Invalid, $"Role must be one of: {string.Join(", ", TenantRoles.All)}."), null);
        }

        if (role == TenantRoles.Owner && !actor.IsOwner)
        {
            return (new(IamOutcome.Forbidden, "Only an owner can invite an owner."), null);
        }

        var tenant = await tenants.FindByIdAsync(tenantId, ct);
        if (tenant is null)
        {
            return (new(IamOutcome.NotFound), null);
        }

        var normalized = users.NormalizeEmail(email)!;
        var alreadyMember = await db.Memberships.AnyAsync(m => m.TenantId == tenantId && m.Status == MembershipStatus.Active
            && db.Users.Any(u => u.Id == m.UserId && u.NormalizedEmail == normalized), ct);
        if (alreadyMember)
        {
            return (new(IamOutcome.Conflict, "This person is already a member."), null);
        }

        var now = time.GetUtcNow();
        await db.Invitations
            .Where(i => i.TenantId == tenantId && i.NormalizedEmail == normalized && i.AcceptedAt == null && i.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(i => i.RevokedAt, now), ct);

        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = new TenantInvitation
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            NormalizedEmail = normalized,
            Role = role,
            TokenHash = Hash(token),
            InvitedBy = actor.ActorId,
            CreatedAt = now,
            ExpiresAt = now + InvitationLifetime,
        };
        db.Invitations.Add(invitation);
        audit.Add(AuditEvents.InvitationCreated, tenantId: tenantId, actorId: actor.ActorId, data: new { email = normalized, role });
        await db.SaveChangesAsync(ct);

        await emails.SendInvitationAsync(email, tenant.Identifier, role, links.Invitation(token));
        return (IamResult.Ok, invitation);
    }

    public async Task<IamResult> RevokeInvitationAsync(Guid tenantId, Guid invitationId, IamActor actor, CancellationToken ct)
    {
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.Id == invitationId && i.TenantId == tenantId, ct);
        if (invitation is null)
        {
            return new(IamOutcome.NotFound);
        }

        if (invitation.IsPending(time.GetUtcNow()))
        {
            invitation.RevokedAt = time.GetUtcNow();
            audit.Add(AuditEvents.InvitationRevoked, tenantId: tenantId, actorId: actor.ActorId, data: new { email = invitation.NormalizedEmail });
            await db.SaveChangesAsync(ct);
        }

        return IamResult.Ok;
    }

    /// <summary>Pending, unexpired invitation of a token (null otherwise).</summary>
    public async Task<TenantInvitation?> FindPendingAsync(string token, CancellationToken ct)
    {
        var hash = Hash(token);
        var invitation = await db.Invitations.SingleOrDefaultAsync(i => i.TokenHash == hash, ct);
        return invitation is not null && invitation.IsPending(time.GetUtcNow()) ? invitation : null;
    }

    /// <summary>
    /// Accepts an invitation for the signed-in user: the account email must be the invited one, and an account managed by
    /// another tenant cannot join. Re-activates a revoked membership. Proves ownership of the email.
    /// </summary>
    public async Task<IamResult> AcceptAsync(string token, User user, CancellationToken ct)
    {
        var invitation = await FindPendingAsync(token, ct);
        if (invitation is null || await tenants.FindByIdAsync(invitation.TenantId, ct) is not { IsActive: true })
        {
            return new(IamOutcome.Invalid, "This invitation is invalid, expired or already used.");
        }

        if (user.IsDisabled)
        {
            return new(IamOutcome.Forbidden, "This account is disabled.");
        }

        if (!string.Equals(user.NormalizedEmail, invitation.NormalizedEmail, StringComparison.Ordinal))
        {
            return new(IamOutcome.Forbidden, "This invitation was sent to another email address. Sign in with that account.");
        }

        if (user.ManagedByTenantId is { } owner && owner != invitation.TenantId)
        {
            return new(IamOutcome.Forbidden, "This account is managed by an organization and cannot join other tenants.");
        }

        var now = time.GetUtcNow();
        var membership = await db.Memberships.FindAsync([invitation.TenantId, user.Id], ct);
        if (membership is null)
        {
            db.Memberships.Add(new TenantMembership
            {
                TenantId = invitation.TenantId, UserId = user.Id, Role = invitation.Role, Status = MembershipStatus.Active, CreatedAt = now, UpdatedAt = now,
            });
        }
        else
        {
            membership.Role = invitation.Role;
            membership.Status = MembershipStatus.Active;
            membership.UpdatedAt = now;
        }

        invitation.AcceptedAt = now;
        invitation.AcceptedBy = user.Id;
        user.EmailConfirmed = true;
        audit.Add(AuditEvents.InvitationAccepted, user.Id, invitation.TenantId, data: new { role = invitation.Role });
        await db.SaveChangesAsync(ct);

        access.Invalidate(invitation.TenantId, user.Id);
        return IamResult.Ok;
    }

    private async Task<bool> IsLastActiveOwnerAsync(TenantMembership membership, CancellationToken ct) =>
        membership is { Role: TenantRoles.Owner, Status: MembershipStatus.Active }
        && !await db.Memberships.AnyAsync(m => m.TenantId == membership.TenantId && m.UserId != membership.UserId
            && m.Role == TenantRoles.Owner && m.Status == MembershipStatus.Active, ct);

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
