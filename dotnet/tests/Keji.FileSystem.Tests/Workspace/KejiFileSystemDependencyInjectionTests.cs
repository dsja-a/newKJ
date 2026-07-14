using Keji.FileSystem.Workspace;
using Keji.Security.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.FileSystem.Tests.Workspace;

public sealed class KejiFileSystemDependencyInjectionTests
{
    [Fact]
    public void RegistrationResolvesCompleteWorkspacePipeline()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserAccessor>(new NullAccessor());
        services.AddKejiWorkspaceFileSystem(
            new KejiWorkspaceOptions("C:\\keji-workspace"),
            new KejiWorkspaceFileSystemOptions(maxContentBytes: 4096, maxEntries: 32));

        using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        using var scope = provider.CreateScope();

        Assert.IsType<KejiWorkspacePathCandidateResolver>(
            scope.ServiceProvider.GetRequiredService<IKejiWorkspacePathCandidateResolver>());
        Assert.IsType<KejiWindowsPathInspector>(
            scope.ServiceProvider.GetRequiredService<IKejiWindowsPathInspector>());
        Assert.IsType<KejiWorkspaceAccessPolicy>(
            scope.ServiceProvider.GetRequiredService<IKejiWorkspaceAccessPolicy>());
        Assert.IsType<KejiWorkspaceFileSystem>(
            scope.ServiceProvider.GetRequiredService<IKejiWorkspaceFileSystem>());
    }

    [Fact]
    public void ScopedServicesAreIsolatedAndSingletonServicesAreShared()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentUserAccessor>(new NullAccessor());
        services.AddKejiWorkspaceFileSystem(
            new KejiWorkspaceOptions("C:\\keji-workspace"));
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var firstFileSystem = firstScope.ServiceProvider
            .GetRequiredService<IKejiWorkspaceFileSystem>();
        var secondFileSystem = secondScope.ServiceProvider
            .GetRequiredService<IKejiWorkspaceFileSystem>();
        var firstResolver = firstScope.ServiceProvider
            .GetRequiredService<IKejiWorkspacePathCandidateResolver>();
        var secondResolver = secondScope.ServiceProvider
            .GetRequiredService<IKejiWorkspacePathCandidateResolver>();

        Assert.NotSame(firstFileSystem, secondFileSystem);
        Assert.Same(firstResolver, secondResolver);
    }

    [Fact]
    public void RegistrationRejectsNullArguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            KejiFileSystemServiceCollectionExtensions.AddKejiWorkspaceFileSystem(
                null!,
                new KejiWorkspaceOptions("C:\\keji-workspace")));
        Assert.Throws<ArgumentNullException>(() =>
            services.AddKejiWorkspaceFileSystem(null!));
    }

    private sealed class NullAccessor : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => null;
    }
}
