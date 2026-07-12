namespace Keji.Persistence.Repositories;

public interface ISettingsRepository
{
    Task<string> GetAsync(string key, string defaultValue = "", CancellationToken cancellationToken = default);
    Task SetAsync(string key, string value, double timestamp, CancellationToken cancellationToken = default);
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}
