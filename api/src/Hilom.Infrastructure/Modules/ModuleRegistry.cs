using System.Reflection;

namespace Hilom.Infrastructure.Modules;

/// <summary>The modules loaded by the host. Registered once as a singleton.</summary>
public sealed class ModuleRegistry
{
    public ModuleRegistry(IEnumerable<IModule> modules)
    {
        Modules = [.. modules];

        var duplicateSchema = Modules.GroupBy(m => m.Schema).FirstOrDefault(g => g.Count() > 1);
        if (duplicateSchema is not null)
        {
            throw new InvalidOperationException($"Two modules share the schema '{duplicateSchema.Key}'.");
        }
    }

    public IReadOnlyList<IModule> Modules { get; }

    public IModule? FindByAssembly(Assembly assembly) => Modules.FirstOrDefault(m => m.Assembly == assembly);
}
