using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWorkspaceRootLayoutTests
{
    [Fact]
    public void EqualRootIsWithinRoot()
    {
        Assert.True(KejiWorkspaceRootLayout.IsWithinRoot("C:\\workspace\\shared", "C:\\workspace\\shared"));
    }

    [Fact]
    public void ChildIsWithinRoot()
    {
        Assert.True(KejiWorkspaceRootLayout.IsWithinRoot("C:\\workspace\\shared", "C:\\workspace\\shared\\child.txt"));
    }

    [Fact]
    public void DriveRootPrefixDoesNotGainDuplicateSeparator()
    {
        Assert.True(KejiWorkspaceRootLayout.IsWithinRoot("C:\\", "C:\\shared"));
        Assert.False(KejiWorkspaceRootLayout.IsWithinRoot("C:\\", "D:\\shared"));
    }

    [Fact]
    public void ComparisonIsCaseInsensitive()
    {
        Assert.True(KejiWorkspaceRootLayout.IsWithinRoot("C:\\workspace\\shared", "c:\\WORKSPACE\\SHARED\\child.txt"));
    }

    [Fact]
    public void PrefixCollisionIsOutsideRoot()
    {
        Assert.False(KejiWorkspaceRootLayout.IsWithinRoot("C:\\workspace\\shared", "C:\\workspace\\shared2"));
    }

    [Fact]
    public void UserPrefixCollisionIsOutsideRoot()
    {
        Assert.False(KejiWorkspaceRootLayout.IsWithinRoot("C:\\workspace\\user", "C:\\workspace\\user2"));
    }
}
