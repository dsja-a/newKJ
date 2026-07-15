using System.Text.Json;

namespace Keji.ToolWorker.Tests;

public class GetTimeExecutorTests
{
    [Fact]
    public void Execute_ReturnsValidTimestamp()
    {
        var executor = new GetTimeToolExecutor();
        var before = DateTimeOffset.UtcNow;

        var result = executor.Execute("{}");
        var dict = Assert.IsType<Dictionary<string, object>>(result);

        Assert.True(dict.ContainsKey("utc_iso8601"));
        Assert.True(dict.ContainsKey("unix_seconds"));
        Assert.True(dict.ContainsKey("unix_milliseconds"));

        var utcStr = Assert.IsType<string>(dict["utc_iso8601"]);
        var parsed = DateTimeOffset.Parse(utcStr);
        var after = DateTimeOffset.UtcNow;

        Assert.True(parsed >= before.AddSeconds(-1));
        Assert.True(parsed <= after.AddSeconds(1));
    }
}
