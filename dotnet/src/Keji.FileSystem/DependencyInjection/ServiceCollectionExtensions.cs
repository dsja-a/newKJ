using Keji.FileSystem.Workspace;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class KejiFileSystemServiceCollectionExtensions
{
    public static IServiceCollection AddKejiWorkspaceFileSystem(
        this IServiceCollection services,
        KejiWorkspaceOptions workspaceOptions,
        KejiWorkspaceFileSystemOptions? fileSystemOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workspaceOptions);

        services.AddSingleton(workspaceOptions);
        services.AddSingleton(fileSystemOptions ?? new KejiWorkspaceFileSystemOptions());
        services.TryAddSingleton<IKejiWorkspacePathCandidateResolver,
            KejiWorkspacePathCandidateResolver>();
        services.TryAddSingleton<IKejiWindowsPathInspector, KejiWindowsPathInspector>();
        services.TryAddScoped<IKejiWorkspaceAccessPolicy, KejiWorkspaceAccessPolicy>();
        services.TryAddScoped<IKejiWorkspaceFileSystem, KejiWorkspaceFileSystem>();
        return services;
    }
}
