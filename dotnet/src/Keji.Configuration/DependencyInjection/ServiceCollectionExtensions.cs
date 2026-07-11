using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKejiConfiguration(this IServiceCollection services)
    {
        return AddKejiConfiguration(services, _ => { });
    }

    public static IServiceCollection AddKejiConfiguration(
        this IServiceCollection services,
        Action<KejiConfigurationLoadOptions> configureOptions)
    {
        var options = new KejiConfigurationLoadOptions();
        configureOptions(options);

        services.AddSingleton(options);

        var dotEnvPath = Path.Combine(options.ProjectRoot, options.DotEnvFileName);
        var dotEnvStore = new DotEnvStore(dotEnvPath, options.MaxDotEnvFileBytes);
        services.AddSingleton<IDotEnvStore>(dotEnvStore);

        services.AddSingleton<IEnvironmentValueSource>(sp =>
        {
            var dotEnv = sp.GetRequiredService<IDotEnvStore>();
            return new CompositeEnvironmentValueSource(
                new ProcessEnvironmentValueSource(),
                new DotEnvEnvironmentValueSource(dotEnv)
            );
        });

        services.AddSingleton<IKejiConfigurationLoader>(sp =>
        {
            return new SafeYamlConfigurationLoader();
        });

        services.AddSingleton<ISecretMasker>(_ => new SecretMasker());

        services.AddSingleton(sp =>
        {
            var loader = sp.GetRequiredService<IKejiConfigurationLoader>();
            var options = sp.GetRequiredService<KejiConfigurationLoadOptions>();
            var doc = loader.Load(options);

            var envSource = sp.GetRequiredService<IEnvironmentValueSource>();
            var resolver = new EnvironmentReferenceResolver(envSource, options.FailOnMissingEnvironmentVariable);
            var resolved = resolver.Resolve(doc.Root);

            return new KejiConfigurationDocument(
                resolved as ConfigMap ?? throw new InvalidOperationException("Root node must be a map"));
        });

        return services;
    }
}
