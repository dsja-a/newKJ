namespace Keji.Security.Authorization;

public sealed class KejiAuthorizationDecision
{
    private static readonly KejiAuthorizationDecision Allowed =
        new(true, KejiAuthorizationFailureReason.None);

    public bool IsAllowed { get; }
    public KejiAuthorizationFailureReason FailureReason { get; }

    private KejiAuthorizationDecision(bool isAllowed, KejiAuthorizationFailureReason failureReason)
    {
        IsAllowed = isAllowed;
        FailureReason = failureReason;
    }

    public static KejiAuthorizationDecision Allow() => Allowed;

    public static KejiAuthorizationDecision Deny(KejiAuthorizationFailureReason reason)
    {
        if (!Enum.IsDefined(reason) || reason == KejiAuthorizationFailureReason.None)
            throw new ArgumentOutOfRangeException(nameof(reason), reason, "A denial requires a valid failure reason.");

        return new KejiAuthorizationDecision(false, reason);
    }
}
