using System.Collections.Immutable;

namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolInputSchema
{
    public IReadOnlyList<KejiToolParameterDefinition> Parameters { get; }

    public KejiToolInputSchema(IReadOnlyList<KejiToolParameterDefinition>? parameters)
    {
        if (parameters is null)
        {
            Parameters = ImmutableArray<KejiToolParameterDefinition>.Empty;
            return;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in parameters)
        {
            if (p is null)
                throw new KejiToolContractException("Schema parameter must not be null.");
            if (!names.Add(p.Name))
                throw new KejiToolContractException($"Duplicate parameter name: '{p.Name}'.");
        }

        Parameters = ImmutableArray.CreateRange(parameters);
    }

    public bool HasRequiredParams => Parameters.Any(p => p.Required);
}
