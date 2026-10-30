using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Shell.Abstractions;
using Shell.Abstractions.Tenancy;

namespace Modules.Products.Data;

internal sealed class Product : IMustHaveTenant
{
    public const int NameMaxLength = 200;

    public Guid Id { get; set; }

    public required string Name { get; set; }

    public decimal Price { get; set; }

    /// <summary>Set by <see cref="TenantDbContext"/> on insert; never set it by hand.</summary>
    public Guid TenantId { get; set; }
}

/// <summary>
/// Inherits <see cref="TenantDbContext"/>: queries only see the current tenant's rows and
/// the connection goes to the tenant's own DB (Silo) or the shared DB (Pool).
/// </summary>
internal sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options, ITenantContext tenantContext)
    : TenantDbContext(options, tenantContext)
{
    /// <summary>Every table of this module lives in this schema, including the migration history.</summary>
    public const string Schema = "products";

    public DbSet<Product> Products => Set<Product>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Product>(product =>
        {
            product.ToTable("products");
            product.HasKey(p => p.Id);
            product.Property(p => p.Id).HasColumnName("id");
            product.Property(p => p.Name).HasColumnName("name").HasMaxLength(Product.NameMaxLength);
            product.Property(p => p.Price).HasColumnName("price").HasPrecision(18, 2);
            product.Property(p => p.TenantId).HasColumnName("tenant_id");
            product.HasIndex(p => p.TenantId);
        });
    }
}

/// <summary>Used by <c>dotnet ef migrations add</c> only.</summary>
internal sealed class ProductsDbContextFactory : IDesignTimeDbContextFactory<ProductsDbContext>
{
    public ProductsDbContext CreateDbContext(string[] args) =>
        new(ModuleDbContextExtensions.CreateDesignTimeOptions<ProductsDbContext>(ProductsDbContext.Schema),
            ModuleDbContextExtensions.DesignTimeTenantContext);
}
