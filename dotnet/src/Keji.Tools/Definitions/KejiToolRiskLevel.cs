namespace Keji.Tools.Definitions;

public enum KejiToolRiskLevel
{
    ReadOnly = 1,
    Mutating = 2,
    ExternalSideEffect = 3,
    Privileged = 4,
}
