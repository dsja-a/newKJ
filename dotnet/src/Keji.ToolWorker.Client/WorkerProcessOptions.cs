namespace Keji.ToolWorker.Client;

public sealed record WorkerProcessOptions
{
    public string ExecutablePath { get; init; } = "";
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
    public long? MaxProcessMemoryBytes { get; init; }
    public int? MaxActiveProcesses { get; init; }
    public int MaxFrameSize { get; init; } = 1_048_576;
    public int MaxStderrLength { get; init; } = 8192;
}
