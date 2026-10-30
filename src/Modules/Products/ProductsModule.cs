using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Products.Data;
using Shell.Abstractions;

namespace Modules.Products;

public sealed class ProductsModule : IModule
{
    public string Id => "products";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ProductsDbContext>(ProductsDbContext.Schema);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ProductEndpoints.Map(endpoints);
    }
}
