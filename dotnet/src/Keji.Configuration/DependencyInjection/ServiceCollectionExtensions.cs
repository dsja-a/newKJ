using Keji.Configuration.Loading;
using Keji.Configuration.Models;
using Keji.Configuration.Secrets;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddKejiConfigurationFoundation(
        this IServiceCollection services)
    {
        return AddKejiConfigurationFoundation(services, _ => { });
    }

    public static IServiceCollection AddKejiConfigurationFoundation(
        this IServiceCollection services,
        Action<KejiConfigurationLoadOptions> configureOptions)
    {
        var options = new KejiConfigurationLoadOptions();
        configureOptions(options);

        services.AddSingleton(options);

        services.AddSingleton<IDotEnvStore>(sp =>
        {
            var opts = sp.GetRequiredService<KejiConfigurationLoadOptions>();
            var dotEnvPath = Path.Combine(opts.ProjectRoot, opts.DotEnvFileName);
            return new DotEnvStore(dotEnvPath, opts.MaxDotEnvFileBytes, opts.MaxDotEnvLineLength);
        });

        services.AddSingleton<IEnvironmentValueSource>(sp =>
        {
            var dotEnv = sp.GetRequiredService<IDotEnvStore>();
            return new CompositeEnvironmentValueSource(
                new ProcessEnvironmentValueSource(),
                new DotEnvEnvironmentValueSource(dotEnv)
            );
        });

        services.AddSingleton<ISafeYamlConfigurationLoader>(_ => new SafeYamlConfigurationLoader());

        services.AddSingleton<IKejiConfigurationLoader>(sp =>
        {
            var yamlLoader = sp.GetRequiredService<ISafeYamlConfigurationLoader>();
            var dotEnv = sp.GetRequiredService<IDotEnvStore>();
            return new KejiConfigurationLoader(yamlLoader, dotEnv);
        });

        services.AddSingleton<ISecretMasker>(_ => new SecretMasker());

        return services;
    }
}
