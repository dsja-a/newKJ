using Keji.Configuration.Models;
using Keji.Configuration.Secrets;

namespace Keji.Configuration.Loading;

public class KejiConfigurationLoader : IKejiConfigurationLoader
{
    private readonly ISafeYamlConfigurationLoader _yamlLoader;
    private readonly IDotEnvStore? _dotEnvStore;

    public KejiConfigurationLoader(ISafeYamlConfigurationLoader yamlLoader, IDotEnvStore? dotEnvStore = null)
    {
        _yamlLoader = yamlLoader;
        _dotEnvStore = dotEnvStore;
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

    private IEnvironmentValueSource CreateEnvironmentValueSource(KejiConfigurationLoadOptions options)
    {
        var sources = new List<IEnvironmentValueSource>
        {
            new ProcessEnvironmentValueSource(),
        };

        if (_dotEnvStore is not null)
        {
            sources.Add(new DotEnvEnvironmentValueSource(_dotEnvStore));
        }
        else
        {
            var dotEnvPath = Path.Combine(options.ProjectRoot, options.DotEnvFileName);
            var store = new DotEnvStore(dotEnvPath, options.MaxDotEnvFileBytes, options.MaxDotEnvLineLength);
            sources.Add(new DotEnvEnvironmentValueSource(store));
        }

        return new CompositeEnvironmentValueSource(sources);
    }
}
