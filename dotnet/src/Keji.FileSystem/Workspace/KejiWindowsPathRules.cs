using System.Text;

namespace Keji.FileSystem.Workspace;

internal static class KejiWindowsPathRules
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³",
    };

    public static bool IsLocalAbsolutePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length < 3)
            return false;
        if (IsDevicePath(path))
            return false;
        if (path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            return false;
        return char.IsAsciiLetter(path[0]) &&
            path[1] == ':' &&
            (path[2] == '\\' || path[2] == '/');
    }

    public static KejiPathSandboxFailureReason Validate(
        string? relativePath, int maxPathLength, int maxSegmentLength, out string? normalized)
    {
        normalized = null;
        if (relativePath is null)
            return KejiPathSandboxFailureReason.MissingRelativePath;
        if (relativePath.Length > 0 && string.IsNullOrWhiteSpace(relativePath))
            return KejiPathSandboxFailureReason.WhitespaceRelativePath;
        if (relativePath.Length == 0)
        {
            normalized = string.Empty;
            return KejiPathSandboxFailureReason.None;
        }
        if (ContainsUnpairedSurrogate(relativePath))
            return KejiPathSandboxFailureReason.PathNormalizationFailed;
        string formC;
        try
        {
            formC = relativePath.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return KejiPathSandboxFailureReason.PathNormalizationFailed;
        }
        var special = ClassifyRooted(formC);
        if (special != KejiPathSandboxFailureReason.None)
            return special;
        var parts = formC.Split(new[] { '\\', '/' }, StringSplitOptions.None);
        if (parts.Any(string.IsNullOrEmpty))
            return KejiPathSandboxFailureReason.EmptySegment;
        foreach (var part in parts)
        {
            if (part == ".")
                return KejiPathSandboxFailureReason.CurrentDirectorySegment;
            if (part == "..")
                return KejiPathSandboxFailureReason.TraversalSegment;
            if (part.Length > maxSegmentLength)
                return KejiPathSandboxFailureReason.SegmentTooLong;
            if (part.Any(char.IsControl))
                return KejiPathSandboxFailureReason.ControlCharacter;
            if (part.Contains(':'))
                return KejiPathSandboxFailureReason.AlternateDataStream;
            if (part.IndexOfAny(new[] { '<', '>', '"', '|', '?', '*' }) >= 0)
                return KejiPathSandboxFailureReason.InvalidCharacter;
            var stem = part.Split('.')[0].TrimEnd(' ', '.');
            if (ReservedNames.Contains(stem))
                return KejiPathSandboxFailureReason.ReservedDeviceName;
            if (char.IsWhiteSpace(part[0]) || part.EndsWith(' ') || part.EndsWith('.'))
                return KejiPathSandboxFailureReason.TrailingDotOrSpace;
        }
        normalized = string.Join('\\', parts);
        return normalized.Length > maxPathLength
            ? KejiPathSandboxFailureReason.PathTooLong
            : KejiPathSandboxFailureReason.None;
    }

    private static bool ContainsUnpairedSurrogate(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
                continue;
            if (char.IsHighSurrogate(value[index]) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }
            return true;
        }
        return false;
    }

    private static KejiPathSandboxFailureReason ClassifyRooted(string path)
    {
        if (IsDevicePath(path))
            return KejiPathSandboxFailureReason.DevicePath;
        if (path.StartsWith("\\\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
            return KejiPathSandboxFailureReason.UncPath;
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
            return KejiPathSandboxFailureReason.DriveQualifiedPath;
        if (path.StartsWith('\\') || path.StartsWith('/'))
            return KejiPathSandboxFailureReason.RootedPath;
        return KejiPathSandboxFailureReason.None;
    }

    private static bool IsDevicePath(string path) =>
        path.StartsWith("\\\\?\\", StringComparison.Ordinal) ||
        path.StartsWith("//?/", StringComparison.Ordinal) ||
        path.StartsWith("\\\\.\\", StringComparison.Ordinal) ||
        path.StartsWith("//./", StringComparison.Ordinal) ||
        path.StartsWith("\\??\\", StringComparison.Ordinal) ||
        path.StartsWith("/??/", StringComparison.Ordinal);
}
