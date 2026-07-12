using Keji.Configuration.Models;

namespace Keji.Configuration.Loading;

public interface IKejiConfigurationLoader
{
    KejiConfigurationLoadResult Load(KejiConfigurationLoadOptions options);
}
