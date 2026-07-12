using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public interface ISecretMasker
{
    ConfigNode Mask(ConfigNode node);
    IReadOnlyDictionary<string, object?> Mask(IReadOnlyDictionary<string, object?> dictionary);
    IReadOnlyList<object?> Mask(IEnumerable<object?> sequence);
}
