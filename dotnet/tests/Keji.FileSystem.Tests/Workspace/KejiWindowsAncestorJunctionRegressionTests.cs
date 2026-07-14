using System.Diagnostics;
using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWindowsAncestorJunctionRegressionTests : IDisposable
{
    private readonly string _container = Path.Combine(
        Path.GetTempPath(),
        $"keji-ancestor-container-{Guid.NewGuid():N}");

    private readonly string _outside = Path.Combine(
        Path.GetTempPath(),
        $"keji-ancestor-outside-{Guid.NewGuid():N}");

    private string JunctionPath => Path.Combine(_container, "linked-ancestor");

    [Fact]
    public void JunctionAboveConfiguredWorkspaceRootIsRejectedAsFinalPathEscape()
    {
        Assert.True(OperatingSystem.IsWindows(), "This test requires Windows.");
        Directory.CreateDirectory(Path.Combine(_outside, "workspace", "shared"));
        CreateJunction(JunctionPath, _outside);
        var configuredRoot = Path.Combine(JunctionPath, "workspace");
        var options = new KejiWorkspaceOptions(configuredRoot);
        var candidate = new KejiWorkspacePathCandidateResolver(options).Resolve(
            new KejiWorkspacePathRequest(
                KejiWorkspaceScope.Shared,
                targetUserId: null,
                relativePath: "file.txt"));
        var inspector = new KejiWindowsPathInspector(options);

        using var result = inspector.Inspect(candidate, KejiFileSystemOperation.Read);

        Assert.True(candidate.IsValid);
        Assert.False(result.IsSafe);
        Assert.Equal(
            KejiWorkspaceAccessFailureReason.FinalPathEscapesRoot,
            result.FailureReason);
        Assert.False(result.TargetExists);
        Assert.False(result.TargetIsDirectory);
    }

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        Directory.CreateDirectory(Path.GetDirectoryName(junctionPath)!);

        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        Assert.False(string.IsNullOrWhiteSpace(commandInterpreter));

        var startInfo = new ProcessStartInfo
        {
            FileName = commandInterpreter,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(
            process.ExitCode == 0,
            $"mklink /J failed with exit code {process.ExitCode}: " +
            standardOutput + standardError);
        Assert.True(
            File.GetAttributes(junctionPath).HasFlag(FileAttributes.ReparsePoint),
            "The created ancestor must be a junction.");
    }

    public void Dispose()
    {
        if (Directory.Exists(JunctionPath))
            Directory.Delete(JunctionPath, recursive: false);
        if (Directory.Exists(_container))
            Directory.Delete(_container, recursive: true);
        if (Directory.Exists(_outside))
            Directory.Delete(_outside, recursive: true);
    }
}
