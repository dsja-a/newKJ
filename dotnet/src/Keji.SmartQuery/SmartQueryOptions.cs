namespace Keji.SmartQuery;

public sealed class KejiSmartQueryOptions
{
    public const int SystemMaxRows = 200;
    public string ProviderName { get; }
    public string Model { get; }
    public bool IncludeSummary { get; }
    public int MaxColumns { get; }
    public int MaxOrderBy { get; }
    public int MaxQuestionBytes { get; }
    public int MaxPlanBytes { get; }
    public int MaxSchemaBytes { get; }
    public int MaxCellBytes { get; }
    public int MaxResultBytes { get; }
    public int CommandTimeoutSeconds { get; }
    public int MaxFilterDepth => 4;
    public int MaxFilterNodes => 64;
    public int MaxFilterLeaves => 32;
    public int MaxInItems => 100;
    public int MaxParameters => 256;

    public KejiSmartQueryOptions(
        string providerName = "openai", string model = "gpt-4o", bool includeSummary = false,
        int maxColumns = 32, int maxOrderBy = 8, int maxQuestionBytes = 16 * 1024,
        int maxPlanBytes = 64 * 1024, int maxSchemaBytes = 128 * 1024,
        int maxCellBytes = 64 * 1024, int maxResultBytes = 2 * 1024 * 1024,
        int commandTimeoutSeconds = 30)
    {
        if (!ValidName(providerName, 32)) throw new ArgumentException("Invalid provider.", nameof(providerName));
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256) throw new ArgumentException("Invalid model.", nameof(model));
        if (maxColumns is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maxColumns));
        if (maxOrderBy is < 0 or > 16) throw new ArgumentOutOfRangeException(nameof(maxOrderBy));
        if (maxQuestionBytes is < 256 or > 65536) throw new ArgumentOutOfRangeException(nameof(maxQuestionBytes));
        if (maxPlanBytes is < 1024 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxPlanBytes));
        if (maxSchemaBytes is < 1024 or > 512 * 1024) throw new ArgumentOutOfRangeException(nameof(maxSchemaBytes));
        if (maxCellBytes is < 256 or > 262144) throw new ArgumentOutOfRangeException(nameof(maxCellBytes));
        if (maxResultBytes is < 1024 or > 8 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxResultBytes));
        if (commandTimeoutSeconds is < 1 or > 120) throw new ArgumentOutOfRangeException(nameof(commandTimeoutSeconds));
        ProviderName = providerName; Model = model; IncludeSummary = includeSummary;
        MaxColumns = maxColumns; MaxOrderBy = maxOrderBy; MaxQuestionBytes = maxQuestionBytes;
        MaxPlanBytes = maxPlanBytes; MaxSchemaBytes = maxSchemaBytes; MaxCellBytes = maxCellBytes;
        MaxResultBytes = maxResultBytes; CommandTimeoutSeconds = commandTimeoutSeconds;
    }

    private static bool ValidName(string value, int max) => value.Length is > 0 && value.Length <= max &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}
