using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceOptionsTests
{
    [Fact]
    public void WindowsRootIsPreserved()
    {
        var options = new KejiWorkspaceOptions("C:\\");
        Assert.Equal("C:\\", options.WorkspaceRoot);
    }

    [Fact]
    public void WindowsSlashRootIsNormalized()
    {
        var options = new KejiWorkspaceOptions("C:/workspace");
        Assert.EndsWith("workspace", options.WorkspaceRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("C:folder")]
    [InlineData("\\folder")]
    [InlineData("/folder")]
    [InlineData("\\\\server\\share")]
    [InlineData("//server/share")]
    [InlineData("\\\\?\\C:\\data")]
    [InlineData("//?/C:/data")]
    [InlineData("//./PhysicalDrive0")]
    [InlineData("/??/C:/data")]
    public void NonLocalRootsAreRejected(string root)
    {
        Assert.Throws<ArgumentException>(() => new KejiWorkspaceOptions(root));
    }
}
