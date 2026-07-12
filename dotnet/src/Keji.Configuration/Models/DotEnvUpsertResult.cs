namespace Keji.Configuration.Models;

public class DotEnvUpsertResult
{
    public string Key { get; }
    public bool Created { get; }
    public bool Updated { get; }

    public DotEnvUpsertResult(string key, bool created, bool updated)
    {
        Key = key;
        Created = created;
        Updated = updated;
    }
}
