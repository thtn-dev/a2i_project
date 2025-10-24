using System.Reflection;
using A2I.Core.Entities;
using BuildingBlocks.SharedKernel.Common;
using Microsoft.EntityFrameworkCore;

namespace A2I.Infrastructure.Database;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global soft-delete filter
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var p = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var prop = System.Linq.Expressions.Expression.Property(p, nameof(ISoftDelete.IsDeleted));
                var cond = System.Linq.Expressions.Expression.Equal(prop, System.Linq.Expressions.Expression.Constant(false));
                var lambda = System.Linq.Expressions.Expression.Lambda(cond, p);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }

        ConfigureAuditFields(modelBuilder);
    }

    private static void ConfigureAuditFields(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IAuditableEntity.CreatedAt))
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAdd();

                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IAuditableEntity.UpdatedAt))
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAddOrUpdate();
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry is { State: EntityState.Added, Entity: IEntityBase<Guid> gen } &&
                gen.Id == Guid.Empty)
            {
                gen.Id = BuildingBlocks.Utils.Helpers.IdGenHelper.NewGuidId();
            }

            if (entry.Entity is IAuditableEntity aud)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        aud.CreatedAt = utcNow;
                        aud.UpdatedAt = utcNow;
                        break;
                    case EntityState.Modified:
                        aud.UpdatedAt = utcNow;
                        entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                        break;
                    case EntityState.Detached:
                    case EntityState.Unchanged:
                    case EntityState.Deleted:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (entry is { Entity: ISoftDelete soft, State: EntityState.Deleted })
            {
                entry.State = EntityState.Modified;
                soft.IsDeleted = true;
                soft.DeletedAt = utcNow;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}
