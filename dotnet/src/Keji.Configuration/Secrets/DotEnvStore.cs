using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public class DotEnvStore : IDotEnvStore
{
    private readonly string _filePath;
    private readonly long _maxFileBytes;
    private Dictionary<string, string> _variables;

    public DotEnvStore(string filePath, long maxFileBytes = 1 * 1024 * 1024)
    {
        _filePath = filePath;
        _maxFileBytes = maxFileBytes;
        _variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Reload();
    }

    public string? GetValue(string key)
    {
        return _variables.TryGetValue(key, out var value) ? value : null;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        return _variables;
    }

    public void SetValue(string key, string value)
    {
        _variables[key] = value;
        Persist();
    }

    public bool RemoveValue(string key)
    {
        var removed = _variables.Remove(key);
        if (removed)
            Persist();
        return removed;
    }

    public void Reload()
    {
        _variables.Clear();

        if (!File.Exists(_filePath))
            return;

        var fileInfo = new FileInfo(_filePath);
        if (fileInfo.Length > _maxFileBytes)
            throw new KejiConfigurationException(
                $".env file exceeds maximum size of {_maxFileBytes} bytes: {_filePath}");

        var lines = File.ReadAllLines(_filePath);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (string.IsNullOrEmpty(line) || line.StartsWith('#'))
                continue;

            var eqIndex = line.IndexOf('=');
            if (eqIndex < 0)
                continue;

            var key = line[..eqIndex].Trim();
            if (key.Length == 0)
                continue;

            var value = eqIndex + 1 < line.Length ? line[(eqIndex + 1)..].Trim() : string.Empty;

            if (value.Length >= 2)
            {
                if ((value[0] == '"' && value[^1] == '"') ||
                    (value[0] == '\'' && value[^1] == '\''))
                {
                    value = value[1..^1];
                }
            }

            _variables[key] = value;
        }
    }

    private void Persist()
    {
        var lines = new List<string>(_variables.Count);
        foreach (var kvp in _variables.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{kvp.Key}={kvp.Value}");
        }

        var tempPath = _filePath + ".tmp";
        try
        {
            File.WriteAllLines(tempPath, lines);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }
}
