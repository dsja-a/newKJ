using System.Text.RegularExpressions;

namespace Keji.ToolWorker.Client;

public static partial class WorkerPathValidator
{
    private static readonly char[] PathSeparators = ['/', '\\'];

    public static string Validate(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            throw new ArgumentException("Path must not be empty", nameof(exePath));

        var fullPath = Path.GetFullPath(exePath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Worker executable not found", fullPath);

        var fileName = Path.GetFileName(fullPath);
        if (!fileName.Equals("Keji.ToolWorker.exe", StringComparison.OrdinalIgnoreCase) &&
            !fileName.Equals("Keji.ToolWorker", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Invalid executable name: {fileName}. Must be Keji.ToolWorker.exe or Keji.ToolWorker");
        }

        var dir = Path.GetDirectoryName(fullPath)!;
        var solutionRoot = FindSolutionRoot(dir);
        if (solutionRoot is null || !dir.StartsWith(solutionRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Worker path must be under the solution root. Path: {fullPath}");

        return fullPath;
    }

    private static string? FindSolutionRoot(string? dir)
    {
        while (dir is not null)
        {
            if (Directory.EnumerateFiles(dir, "*.sln").Any())
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
