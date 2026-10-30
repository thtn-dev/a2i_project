using System.Text.RegularExpressions;
using Shell.Abstractions;

namespace Shell.Core.Modules;

/// <summary>
/// Validated set of modules, ordered so that every module comes after its dependencies.
/// </summary>
public sealed partial class ModuleCatalog
{
    private ModuleCatalog(IReadOnlyList<IModule> ordered) => Modules = ordered;

    /// <summary>Modules in load order (dependencies first).</summary>
    public IReadOnlyList<IModule> Modules { get; }

    /// <summary>Validates ids and dependencies, then sorts topologically (ties broken by id).</summary>
    /// <exception cref="ModuleLoadException">Invalid id, duplicate id, missing dependency or cycle.</exception>
    public static ModuleCatalog Build(IEnumerable<IModule> modules)
    {
        var byId = new SortedDictionary<string, IModule>(StringComparer.Ordinal);
        foreach (var module in modules)
        {
            if (!IdPattern().IsMatch(module.Id))
            {
                throw new ModuleLoadException(
                    $"Module id '{module.Id}' ({module.GetType().FullName}) is invalid: use lowercase letters, digits and '-', starting with a letter.");
            }

            if (!byId.TryAdd(module.Id, module))
            {
                throw new ModuleLoadException(
                    $"Duplicate module id '{module.Id}' ({byId[module.Id].GetType().FullName} and {module.GetType().FullName}).");
            }
        }

        var missing = byId.Values
            .SelectMany(m => m.DependsOn
                .Where(d => !byId.ContainsKey(d))
                .Select(d => $"'{m.Id}' depends on '{d}'"))
            .ToList();
        if (missing.Count > 0)
        {
            throw new ModuleLoadException(
                $"Missing module dependencies: {string.Join("; ", missing)}. Add the missing module(s) or fix DependsOn.");
        }

        var ordered = new List<IModule>(byId.Count);
        var states = new Dictionary<string, VisitState>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var id in byId.Keys)
        {
            Visit(id);
        }

        return new ModuleCatalog(ordered);

        void Visit(string id)
        {
            switch (states.GetValueOrDefault(id))
            {
                case VisitState.Done:
                    return;
                case VisitState.Visiting:
                    var cycle = path.Skip(path.IndexOf(id)).Append(id);
                    throw new ModuleLoadException($"Circular module dependency: {string.Join(" -> ", cycle)}.");
            }

            states[id] = VisitState.Visiting;
            path.Add(id);
            foreach (var dependency in byId[id].DependsOn.Order(StringComparer.Ordinal))
            {
                Visit(dependency);
            }

            path.RemoveAt(path.Count - 1);
            states[id] = VisitState.Done;
            ordered.Add(byId[id]);
        }
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex IdPattern();

    private enum VisitState
    {
        New,
        Visiting,
        Done,
    }
}
