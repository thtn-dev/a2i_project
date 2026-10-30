using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shell.Abstractions;

namespace Shell.Identity.Data;

/// <summary>
/// Authentication data: one global user store, tenant memberships, OpenIddict and Data Protection tables.
/// Lives in the catalog DB (schema <c>identity</c>), never in a tenant DB, so moving a tenant between Pool and Silo
/// does not touch accounts, memberships or tokens.
/// </summary>
public sealed partial class AuthDbContext(DbContextOptions<AuthDbContext> options)
    : IdentityUserContext<User, Guid>(options), IDataProtectionKeyContext
{
    public const string Schema = "identity";

    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();

    public DbSet<TenantInvitation> Invitations => Set<TenantInvitation>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.UseOpenIddict<Guid>();

        builder.Entity<User>(user =>
        {
            user.ToTable("users");
            user.HasIndex(u => u.ManagedByTenantId);
        });
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
        builder.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreApplication<Guid>>().ToTable("openiddict_applications");
        builder.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreAuthorization<Guid>>().ToTable("openiddict_authorizations");
        builder.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreScope<Guid>>().ToTable("openiddict_scopes");
        builder.Entity<OpenIddict.EntityFrameworkCore.Models.OpenIddictEntityFrameworkCoreToken<Guid>>().ToTable("openiddict_tokens");

        builder.Entity<TenantMembership>(membership =>
        {
            membership.ToTable("tenant_memberships");
            membership.HasKey(m => new { m.TenantId, m.UserId });
            membership.HasIndex(m => m.UserId);
            membership.Property(m => m.Role).HasMaxLength(20);
            membership.Property(m => m.Status).HasMaxLength(20)
                .HasConversion(v => v.ToString().ToLowerInvariant(), v => Enum.Parse<MembershipStatus>(v, true));
            // No FK to catalog.tenants: the tenant may be locked or moved, memberships stay.
            membership.HasOne<User>().WithMany().HasForeignKey(m => m.UserId);
        });

        builder.Entity<TenantInvitation>(invitation =>
        {
            invitation.ToTable("tenant_invitations");
            invitation.Property(i => i.NormalizedEmail).HasMaxLength(256);
            invitation.Property(i => i.Role).HasMaxLength(20);
            invitation.Property(i => i.TokenHash).HasMaxLength(64);
            invitation.HasIndex(i => i.TokenHash).IsUnique();
            invitation.HasIndex(i => new { i.TenantId, i.NormalizedEmail });
        });

        builder.Entity<AuditEvent>(audit =>
        {
            audit.ToTable("audit_events");
            audit.Property(a => a.Type).HasMaxLength(100);
            audit.Property(a => a.IpAddress).HasMaxLength(64);
            audit.Property(a => a.UserAgent).HasMaxLength(512);
            audit.Property(a => a.Data).HasColumnType("jsonb");
            audit.HasIndex(a => new { a.TenantId, a.OccurredAt });
            audit.HasIndex(a => new { a.UserId, a.OccurredAt });
        });

        // snake_case columns, like the rest of the database.
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(SnakeCase().Replace(property.Name, "_$1").ToLowerInvariant());
            }
        }
    }

    [GeneratedRegex("(?<=[a-z0-9])([A-Z])")]
    private static partial Regex SnakeCase();
}

/// <summary>A person, once for the whole platform. <see cref="IdentityUser{TKey}.Id"/> is the stable id (<c>sub</c>) business data stores.</summary>
public sealed class User : IdentityUser<Guid>
{
    /// <summary>Set for enterprise-managed accounts: such a user can only access this tenant. Null: personal account.</summary>
    public Guid? ManagedByTenantId { get; set; }

    /// <summary>Disabled accounts cannot sign in nor use any token.</summary>
    public bool IsDisabled { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public enum MembershipStatus
{
    Active,
    Suspended,
    Revoked,
}

/// <summary>User ↔ tenant link. Only <see cref="MembershipStatus.Active"/> members get tokens for (and access to) the tenant.</summary>
public sealed class TenantMembership
{
    public Guid TenantId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>One of <see cref="Shell.Abstractions.Security.TenantRoles"/>.</summary>
    public required string Role { get; set; }

    public MembershipStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Invitation to join a tenant. The token itself is only sent by email; only its SHA-256 is stored, so a database leak
/// does not leak usable invitations. Single use, expires.
/// </summary>
public sealed class TenantInvitation
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    /// <summary>Upper-case email (Identity normalisation): only the account with this email can accept.</summary>
    public required string NormalizedEmail { get; set; }

    public required string Role { get; set; }

    public required string TokenHash { get; set; }

    public Guid InvitedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public Guid? AcceptedBy { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public bool IsPending(DateTimeOffset now) => AcceptedAt is null && RevokedAt is null && ExpiresAt > now;
}

/// <summary>Security-relevant event (sign-ins, MFA, memberships, invitations, account changes). Append-only.</summary>
public sealed class AuditEvent
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Dotted name, e.g. <c>login.failed</c>, <c>membership.revoked</c>.</summary>
    public required string Type { get; set; }

    /// <summary>Who did it (null: the user themselves, a command, or anonymous).</summary>
    public Guid? ActorId { get; set; }

    /// <summary>Whose account it concerns.</summary>
    public Guid? UserId { get; set; }

    public Guid? TenantId { get; set; }

    public string? IpAddress { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>Extra details as JSON.</summary>
    public string? Data { get; set; }
}

internal sealed class AuthDbContextFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.CreateDesignTimeOptions<AuthDbContext>(AuthDbContext.Schema));
}
