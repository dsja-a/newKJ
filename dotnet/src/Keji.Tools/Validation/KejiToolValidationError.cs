namespace Keji.Tools.Validation;

public enum KejiToolValidationError
{
    InvalidToolName = 1,
    UnknownTool = 2,
    MissingRequiredParameter = 3,
    UnknownParameter = 4,
    InvalidParameterType = 5,
    ValueOutOfRange = 6,
    ValueTooLong = 7,
    TooManyItems = 8,
    InvalidEnumValue = 9,
}
