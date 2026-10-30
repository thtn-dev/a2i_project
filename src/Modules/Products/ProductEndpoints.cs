using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Modules.Products.Contracts;
using Modules.Products.Data;
using Shell.Abstractions.Security;

namespace Modules.Products;

/// <summary>
/// No tenant code here: the tenant filter, the tenant database and the TenantId stamp
/// are all handled by <see cref="ProductsDbContext"/>'s base class.
/// </summary>
internal static class ProductEndpoints
{
    private const string GetProductRoute = "products.get";

    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("", GetAll);
        endpoints.MapGet("/{id:guid}", GetById).WithName(GetProductRoute);
        endpoints.MapPost("", Create);
    }

    private static async Task<Ok<List<ProductDto>>> GetAll(ProductsDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await db.Products
            .AsNoTracking()
            .OrderBy(p => p.Name)
            .Select(p => new ProductDto(p.Id, p.Name, p.Price, p.CreatedBy))
            .ToListAsync(ct));

    private static async Task<Results<Ok<ProductDto>, NotFound>> GetById(Guid id, ProductsDbContext db, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ToDto(product));
    }

    private static async Task<Results<CreatedAtRoute<ProductDto>, ValidationProblem>> Create(
        CreateProductRequest request, ProductsDbContext db, ICurrentUser user, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > Product.NameMaxLength)
        {
            errors["name"] = [$"Name is required and must be at most {Product.NameMaxLength} characters."];
        }

        if (request.Price < 0)
        {
            errors["price"] = ["Price must not be negative."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = new Product { Id = Guid.CreateVersion7(), Name = name!, Price = request.Price, CreatedBy = user.UserId!.Value };
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);

        return TypedResults.CreatedAtRoute(ToDto(product), GetProductRoute, new { id = product.Id });
    }

    private static ProductDto ToDto(Product product) => new(product.Id, product.Name, product.Price, product.CreatedBy);
}
