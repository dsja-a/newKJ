namespace Keji.Tools.Execution;

public sealed class ToolWorkerOptions
{
    public string ExecutablePath { get; }
    public TimeSpan DefaultTimeout { get; }

    public ToolWorkerOptions(string executablePath, TimeSpan? defaultTimeout = null)
    {
        var validated = ValidateWorkerPath(executablePath);
        ExecutablePath = validated ?? throw new ArgumentException($"Invalid worker path: {executablePath}", nameof(executablePath));
        DefaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(10);
        if (DefaultTimeout <= TimeSpan.Zero || DefaultTimeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(defaultTimeout), "Timeout must be between 1ms and 60s");
    }

    private static string? ValidateWorkerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }

        if (!File.Exists(fullPath))
            return null;

        var fileName = Path.GetFileName(fullPath);
        if (!fileName.Equals("Keji.ToolWorker.exe", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("Keji.ToolWorker", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fullPath;
    }
}
