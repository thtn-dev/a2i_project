using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shell.Abstractions;

namespace Shell.Core.Tenancy;

/// <summary>Master (catalog) database: always the fixed <c>ConnectionStrings:Catalog</c>, never tenant-dependent.</summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public const string Schema = "catalog";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Id).HasColumnName("id");
            tenant.Property(t => t.Name).HasColumnName("name").HasMaxLength(200);
            tenant.Property(t => t.IsActive).HasColumnName("is_active");
            tenant.Property(t => t.IsIsolatedDb).HasColumnName("is_isolated_db");
            tenant.Property(t => t.ConnectionString).HasColumnName("connection_string");
            tenant.HasMany(t => t.Domains).WithOne(d => d.Tenant).HasForeignKey(d => d.TenantId);
        });

        modelBuilder.Entity<TenantDomain>(domain =>
        {
            domain.ToTable("tenant_domains");
            domain.HasKey(d => d.Id);
            domain.Property(d => d.Id).HasColumnName("id");
            domain.Property(d => d.TenantId).HasColumnName("tenant_id");
            domain.Property(d => d.Domain).HasColumnName("domain").HasMaxLength(253);
            domain.HasIndex(d => d.Domain).IsUnique();
        });
    }
}

public sealed class Tenant
{
    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>False: tenant is locked (403).</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True: data in its own database (Silo). False: shared database (Pool).</summary>
    public bool IsIsolatedDb { get; set; }

    /// <summary>Own database connection string (Silo only). Stored in clear: see docs/extension-points.md.</summary>
    public string? ConnectionString { get; set; }

    public List<TenantDomain> Domains { get; set; } = [];
}

/// <summary>A host name (subdomain or custom domain, lowercase) that resolves to a tenant.</summary>
public sealed class TenantDomain
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public required string Domain { get; set; }

    public Tenant Tenant { get; set; } = null!;
}

internal sealed class CatalogDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.CreateDesignTimeOptions<CatalogDbContext>(CatalogDbContext.Schema));
}
