using Keji.Tools.Definitions;
using Keji.Tools.Names;

namespace Keji.Tools.Registry;

public sealed class KejiToolResolution
{
    public KejiToolResolutionStatus Status { get; }
    public KejiToolDefinition? Definition { get; }
    public KejiToolName? Name { get; }

    private KejiToolResolution(KejiToolResolutionStatus status, KejiToolDefinition? definition, KejiToolName? name)
    {
        Status = status;
        Definition = definition;
        Name = name;
    }

    public static KejiToolResolution Found(KejiToolDefinition definition) =>
        new(KejiToolResolutionStatus.Found, definition, definition.Name);

    public static KejiToolResolution InvalidName(KejiToolName name) =>
        new(KejiToolResolutionStatus.InvalidName, null, name);

    public static KejiToolResolution NotRegistered(KejiToolName name) =>
        new(KejiToolResolutionStatus.NotRegistered, null, name);
}
