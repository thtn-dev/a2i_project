using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shell.Abstractions.Security;
using Shell.Core;
using Shell.Core.Data;
using Shell.Core.Tenancy;
using Shell.Identity.Clients;
using Shell.Identity.Keys;

namespace Shell.Identity.Data;

/// <summary>
/// Identity part of the migrate command: identity schema, token certificates (when auto-generated), one OAuth client per tenant,
/// and demo users when demo tenants are seeded.
/// </summary>
internal sealed class IdentityMigrationStep(
    IServiceScopeFactory scopeFactory,
    IOptions<TenancyOptions> tenancy,
    ILogger<IdentityMigrationStep> logger) : IMigrationStep
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;

        logger.LogInformation("Migrating identity schema");
        await services.GetRequiredService<AuthDbContext>().Database.MigrateAsync(cancellationToken);

        _ = services.GetRequiredService<IdentityKeyRing>(); // Generates the certificates when Identity:Keys:AutoGenerate is on.
        await services.GetRequiredService<TenantClientSynchronizer>().SynchronizeAsync(cancellationToken);

        if (tenancy.Value.SeedDemoTenants)
        {
            await services.GetRequiredService<DemoUserSeeder>().SeedAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Demo users: <c>alice@demo.local</c> (owner of tenant A, member of tenant B) and <c>bob@demo.local</c> (member of tenant A only).
/// </summary>
internal sealed class DemoUserSeeder(
    AuthDbContext db,
    CatalogDbContext catalog,
    UserManager<User> users,
    TimeProvider time,
    ILogger<DemoUserSeeder> logger)
{
    public const string Alice = "alice@demo.local";
    public const string Bob = "bob@demo.local";
    public const string Password = "Demo-Passw0rd";

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var tenantIds = await catalog.TenantDomains
            .Where(d => d.Domain == DemoTenantSeeder.PoolTenantDomain || d.Domain == DemoTenantSeeder.SiloTenantDomain)
            .ToDictionaryAsync(d => d.Domain, d => d.TenantId, cancellationToken);
        if (tenantIds.Count != 2)
        {
            return;
        }

        var tenantA = tenantIds[DemoTenantSeeder.PoolTenantDomain];
        var tenantB = tenantIds[DemoTenantSeeder.SiloTenantDomain];
        await CreateAsync(Alice, [(tenantA, TenantRoles.Owner), (tenantB, TenantRoles.Member)]);
        await CreateAsync(Bob, [(tenantA, TenantRoles.Member)]);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded demo users {Alice} and {Bob} (password {Password})", Alice, Bob, Password);
    }

    private async Task CreateAsync(string email, (Guid TenantId, string Role)[] memberships)
    {
        var now = time.GetUtcNow();
        var user = new User { Id = Guid.CreateVersion7(), UserName = email, Email = email, EmailConfirmed = true, CreatedAt = now };
        var result = await users.CreateAsync(user, Password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException($"Cannot create demo user {email}: {string.Join(" ", result.Errors.Select(e => e.Description))}");
        }

        foreach (var (tenantId, role) in memberships)
        {
            db.Memberships.Add(new TenantMembership
            {
                TenantId = tenantId, UserId = user.Id, Role = role, Status = MembershipStatus.Active, CreatedAt = now, UpdatedAt = now,
            });
        }
    }
}
