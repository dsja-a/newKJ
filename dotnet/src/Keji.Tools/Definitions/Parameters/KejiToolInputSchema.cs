namespace Keji.Tools.Definitions.Parameters;

public sealed class KejiToolInputSchema
{
    public IReadOnlyList<KejiToolParameterDefinition> Parameters { get; }

    public KejiToolInputSchema(IReadOnlyList<KejiToolParameterDefinition>? parameters)
    {
        if (parameters is null)
        {
            Parameters = Array.Empty<KejiToolParameterDefinition>();
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

        Parameters = parameters.ToArray();
    }

    public bool HasRequiredParams => Parameters.Any(p => p.Required);
}
