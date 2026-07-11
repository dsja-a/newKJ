namespace Keji.Configuration.Secrets;

public interface IDotEnvStore
{
    string? GetValue(string key);
    IReadOnlyDictionary<string, string> GetAll();
    void SetValue(string key, string value);
    bool RemoveValue(string key);
    void Reload();
}
