namespace Keji.SmartQuery;

public sealed class KejiSmartQueryOptions
{
    public static readonly TimeSpan MaximumRunTimeout = TimeSpan.FromSeconds(120);
    public const int SystemMaxRows = 200;
    public string ProviderName { get; }
    public string Model { get; }
    public bool IncludeSummary { get; }
    public TimeSpan RunTimeout { get; }
    public TimeSpan ConnectTimeout { get; }
    public TimeSpan QueryTimeout { get; }
    public TimeSpan PlannerTimeout { get; }
    public int MaxColumns { get; }
    public int MaxOrderBy { get; }
    public int MaxQuestionBytes { get; }
    public int MaxPlanBytes { get; }
    public int MaxSchemaBytes { get; }
    public int MaxCellBytes { get; }
    public int MaxRowBytes { get; }
    public int MaxResultBytes { get; }
    public int MaxFilterDepth => 4;
    public int MaxFilterNodes => 64;
    public int MaxFilterLeaves => 32;
    public int MaxInItems => 100;
    public int MaxParameters => 256;
    public int MaxTables => 128;
    public int MaxTotalColumns => 4096;
    public int MaxColumnsPerTable => 256;
    public int MaxForeignKeys => 1024;

    public KejiSmartQueryOptions(
        string providerName = "openai", string model = "gpt-4o", bool includeSummary = false,
        TimeSpan? runTimeout = null, TimeSpan? connectTimeout = null,
        TimeSpan? queryTimeout = null, TimeSpan? plannerTimeout = null,
        int maxColumns = 64, int maxOrderBy = 8, int maxQuestionBytes = 16 * 1024,
        int maxPlanBytes = 64 * 1024, int maxSchemaBytes = 512 * 1024,
        int maxCellBytes = 64 * 1024, int maxRowBytes = 256 * 1024,
        int maxResultBytes = 2 * 1024 * 1024)
    {
        RunTimeout = runTimeout ?? TimeSpan.FromSeconds(60);
        ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(10);
        QueryTimeout = queryTimeout ?? TimeSpan.FromSeconds(15);
        PlannerTimeout = plannerTimeout ?? TimeSpan.FromSeconds(30);
        if (!ValidName(providerName, 32)) throw new ArgumentException("Invalid provider.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256) throw new ArgumentException("Invalid model.", nameof(model));
        ValidateTimeout(RunTimeout, MaximumRunTimeout, nameof(runTimeout));
        ValidateTimeout(ConnectTimeout, TimeSpan.FromSeconds(30), nameof(connectTimeout));
        ValidateTimeout(QueryTimeout, TimeSpan.FromSeconds(60), nameof(queryTimeout));
        ValidateTimeout(PlannerTimeout, TimeSpan.FromSeconds(60), nameof(plannerTimeout));
        if (maxColumns is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        if (maxOrderBy is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(maxOrderBy));
        if (maxQuestionBytes is < 256 or > 65536) throw new ArgumentOutOfRangeException(nameof(maxQuestionBytes));
        if (maxPlanBytes is < 1024 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxPlanBytes));
        if (maxSchemaBytes is < 1024 or > 512 * 1024) throw new ArgumentOutOfRangeException(nameof(maxSchemaBytes));
        if (maxCellBytes is < 256 or > 64 * 1024) throw new ArgumentOutOfRangeException(nameof(maxCellBytes));
        if (maxRowBytes is < 1024 or > 256 * 1024) throw new ArgumentOutOfRangeException(nameof(maxRowBytes));
        if (maxResultBytes is < 1024 or > 2 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxResultBytes));
        ProviderName = providerName; Model = model; IncludeSummary = includeSummary;
        MaxColumns = maxColumns; MaxOrderBy = maxOrderBy; MaxQuestionBytes = maxQuestionBytes;
        MaxPlanBytes = maxPlanBytes; MaxSchemaBytes = maxSchemaBytes; MaxCellBytes = maxCellBytes;
        MaxRowBytes = maxRowBytes; MaxResultBytes = maxResultBytes;
    }
    private static void ValidateTimeout(TimeSpan value, TimeSpan max, string name)
    {
        if (value <= TimeSpan.Zero || value > max) throw new ArgumentOutOfRangeException(name);
    }
    private static bool ValidName(string value, int max) => value.Length is > 0 && value.Length <= max &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
