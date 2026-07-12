using Keji.Configuration.Models;
using Keji.Configuration.Secrets;

namespace Keji.Configuration.Loading;

public class KejiConfigurationLoader : IKejiConfigurationLoader
{
    private readonly ISafeYamlConfigurationLoader _yamlLoader;

    public KejiConfigurationLoader(ISafeYamlConfigurationLoader yamlLoader)
    {
        _yamlLoader = yamlLoader;
    }

    public KejiConfigurationLoadResult Load(KejiConfigurationLoadOptions options)
    {
        var envSource = CreateEnvironmentValueSource(options);

        var yamlRoot = _yamlLoader.Load(options);

        var resolver = new EnvironmentReferenceResolver(envSource, options.FailOnMissingEnvironmentVariable);
        var resolutionResult = resolver.Resolve(yamlRoot);

        if (resolutionResult.Root is not ConfigMap resolvedMap)
            throw new KejiConfigurationException("Root node must be a mapping after environment resolution.");

        var document = new KejiConfigurationDocument(resolvedMap);
        return new KejiConfigurationLoadResult(document, resolutionResult.Diagnostics);
    }

    private static IEnvironmentValueSource CreateEnvironmentValueSource(KejiConfigurationLoadOptions options)
    {
        var dotEnvPath = Path.Combine(options.ProjectRoot, options.DotEnvFileName);
        var store = new DotEnvStore(dotEnvPath, options.MaxDotEnvFileBytes, options.MaxDotEnvLineLength);

        return new CompositeEnvironmentValueSource(
            new ProcessEnvironmentValueSource(),
            new DotEnvEnvironmentValueSource(store)
        );
    }
}
