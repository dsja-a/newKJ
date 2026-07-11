namespace Keji.Configuration.Models;

public class KejiConfigurationDocument
{
    public ConfigMap Root { get; }

    public KejiConfigurationDocument(ConfigMap root)
    {
        Root = root;
    }

    public bool TryGetNode(string dottedPath, out ConfigNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(dottedPath))
            return false;

        var parts = dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        ConfigNode current = Root;

        for (int i = 0; i < parts.Length; i++)
        {
            if (current is ConfigMap map && map.TryGetValue(parts[i], out var next))
            {
                current = next;
                continue;
            }
            return false;
        }

        node = current;
        return true;
    }

    public string? GetOptionalString(string dottedPath)
    {
        if (!TryGetNode(dottedPath, out var node) || node is not ConfigScalar scalar)
            return null;
        return scalar.Value;
    }

    public string GetRequiredString(string dottedPath)
    {
        if (!TryGetNode(dottedPath, out var node))
            throw new KejiConfigurationException($"Required configuration path '{dottedPath}' was not found.", dottedPath);

        if (node is not ConfigScalar scalar)
            throw new KejiConfigurationException(
                $"Configuration path '{dottedPath}' exists but is not a scalar value.", dottedPath);

        return scalar.Value ?? throw new KejiConfigurationException(
            $"Configuration path '{dottedPath}' exists but is null.", dottedPath);
    }

    public bool GetBoolean(string dottedPath, bool defaultValue)
    {
        var value = GetOptionalString(dottedPath);
        if (value is null)
            return defaultValue;

        if (bool.TryParse(value, out var result))
            return result;

        throw new KejiConfigurationException(
            $"Configuration path '{dottedPath}' has value that cannot be converted to boolean.", dottedPath);
    }

    public int GetInt32(string dottedPath, int defaultValue)
    {
        var value = GetOptionalString(dottedPath);
        if (value is null)
            return defaultValue;

        if (int.TryParse(value, out var result))
            return result;

        throw new KejiConfigurationException(
            $"Configuration path '{dottedPath}' has value that cannot be converted to int32.", dottedPath);
    }

    public IReadOnlyList<string> GetStringList(string dottedPath)
    {
        if (!TryGetNode(dottedPath, out var node))
            return Array.Empty<string>();

        if (node is ConfigSequence seq)
        {
            var results = new List<string>(seq.Count);
            foreach (var item in seq)
            {
                if (item is ConfigScalar scalar)
                    results.Add(scalar.Value ?? "");
            }
            return results;
        }

        throw new KejiConfigurationException(
            $"Configuration path '{dottedPath}' exists but is not a sequence.", dottedPath);
    }
}
