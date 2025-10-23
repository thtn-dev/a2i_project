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

        // Ensure pgcrypto for gen_random_uuid()
        modelBuilder.HasPostgresExtension("pgcrypto");

        // Apply all configs from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Global soft-delete filter
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = System.Linq.Expressions.Expression.Parameter(entityType.ClrType, "e");
                var prop = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
                var condition = System.Linq.Expressions.Expression.Equal(
                    prop,
                    System.Linq.Expressions.Expression.Constant(false));

                var lambda = System.Linq.Expressions.Expression.Lambda(condition, parameter);
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
        
        // Configure audit fields
        ConfigureAuditFields(modelBuilder);
    }

    private static void ConfigureAuditFields(ModelBuilder modelBuilder)
    {
        // This will automatically add audit trail to all entities
        // that implement IAuditableEntity interface
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IAuditableEntity).IsAssignableFrom(entityType.ClrType))
            {
                // Configure CreatedAt to be set automatically on insert
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(IAuditableEntity.CreatedAt))
                    .HasDefaultValueSql("CURRENT_TIMESTAMP")
                    .ValueGeneratedOnAdd();

                // Configure UpdatedAt to be set automatically on update
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

            if (entry.Entity is not ISoftDelete soft) continue;
            if (entry.State != EntityState.Deleted) continue;
            entry.State = EntityState.Modified;
            soft.IsDeleted = true;
            soft.DeletedAt = utcNow;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }
}