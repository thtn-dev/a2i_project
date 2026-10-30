using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Shell.Abstractions.Tenancy;

/// <summary>
/// Base class for module DbContexts holding tenant data:
/// <list type="bullet">
/// <item>Pool isolation: every <see cref="IMustHaveTenant"/> entity gets the query filter <c>TenantId == current tenant</c>.</item>
/// <item>Writes: <c>TenantId</c> is stamped on insert and cannot be changed; saving without a tenant throws.</item>
/// </list>
/// Silo isolation (own database) is handled by the connection string, see <c>AddModuleDbContext</c>.
/// </summary>
public abstract class TenantDbContext(DbContextOptions options, ITenantContext tenantContext) : DbContext(options)
{
    public const string TenantFilterName = "Tenant";

    /// <summary>Referenced by the query filters; EF re-evaluates it for every query of this instance.</summary>
    protected Guid CurrentTenantId => tenantContext.Current?.Id ?? Guid.Empty;

    /// <summary>Configure the module's model here. Tenant filters are added afterwards.</summary>
    protected abstract void ConfigureModel(ModelBuilder modelBuilder);

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureModel(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            // Filters can only be declared on the root of an inheritance hierarchy.
            if (entityType.BaseType is not null || !typeof(IMustHaveTenant).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var entity = Expression.Parameter(entityType.ClrType, "e");
            var filter = Expression.Lambda(
                Expression.Equal(
                    Expression.Property(entity, nameof(IMustHaveTenant.TenantId)),
                    Expression.Property(Expression.Constant(this), nameof(CurrentTenantId))),
                entity);

            // Named filter: modules can add their own filters (e.g. soft delete) next to it.
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(TenantFilterName, filter);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantRules()
    {
        foreach (var entry in ChangeTracker.Entries<IMustHaveTenant>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var entityName = entry.Metadata.ClrType.Name;
            var tenant = tenantContext.Current
                ?? throw new InvalidOperationException(
                    $"Cannot save '{entityName}': no tenant has been resolved for the current operation.");

            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TenantId == Guid.Empty)
                {
                    entry.Entity.TenantId = tenant.Id;
                }
                else if (entry.Entity.TenantId != tenant.Id)
                {
                    throw new InvalidOperationException(
                        $"Cannot add '{entityName}' for tenant {entry.Entity.TenantId} while the current tenant is {tenant.Id}.");
                }

                continue;
            }

            // Modified or Deleted: the row must belong to the current tenant and keep its TenantId.
            var tenantId = entry.Property(nameof(IMustHaveTenant.TenantId));
            if (!Equals(tenantId.OriginalValue, tenant.Id) || !Equals(tenantId.CurrentValue, tenant.Id))
            {
                throw new InvalidOperationException(
                    $"Cannot change TenantId of '{entityName}' or modify a row of another tenant (current tenant {tenant.Id}).");
            }
        }
    }
}
