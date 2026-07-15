namespace Keji.ToolWorker.Execution;

public static class GetTimeExecutor
{
    public static string Execute() => DateTime.UtcNow.ToString("o");
}
