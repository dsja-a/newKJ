using Keji.FileSystem.Workspace;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiWindowsReservedNameRegressionTests
{
    [Theory]
    [InlineData("COM\u00B9")]
    [InlineData("COM\u00B2")]
    [InlineData("COM\u00B3")]
    [InlineData("LPT\u00B9")]
    [InlineData("LPT\u00B2")]
    [InlineData("LPT\u00B3")]
    [InlineData("CONIN$")]
    [InlineData("CONOUT$")]
    public void ExtendedDosDeviceNamesAreRejected(string relativePath)
    {
        var options = new KejiWorkspaceOptions("C:\\workspace");
        var resolver = new KejiWorkspacePathCandidateResolver(options);

        var result = resolver.Resolve(new KejiWorkspacePathRequest(
            KejiWorkspaceScope.Shared,
            targetUserId: null,
            relativePath));

        Assert.False(result.IsValid);
        Assert.Equal(
            KejiPathSandboxFailureReason.ReservedDeviceName,
            result.FailureReason);
        Assert.Null(result.NormalizedRelativePath);
    }
}
