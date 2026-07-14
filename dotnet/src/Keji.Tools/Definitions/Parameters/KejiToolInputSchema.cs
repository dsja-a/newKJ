using System.Collections.Frozen;

namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolInputSchema
{
    public IReadOnlyList<KejiToolParameterDefinition> Parameters { get; }

    private static readonly FrozenSet<string> Empty = new List<string>().ToFrozenSet(StringComparer.Ordinal);

    public KejiToolInputSchema(IReadOnlyList<KejiToolParameterDefinition>? parameters)
    {
        if (parameters is null)
        {
            Parameters = new List<KejiToolParameterDefinition>();
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters)
        {
            if (!names.Add(p.Name))
                throw new KejiToolContractException($"Duplicate parameter name: '{p.Name}'.");
        }

        Parameters = parameters.ToFrozenSet().ToList().AsReadOnly();
    }

    public bool HasRequiredParams => Parameters.Any(p => p.Required);
}
