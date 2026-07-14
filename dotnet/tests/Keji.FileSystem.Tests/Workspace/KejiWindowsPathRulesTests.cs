using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWindowsPathRulesTests
{
    [Theory]
    [InlineData("//?/C:/file")]
    [InlineData("//./PhysicalDrive0")]
    [InlineData("/??/C:/file")]
    public void SlashDevicePathsAreRejected(string path)
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, path));
        Assert.Equal(KejiPathSandboxFailureReason.DevicePath, result.FailureReason);
    }

    [Theory]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("\"")]
    [InlineData("|")]
    [InlineData("?")]
    [InlineData("*")]
    public void EachIllegalCharacterIsRejected(string character)
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "file" + character));
        Assert.Equal(KejiPathSandboxFailureReason.InvalidCharacter, result.FailureReason);
    }

    [Theory]
    [InlineData("CON .txt")]
    [InlineData("NUL .json")]
    [InlineData("COM1 .log")]
    public void ReservedNamesWithTrailingSpaceAreRejected(string path)
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, path));
        Assert.Equal(KejiPathSandboxFailureReason.ReservedDeviceName, result.FailureReason);
    }

    [Fact]
    public void HighSurrogateReturnsNormalizationFailure()
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "\uD800"));
        Assert.Equal(KejiPathSandboxFailureReason.PathNormalizationFailed, result.FailureReason);
    }

    [Fact]
    public void LowSurrogateReturnsNormalizationFailure()
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "\uDC00"));
        Assert.Equal(KejiPathSandboxFailureReason.PathNormalizationFailed, result.FailureReason);
    }

    [Fact]
    public void EmbeddedHighSurrogateReturnsNormalizationFailure()
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "a\uD800b"));
        Assert.Equal(KejiPathSandboxFailureReason.PathNormalizationFailed, result.FailureReason);
    }

    [Fact]
    public void EmbeddedLowSurrogateReturnsNormalizationFailure()
    {
        var result = NewResolver().Resolve(new(KejiWorkspaceScope.Shared, null, "a\uDC00b"));
        Assert.Equal(KejiPathSandboxFailureReason.PathNormalizationFailed, result.FailureReason);
    }

    private static KejiWorkspacePathCandidateResolver NewResolver() =>
        new(new KejiWorkspaceOptions("C:\\workspace"));
}
