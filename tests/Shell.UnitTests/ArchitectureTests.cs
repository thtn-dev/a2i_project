using System.Reflection;
using System.Runtime.Loader;
using NetArchTest.Rules;
using Shell.Core.Modules;

namespace Shell.UnitTests;

/// <summary>
/// A module may only reach another module through its <c>X.Contracts</c> assembly,
/// and must not depend on the shell internals (Shell.Core, Host).
/// </summary>
public class ArchitectureTests
{
    private static readonly string[] ShellInternals = ["Shell.Core", "Shell.Host"];

    private static readonly IReadOnlyList<Assembly> ModuleAssemblies = ModuleLoader.DiscoverAssemblies(AppContext.BaseDirectory);

    private static readonly IReadOnlyList<Assembly> ContractAssemblies =
        Directory.EnumerateFiles(AppContext.BaseDirectory, $"{ModuleLoader.AssemblyPrefix}*{ModuleLoader.ContractsSuffix}.dll")
            .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)
            .ToList();

    [Fact]
    public void Modules_reference_other_modules_only_through_contracts()
    {
        Assert.NotEmpty(ModuleAssemblies);

        foreach (var module in ModuleAssemblies)
        {
            var forbidden = ModuleAssemblies
                .Where(other => other != module)
                .SelectMany(ImplementationTypeNames)
                .Concat(ShellInternals)
                .ToArray();

            var result = Types.InAssembly(module).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

            Assert.True(result.IsSuccessful,
                $"{module.GetName().Name} must only use other modules through their Contracts and must not use shell internals. " +
                $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void Contracts_do_not_depend_on_module_implementations()
    {
        Assert.NotEmpty(ContractAssemblies);

        var forbidden = ModuleAssemblies.SelectMany(ImplementationTypeNames).Concat(ShellInternals).ToArray();

        foreach (var contracts in ContractAssemblies)
        {
            var result = Types.InAssembly(contracts).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

            Assert.True(result.IsSuccessful,
                $"{contracts.GetName().Name} must not depend on module implementations or shell internals. " +
                $"Offending types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    // Full type names (not namespaces): NetArchTest matches by prefix, so the namespace
    // "Modules.Billing" would also match "Modules.Billing.Contracts", which is allowed.
    private static IEnumerable<string> ImplementationTypeNames(Assembly assembly) =>
        assembly.GetTypes()
            .Where(t => !t.IsNested && t.FullName is not null && !t.Name.StartsWith('<'))
            .Select(t => t.FullName!);
}
