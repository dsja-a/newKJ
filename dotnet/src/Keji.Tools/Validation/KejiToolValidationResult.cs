using System.Collections.Frozen;

namespace Keji.Tools.Validation;

public sealed class KejiToolValidationResult
{
    public bool IsValid { get; }
    public IReadOnlyList<KejiToolValidationError> Errors { get; }
    public string? ErrorMessage { get; }

    private static readonly FrozenSet<string> EmptySet = new List<string>().ToFrozenSet(StringComparer.Ordinal);

    private KejiToolValidationResult(bool isValid, IReadOnlyList<KejiToolValidationError> errors, string? errorMessage)
    {
        IsValid = isValid;
        Errors = errors;
        ErrorMessage = errorMessage;
    }

    public static KejiToolValidationResult Valid() =>
        new(true, Array.Empty<KejiToolValidationError>(), null);

    public static KejiToolValidationResult Invalid(KejiToolValidationError error, string message) =>
        new(false, new[] { error }, message);
}
