using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public partial class DotEnvStore : IDotEnvStore
{
    private static readonly Regex KeyRegex = ValidKeyPattern();

    private readonly string _filePath;
    private readonly long _maxFileBytes;
    private readonly int _maxLineLength;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private List<EnvLine> _lines;
    private Dictionary<string, int> _keyIndex;

    public DotEnvStore(string filePath, long maxFileBytes = 1 * 1024 * 1024, int maxLineLength = 16384)
    {
        _filePath = filePath;
        _maxFileBytes = maxFileBytes;
        _maxLineLength = maxLineLength;
        _lines = new List<EnvLine>();
        _keyIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Reload();
    }

    public void Reload()
    {
        _lines.Clear();
        _keyIndex.Clear();

        if (!File.Exists(_filePath))
            return;

        var fileInfo = new FileInfo(_filePath);
        if (fileInfo.Length > _maxFileBytes)
            throw new KejiConfigurationException(
                $".env file exceeds maximum size of {_maxFileBytes} bytes: {_filePath}");

        var allText = File.ReadAllText(_filePath);
        if (allText.Contains('\0'))
            throw new KejiConfigurationException(".env file contains null characters and is rejected.");

        var rawLines = File.ReadAllLines(_filePath);

        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];

            if (line.Length > _maxLineLength)
                throw new KejiConfigurationException(
                    $".env file line {i + 1} exceeds maximum length of {_maxLineLength} characters.");

            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                _lines.Add(new EnvLine { Raw = line, Type = LineType.Other });
                continue;
            }

            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: 'export' is not supported. Remove 'export' prefix.");

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: line is not empty, not a comment, and does not contain '='.");

            var keyPart = trimmed[..eqIndex].TrimEnd();
            if (!KeyRegex.IsMatch(keyPart))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: variable name '{keyPart}' is not a valid identifier. " +
                    "Must match pattern: ^[A-Za-z_][A-Za-z0-9_]*$");

            if (_keyIndex.ContainsKey(keyPart))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: duplicate variable name '{keyPart}'.");

            var valuePart = eqIndex + 1 < trimmed.Length ? trimmed[(eqIndex + 1)..] : string.Empty;
            valuePart = StripQuotes(valuePart);

            if (valuePart.Contains('\0'))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: value for '{keyPart}' contains null character.");

            _lines.Add(new EnvLine { Raw = line, Type = LineType.KeyValue, Key = keyPart });
            _keyIndex[keyPart] = _lines.Count - 1;
        }
    }

    public string? GetValue(string key)
    {
        if (!_keyIndex.TryGetValue(key, out var index))
            return null;

        var line = _lines[index];
        var eqIndex = line.Raw.IndexOf('=');
        if (eqIndex < 0)
            return null;

        var valuePart = eqIndex + 1 < line.Raw.Length ? line.Raw[(eqIndex + 1)..] : string.Empty;
        return StripQuotes(valuePart);
    }

    public IReadOnlyDictionary<string, string> GetSnapshot()
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in _keyIndex)
        {
            var line = _lines[kvp.Value];
            var eqIndex = line.Raw.IndexOf('=');
            var valuePart = eqIndex + 1 < line.Raw.Length ? line.Raw[(eqIndex + 1)..] : string.Empty;
            snapshot[kvp.Key] = StripQuotes(valuePart);
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(snapshot);
    }

    public async Task<DotEnvUpsertResult> UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (!KeyRegex.IsMatch(key))
            throw new ArgumentException($"Invalid environment variable name: '{key}'. Must match ^[A-Za-z_][A-Za-z0-9_]*$", nameof(key));

        if (value.Contains('\r') || value.Contains('\n') || value.Contains('\0'))
            throw new ArgumentException("Value must not contain carriage return, line feed, or null characters.", nameof(value));

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            bool created;
            bool updated;

            if (_keyIndex.TryGetValue(key, out var existingIndex))
            {
                var existingLine = _lines[existingIndex];
                var eqIndex = existingLine.Raw.IndexOf('=');
                var prefix = eqIndex >= 0 ? existingLine.Raw[..(eqIndex + 1)] : key + "=";
                _lines[existingIndex] = new EnvLine
                {
                    Raw = prefix + value,
                    Type = LineType.KeyValue,
                    Key = key,
                };
                created = false;
                updated = true;
            }
            else
            {
                _lines.Add(new EnvLine
                {
                    Raw = $"{key}={value}",
                    Type = LineType.KeyValue,
                    Key = key,
                });
                _keyIndex[key] = _lines.Count - 1;
                created = true;
                updated = false;
            }

            await PersistAsync(cancellationToken).ConfigureAwait(false);

            return new DotEnvUpsertResult(key, created, updated);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public bool RemoveValue(string key)
    {
        if (!_keyIndex.TryGetValue(key, out var index))
            return false;

        _semaphore.Wait();

        try
        {
            if (!_keyIndex.TryGetValue(key, out index))
                return false;

            _lines.RemoveAt(index);
            _keyIndex.Remove(key);

            foreach (var k in _keyIndex.Keys.ToList())
            {
                if (_keyIndex[k] > index)
                    _keyIndex[k]--;
            }

            PersistAsync(CancellationToken.None).GetAwaiter().GetResult();
            return true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        var tempDir = Path.GetDirectoryName(_filePath)!;
        var tempFile = Path.Combine(tempDir, $".env.tmp.{Guid.NewGuid():N}");

        try
        {
            await using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(fs, System.Text.Encoding.UTF8))
            {
                foreach (var line in _lines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(line.Raw.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tempFile, _filePath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            throw;
        }
    }

    private static string StripQuotes(string value)
    {
        if (value.Length >= 2)
        {
            if ((value[0] == '"' && value[^1] == '"') ||
                (value[0] == '\'' && value[^1] == '\''))
            {
                return value[1..^1];
            }
        }
        return value;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled)]
    private static partial Regex ValidKeyPattern();

    private enum LineType { Other, KeyValue }

    private struct EnvLine
    {
        public string Raw;
        public LineType Type;
        public string? Key;
    }
}
