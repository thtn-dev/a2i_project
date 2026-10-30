namespace Shell.Abstractions.Tenancy;

/// <summary>Entities stored per tenant. Automatically filtered and stamped by <see cref="TenantDbContext"/>.</summary>
public interface IMustHaveTenant
{
    Guid TenantId { get; set; }
}

/// <summary>Tenant resolved for the current request.</summary>
/// <param name="Identifier">Domain the tenant was resolved from (e.g. <c>tenant1.app.com</c>), or its name when looked up by id.</param>
/// <param name="IsIsolatedDb">True: own database (Silo). False: shared database (Pool).</param>
/// <param name="ConnectionString">Own database connection string; null for Pool tenants.</param>
/// <param name="IsActive">False: tenant is locked, requests get 403.</param>
public sealed record TenantInfo(Guid Id, string Identifier, bool IsIsolatedDb, string? ConnectionString, bool IsActive);

/// <summary>Read access to the current tenant (scoped). Null when no tenant has been resolved.</summary>
public interface ITenantContext
{
    TenantInfo? Current { get; }
}

/// <summary>Connection string of the database holding the current tenant's data (own DB for Silo, shared DB otherwise).</summary>
public interface ITenantConnectionStringProvider
{
    string GetConnectionString();
}
