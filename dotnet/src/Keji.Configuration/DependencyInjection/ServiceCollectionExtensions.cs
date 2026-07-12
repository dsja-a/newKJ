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

        var dotEnvPath = Path.Combine(options.ProjectRoot, options.DotEnvFileName);
        var dotEnvStore = new DotEnvStore(dotEnvPath, options.MaxDotEnvFileBytes, options.MaxDotEnvLineLength);
        services.AddSingleton<IDotEnvStore>(dotEnvStore);

        services.AddSingleton<IEnvironmentValueSource>(sp =>
        {
            var dotEnv = sp.GetRequiredService<IDotEnvStore>();
            return new CompositeEnvironmentValueSource(
                new ProcessEnvironmentValueSource(),
                new DotEnvEnvironmentValueSource(dotEnv)
            );
        });

        services.AddSingleton<IKejiConfigurationLoader>(_ => new SafeYamlConfigurationLoader());
        services.AddSingleton<ISecretMasker>(_ => new SecretMasker());

        return services;
    }
}
