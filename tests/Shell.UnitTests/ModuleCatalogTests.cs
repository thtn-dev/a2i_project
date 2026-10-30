using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shell.Abstractions;
using Shell.Core.Modules;

namespace Shell.UnitTests;

public class ModuleCatalogTests
{
    [Fact]
    public void Orders_dependencies_before_dependents()
    {
        var catalog = ModuleCatalog.Build([Module("orders", "billing"), Module("billing", "customers"), Module("customers")]);

        Assert.Equal(["customers", "billing", "orders"], catalog.Modules.Select(m => m.Id));
    }

    [Fact]
    public void Orders_diamond_dependencies_deterministically()
    {
        var catalog = ModuleCatalog.Build([Module("d", "b", "c"), Module("c", "a"), Module("b", "a"), Module("a")]);

        Assert.Equal(["a", "b", "c", "d"], catalog.Modules.Select(m => m.Id));
    }

    [Fact]
    public void Missing_dependency_fails_with_module_names()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => ModuleCatalog.Build([Module("billing", "customers")]));

        Assert.Contains("'billing' depends on 'customers'", ex.Message);
    }

    [Fact]
    public void Cycle_fails_with_cycle_path()
    {
        var ex = Assert.Throws<ModuleLoadException>(() =>
            ModuleCatalog.Build([Module("a", "b"), Module("b", "c"), Module("c", "a")]));

        Assert.Contains("a -> b -> c -> a", ex.Message);
    }

    [Fact]
    public void Self_dependency_is_a_cycle()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => ModuleCatalog.Build([Module("a", "a")]));

        Assert.Contains("a -> a", ex.Message);
    }

    [Fact]
    public void Duplicate_id_fails()
    {
        var ex = Assert.Throws<ModuleLoadException>(() => ModuleCatalog.Build([Module("a"), Module("a")]));

        Assert.Contains("Duplicate module id 'a'", ex.Message);
    }

    [Theory]
    [InlineData("Products")]
    [InlineData("1products")]
    [InlineData("products module")]
    public void Invalid_id_fails(string id)
    {
        Assert.Throws<ModuleLoadException>(() => ModuleCatalog.Build([Module(id)]));
    }

    [Fact]
    public void Module_without_dependencies_declares_none()
    {
        IModule module = new MinimalModule();

        Assert.Empty(module.DependsOn);
    }

    [Fact]
    public void Loader_discovers_products_module_from_output_directory()
    {
        var catalog = ModuleLoader.LoadFromDirectory(AppContext.BaseDirectory);

        Assert.Single(catalog.Modules, m => m.Id == "products");
    }

    private static IModule Module(string id, params string[] dependsOn) => new FakeModule(id, dependsOn);

    private sealed class FakeModule(string id, IReadOnlyList<string> dependsOn) : IModule
    {
        public string Id => id;

        public IReadOnlyList<string> DependsOn => dependsOn;

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }

    /// <summary>Relies on the default (empty) <see cref="IModule.DependsOn"/>.</summary>
    private sealed class MinimalModule : IModule
    {
        public string Id => "minimal";

        public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }
    }
}
