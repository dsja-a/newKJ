using System.Collections.ObjectModel;
using System.Text;
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
                $".env file exceeds maximum size of {_maxFileBytes} bytes.");

        var allText = File.ReadAllText(_filePath);
        if (allText.Contains('\0'))
            throw new KejiConfigurationException(".env file contains null characters.");

        var rawLines = File.ReadAllLines(_filePath);

        for (int i = 0; i < rawLines.Length; i++)
        {
            var line = rawLines[i];

            if (line.Length > _maxLineLength)
                throw new KejiConfigurationException(
                    $".env file line {i + 1} exceeds maximum line length.");

            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                _lines.Add(new EnvLine { Raw = line, Type = LineType.Other });
                continue;
            }

            if (trimmed.StartsWith("export ", StringComparison.Ordinal))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: 'export' is not supported.");

            var eqIndex = trimmed.IndexOf('=');
            if (eqIndex < 0)
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: invalid line format (expected KEY=value).");

            var keyPart = trimmed[..eqIndex].TrimEnd();
            if (!KeyRegex.IsMatch(keyPart))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: invalid variable name '{keyPart}'.");

            if (_keyIndex.ContainsKey(keyPart))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: duplicate variable name '{keyPart}'.");

            var valuePart = eqIndex + 1 < trimmed.Length ? trimmed[(eqIndex + 1)..] : string.Empty;
            valuePart = StripQuotes(valuePart);

            if (valuePart.Contains('\0'))
                throw new KejiConfigurationException(
                    $".env file line {i + 1}: value contains null character.");

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
        return new ReadOnlyDictionary<string, string>(snapshot);
    }

    public async Task<DotEnvUpsertResult> UpsertAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        ValidateKey(key);
        ValidateValue(value);

        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var candidateLines = new List<EnvLine>(_lines);
            var candidateIndex = new Dictionary<string, int>(_keyIndex, StringComparer.OrdinalIgnoreCase);

            bool created;
            bool updated;
            int lineLength;

            if (candidateIndex.TryGetValue(key, out var existingIdx))
            {
                var existingLine = candidateLines[existingIdx];
                var eqPos = existingLine.Raw.IndexOf('=');
                var prefix = eqPos >= 0 ? existingLine.Raw[..(eqPos + 1)] : key + "=";
                var newRaw = prefix + value;
                lineLength = newRaw.Length;

                if (lineLength > _maxLineLength)
                    throw new KejiConfigurationException(
                        $"Updated line exceeds maximum length of {_maxLineLength}.");

                candidateLines[existingIdx] = new EnvLine { Raw = newRaw, Type = LineType.KeyValue, Key = key };
                created = false;
                updated = true;
            }
            else
            {
                var newRaw = $"{key}={value}";
                lineLength = newRaw.Length;

                if (lineLength > _maxLineLength)
                    throw new KejiConfigurationException(
                        $"New line exceeds maximum length of {_maxLineLength}.");

                candidateLines.Add(new EnvLine { Raw = newRaw, Type = LineType.KeyValue, Key = key });
                candidateIndex[key] = candidateLines.Count - 1;
                created = true;
                updated = false;
            }

            await PersistCandidateAsync(candidateLines, cancellationToken).ConfigureAwait(false);

            _lines = candidateLines;
            _keyIndex = candidateIndex;

            return new DotEnvUpsertResult(key, created, updated);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task PersistCandidateAsync(List<EnvLine> candidateLines, CancellationToken cancellationToken)
    {
        long totalByteCount = 0;
        foreach (var line in candidateLines)
        {
            var lineBytes = Encoding.UTF8.GetByteCount(line.Raw + Environment.NewLine);
            totalByteCount += lineBytes;
            if (totalByteCount > _maxFileBytes)
                throw new KejiConfigurationException(
                    $"Resulting .env file would exceed maximum size of {_maxFileBytes} bytes.");
        }

        var dir = Path.GetDirectoryName(_filePath)!;
        var tempFile = Path.Combine(dir, $".env.tmp.{Guid.NewGuid():N}");

        try
        {
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

            await using (var fs = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            await using (var writer = new StreamWriter(fs, utf8NoBom))
            {
                foreach (var line in candidateLines)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(line.Raw.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(tempFile, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tempFile, _filePath);
            }
        }
        catch
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
            throw;
        }
    }

    private static void ValidateKey(string key)
    {
        if (!KeyRegex.IsMatch(key))
            throw new ArgumentException(
                $"Invalid environment variable name: '{key}'. Must match ^[A-Za-z_][A-Za-z0-9_]*$", nameof(key));
    }

    private static void ValidateValue(string value)
    {
        if (value.Contains('\r'))
            throw new ArgumentException("Value must not contain carriage return.", nameof(value));
        if (value.Contains('\n'))
            throw new ArgumentException("Value must not contain line feed.", nameof(value));
        if (value.Contains('\0'))
            throw new ArgumentException("Value must not contain null characters.", nameof(value));
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
