using Keji.Configuration.Models;

namespace Keji.Configuration.Loading;

public interface ISafeYamlConfigurationLoader
{
    ConfigNode Load(KejiConfigurationLoadOptions options, string? overrideConfigPath = null);
}
