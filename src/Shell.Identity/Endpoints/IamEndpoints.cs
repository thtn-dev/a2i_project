using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Shell.Abstractions.Security;
using Shell.Abstractions.Tenancy;
using Shell.Identity.Access;
using Shell.Identity.Data;

namespace Shell.Identity.Endpoints;

public sealed record MemberDto(Guid UserId, string? Email, string Role, string Status, bool Managed);

public sealed record ChangeRoleRequest(string Role);

public sealed record InviteRequest(string Email, string Role);

public sealed record InvitationDto(Guid Id, string Email, string Role, DateTimeOffset ExpiresAt);

public sealed record AuditEventDto(DateTimeOffset OccurredAt, string Type, Guid? ActorId, Guid? UserId, string? IpAddress, string? Data);

/// <summary>
/// Tenant administration on the tenant's own host (<c>/iam/*</c>): members, invitations, audit.
/// Requires a token of an owner or admin of that tenant.
/// </summary>
internal static class IamEndpoints
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var iam = endpoints.MapGroup("/iam").RequireAuthorization(TenantPolicies.Admin).WithTags("IAM");

        iam.MapGet("/members", ListMembers);
        iam.MapPut("/members/{userId:guid}/role", (Guid userId, ChangeRoleRequest body, IamContext c, CancellationToken ct) =>
            Run(c.Memberships.ChangeRoleAsync(c.TenantId, userId, body.Role, c.Actor, ct)));
        iam.MapPost("/members/{userId:guid}/revoke", (Guid userId, IamContext c, CancellationToken ct) =>
            Run(c.Memberships.RevokeAsync(c.TenantId, userId, c.Actor, ct)));
        iam.MapPost("/members/{userId:guid}/restore", (Guid userId, IamContext c, CancellationToken ct) =>
            Run(c.Memberships.RestoreAsync(c.TenantId, userId, c.Actor, ct)));

        iam.MapGet("/invitations", ListInvitations);
        iam.MapPost("/invitations", Invite);
        iam.MapDelete("/invitations/{id:guid}", (Guid id, IamContext c, CancellationToken ct) =>
            Run(c.Memberships.RevokeInvitationAsync(c.TenantId, id, c.Actor, ct)));

        iam.MapGet("/audit", ListAudit);
    }

    private static async Task<IResult> ListMembers(IamContext c, AuthDbContext db, CancellationToken ct)
    {
        var members = await db.Memberships
            .Where(m => m.TenantId == c.TenantId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Email, m.Role, m.Status, Managed = u.ManagedByTenantId != null })
            .OrderBy(x => x.Email)
            .ToListAsync(ct);
        return Results.Ok(members.Select(x => new MemberDto(x.Id, x.Email, x.Role, x.Status.ToString().ToLowerInvariant(), x.Managed)));
    }

    private static async Task<IResult> ListInvitations(IamContext c, AuthDbContext db, TimeProvider time, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        return Results.Ok(await db.Invitations
            .Where(i => i.TenantId == c.TenantId && i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new InvitationDto(i.Id, i.NormalizedEmail, i.Role, i.ExpiresAt))
            .ToListAsync(ct));
    }

    private static async Task<IResult> Invite(InviteRequest body, IamContext c, CancellationToken ct)
    {
        var (result, invitation) = await c.Memberships.InviteAsync(c.TenantId, body.Email ?? "", body.Role ?? "", c.Actor, ct);
        return result.Outcome == IamOutcome.Ok
            ? Results.Created($"/iam/invitations/{invitation!.Id}", new InvitationDto(invitation.Id, invitation.NormalizedEmail, invitation.Role, invitation.ExpiresAt))
            : ToProblem(result);
    }

    private static async Task<IResult> ListAudit(IamContext c, AuthDbContext db, int? limit, CancellationToken ct) =>
        Results.Ok(await db.AuditEvents
            .Where(a => a.TenantId == c.TenantId)
            .OrderByDescending(a => a.OccurredAt)
            .Take(Math.Clamp(limit ?? 100, 1, 500))
            .Select(a => new AuditEventDto(a.OccurredAt, a.Type, a.ActorId, a.UserId, a.IpAddress, a.Data))
            .ToListAsync(ct));

    private static async Task<IResult> Run(Task<IamResult> operation)
    {
        var result = await operation;
        return result.Outcome == IamOutcome.Ok ? Results.NoContent() : ToProblem(result);
    }

    private static IResult ToProblem(IamResult result) => Results.Problem(detail: result.Detail, statusCode: result.Outcome switch
    {
        IamOutcome.Invalid => StatusCodes.Status400BadRequest,
        IamOutcome.NotFound => StatusCodes.Status404NotFound,
        IamOutcome.Forbidden => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status409Conflict,
    });

}

/// <summary>Tenant and acting admin of an <c>/iam</c> request (both guaranteed by the policy). Scoped service.</summary>
internal sealed class IamContext(ITenantContext tenant, ICurrentUser user, MembershipService memberships)
{
    public Guid TenantId => tenant.Current!.Id;

    public IamActor Actor => new(user.UserId!.Value, user.Role!);

    public MembershipService Memberships => memberships;
}
