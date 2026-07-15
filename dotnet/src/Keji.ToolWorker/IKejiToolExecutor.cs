namespace Keji.ToolWorker;

public interface IKejiToolExecutor
{
    string ToolName { get; }
    object Execute(string inputJson);
}
