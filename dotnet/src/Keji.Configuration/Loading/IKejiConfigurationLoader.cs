using Keji.Configuration.Models;

namespace Keji.Configuration.Loading;

public interface IKejiConfigurationLoader
{
    KejiConfigurationDocument Load(KejiConfigurationLoadOptions options);
}
