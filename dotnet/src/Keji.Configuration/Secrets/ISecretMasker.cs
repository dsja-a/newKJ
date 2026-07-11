using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public interface ISecretMasker
{
    ConfigNode Mask(ConfigNode node);
}
