using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Shell.Identity.Data;

namespace Shell.Identity.Audit;

/// <summary>Event names written to <c>identity.audit_events</c>.</summary>
public static class AuditEvents
{
    public const string LoginSucceeded = "login.succeeded";
    public const string LoginFailed = "login.failed";
    public const string LoginLockedOut = "login.locked_out";
    public const string MfaEnabled = "mfa.enabled";
    public const string MfaDisabled = "mfa.disabled";
    public const string PasswordReset = "password.reset";
    public const string UserRegistered = "user.registered";
    public const string UserDisabled = "user.disabled";
    public const string UserEnabled = "user.enabled";
    public const string ExternalLoginLinked = "external_login.linked";
    public const string ExternalLoginRemoved = "external_login.removed";
    public const string ExternalLoginRejected = "external_login.rejected";
    public const string AuthorizationDenied = "authorization.denied";
    public const string MembershipRoleChanged = "membership.role_changed";
    public const string MembershipRevoked = "membership.revoked";
    public const string MembershipRestored = "membership.restored";
    public const string InvitationCreated = "invitation.created";
    public const string InvitationRevoked = "invitation.revoked";
    public const string InvitationAccepted = "invitation.accepted";
}

/// <summary>
/// Writes audit events with the caller's IP and user agent. <see cref="Add"/> joins the caller's unit of work (saved with its
/// changes, so the event exists if and only if the change does); <see cref="RecordAsync"/> saves on its own (sign-in attempts).
/// </summary>
public sealed class AuditLog(AuthDbContext db, IHttpContextAccessor http, TimeProvider time)
{
    public void Add(string type, Guid? userId = null, Guid? tenantId = null, Guid? actorId = null, object? data = null)
    {
        var request = http.HttpContext;
        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = time.GetUtcNow(),
            Type = type,
            ActorId = actorId,
            UserId = userId,
            TenantId = tenantId,
            IpAddress = request?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = request?.Request.Headers.UserAgent.ToString() is { Length: > 0 } agent ? agent[..Math.Min(agent.Length, 512)] : null,
            Data = data is null ? null : JsonSerializer.Serialize(data),
        });
    }

    public async Task RecordAsync(string type, Guid? userId = null, Guid? tenantId = null, Guid? actorId = null, object? data = null)
    {
        Add(type, userId, tenantId, actorId, data);
        await db.SaveChangesAsync();
    }
}
