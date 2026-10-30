using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Shell.Abstractions;

/// <summary>
/// Entry point of a module: a slice of business code with its own services, endpoints and schema.
/// The shell creates one instance per module at startup.
/// </summary>
public interface IModule
{
    /// <summary>Lowercase id (letters, digits, '-'). Endpoints are mapped under <c>/api/{Id}</c>.</summary>
    string Id { get; }

    /// <summary>Ids of the modules that must be present and registered before this one.</summary>
    IReadOnlyList<string> DependsOn => [];

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary><paramref name="endpoints"/> is a route group already prefixed with <c>/api/{Id}</c>.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
