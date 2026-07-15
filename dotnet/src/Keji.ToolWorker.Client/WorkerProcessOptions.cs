namespace Keji.ToolWorker.Client;

public sealed class WorkerProcessOptions
{
    private string _executablePath = "";
    private TimeSpan _timeout = TimeSpan.FromSeconds(10);
    private long? _maxProcessMemoryBytes;
    private int? _maxActiveProcesses;
    private int _maxFrameSize = 1_048_576;
    private int _maxStderrLength = 8192;

    private const long MaxMemoryBytes = 268_435_456; // 256 MiB
    private const int MaxFrameSizeLimit = 1_048_576; // 1 MiB
    private const int MaxStderrLengthLimit = 8192;   // 8 KiB

    public string ExecutablePath
    {
        get => _executablePath;
        set => _executablePath = value ?? throw new ArgumentNullException(nameof(value));
    }

    public TimeSpan Timeout
    {
        get => _timeout;
        set
        {
            if (value <= TimeSpan.Zero || value > TimeSpan.FromSeconds(60))
                throw new ArgumentOutOfRangeException(nameof(value), "Timeout must be between 1ms and 60s");
            _timeout = value;
        }
    }

    public long? MaxProcessMemoryBytes
    {
        get => _maxProcessMemoryBytes;
        set
        {
            if (value.HasValue && (value.Value <= 0 || value.Value > MaxMemoryBytes))
                throw new ArgumentOutOfRangeException(nameof(value), $"MaxProcessMemoryBytes must be between 1 and {MaxMemoryBytes}");
            _maxProcessMemoryBytes = value;
        }
    }

    public int? MaxActiveProcesses
    {
        get => _maxActiveProcesses;
        set
        {
            if (value.HasValue && value.Value != 1)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxActiveProcesses must be 1");
            _maxActiveProcesses = value;
        }
    }

    public int MaxFrameSize
    {
        get => _maxFrameSize;
        set
        {
            if (value <= 0 || value > MaxFrameSizeLimit)
                throw new ArgumentOutOfRangeException(nameof(value), $"MaxFrameSize must be between 1 and {MaxFrameSizeLimit}");
            _maxFrameSize = value;
        }
    }

    public int MaxStderrLength
    {
        get => _maxStderrLength;
        set
        {
            if (value <= 0 || value > MaxStderrLengthLimit)
                throw new ArgumentOutOfRangeException(nameof(value), $"MaxStderrLength must be between 1 and {MaxStderrLengthLimit}");
            _maxStderrLength = value;
        }
    }

    public WorkerProcessOptions()
    {
    }
}
