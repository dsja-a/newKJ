namespace Keji.ToolWorker;

public sealed class GetTimeToolExecutor : IKejiToolExecutor
{
    public string ToolName => "get_time";

    public object Execute(string inputJson)
    {
        var utcNow = DateTimeOffset.UtcNow;
        return new Dictionary<string, object>
        {
            ["utc_iso8601"] = utcNow.ToString("O"),
            ["unix_seconds"] = utcNow.ToUnixTimeSeconds(),
            ["unix_milliseconds"] = utcNow.ToUnixTimeMilliseconds()
        };
    }
}
