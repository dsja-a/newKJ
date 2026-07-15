using Keji.Tools.Catalog;
using Keji.Tools.Registry;

namespace Keji.ToolWorker.Worker;

public static class BuiltInToolWorkerRegistry
{
    private static readonly Lazy<IKejiToolRegistry> _registry = new(() =>
    {
        var builder = new KejiToolRegistryBuilder();
        foreach (var def in BuiltInToolCatalog.All)
        {
            if (def.Availability == Keji.Tools.Definitions.KejiToolAvailability.Executable
                && def.ExecutionTarget == Keji.Tools.Definitions.KejiToolExecutionTarget.ToolWorker)
            {
                builder.Register(def);
            }
        }
        return builder.Build();
    });

    public static IKejiToolRegistry Instance => _registry.Value;
}
