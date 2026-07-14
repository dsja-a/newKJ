using Keji.Tools.Definitions;
using Keji.Tools.Names;

namespace Keji.Tools.Registry;

public interface IKejiToolRegistry
{
    KejiToolResolution Resolve(KejiToolName name);
    bool Contains(KejiToolName name);
    IReadOnlyList<KejiToolDefinition> GetAll();
}
