using Keji.Configuration.Models;

namespace Keji.Configuration.Secrets;

public interface IDotEnvStore
{
    string? GetValue(string key);
    IReadOnlyDictionary<string, string> GetSnapshot();
    Task<DotEnvUpsertResult> UpsertAsync(string key, string value, CancellationToken cancellationToken = default);
    bool RemoveValue(string key);
}
