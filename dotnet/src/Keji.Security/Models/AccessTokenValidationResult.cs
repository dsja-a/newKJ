namespace Keji.Security.Models;

public class AccessTokenValidationResult
{
    public bool IsValid { get; }
    public AccessTokenClaims? Claims { get; }
    public string? FailureReason { get; }

    private AccessTokenValidationResult(bool isValid, AccessTokenClaims? claims, string? failureReason)
    {
        IsValid = isValid;
        Claims = claims;
        FailureReason = failureReason;
    }

    public static AccessTokenValidationResult Success(AccessTokenClaims claims)
        => new(true, claims, null);

    public static AccessTokenValidationResult Fail(string reason)
        => new(false, null, reason);
}
