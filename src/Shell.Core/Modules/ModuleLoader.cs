using System.Reflection;
using System.Runtime.Loader;
using Shell.Abstractions;

namespace Shell.Core.Modules;

/// <summary>
/// Discovers module assemblies (<c>Modules.*.dll</c>, excluding <c>*.Contracts.dll</c>),
/// instantiates their <see cref="IModule"/> and builds a validated <see cref="ModuleCatalog"/>.
/// </summary>
public static class ModuleLoader
{
    public const string AssemblyPrefix = "Modules.";
    public const string ContractsSuffix = ".Contracts";

    /// <exception cref="ModuleLoadException">A module is malformed or the module graph is invalid.</exception>
    public static ModuleCatalog LoadFromDirectory(string directory) => Load(DiscoverAssemblies(directory));

    public static IReadOnlyList<Assembly> DiscoverAssemblies(string directory) =>
        Directory.EnumerateFiles(directory, $"{AssemblyPrefix}*.dll")
            .Where(path => !Path.GetFileNameWithoutExtension(path).EndsWith(ContractsSuffix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)
            .ToList();

    /// <exception cref="ModuleLoadException">A module is malformed or the module graph is invalid.</exception>
    public static ModuleCatalog Load(IEnumerable<Assembly> assemblies) =>
        ModuleCatalog.Build(assemblies.Select(CreateModule));

    private static IModule CreateModule(Assembly assembly)
    {
        var moduleTypes = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t))
            .ToList();
        if (moduleTypes.Count != 1)
        {
            throw new ModuleLoadException(
                $"Assembly '{assembly.GetName().Name}' must contain exactly one non-abstract IModule implementation, found {moduleTypes.Count}.");
        }

        try
        {
            return (IModule)Activator.CreateInstance(moduleTypes[0])!;
        }
        catch (Exception ex)
        {
            throw new ModuleLoadException(
                $"Cannot create module '{moduleTypes[0].FullName}' (a public parameterless constructor is required): {ex.Message}", ex);
        }
    }
}
