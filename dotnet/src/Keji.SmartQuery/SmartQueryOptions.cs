namespace Keji.SmartQuery;

public sealed class KejiSmartQueryOptions
{
    public int MaxRows { get; }
    public int MaxColumns { get; }
    public int MaxFilters { get; }
    public int MaxOrderBy { get; }
    public int MaxQuestionBytes { get; }
    public int MaxPlanBytes { get; }
    public int MaxSchemaBytes { get; }
    public int MaxCellBytes { get; }
    public int MaxResultBytes { get; }
    public int CommandTimeoutSeconds { get; }

    public KejiSmartQueryOptions(int maxRows = 500, int maxColumns = 32, int maxFilters = 16,
        int maxOrderBy = 4, int maxQuestionBytes = 16 * 1024, int maxPlanBytes = 64 * 1024,
        int maxSchemaBytes = 128 * 1024,
        int maxCellBytes = 64 * 1024, int maxResultBytes = 2 * 1024 * 1024, int commandTimeoutSeconds = 30)
    {
        if (maxRows is < 1 or > 5000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        if (maxColumns is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        if (maxFilters is < 0 or > 64) throw new ArgumentOutOfRangeException(nameof(maxFilters));
        if (maxOrderBy is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(maxOrderBy));
        if (maxQuestionBytes is < 256 or > 65536) throw new ArgumentOutOfRangeException(nameof(maxQuestionBytes));
        if (maxPlanBytes is < 1024 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxPlanBytes));
        if (maxSchemaBytes is < 1024 or > 512 * 1024) throw new ArgumentOutOfRangeException(nameof(maxSchemaBytes));
        if (maxCellBytes is < 256 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxCellBytes));
        if (maxResultBytes is < 1024 or > 8 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxResultBytes));
        if (commandTimeoutSeconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        MaxRows = maxRows; MaxColumns = maxColumns; MaxFilters = maxFilters; MaxOrderBy = maxOrderBy;
        MaxQuestionBytes = maxQuestionBytes; MaxPlanBytes = maxPlanBytes; MaxSchemaBytes = maxSchemaBytes; MaxCellBytes = maxCellBytes;
        MaxResultBytes = maxResultBytes; CommandTimeoutSeconds = commandTimeoutSeconds;
    }
}
