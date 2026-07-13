namespace Keji.Security.Authorization;

public enum KejiAuthorizationFailureReason
{
    None,
    Unauthenticated,
    UnknownRole,
    MissingPermissionMetadata,
    InvalidPermissionMetadata,
    PermissionDenied,
    AdminRequired,
    ReadonlyWriteDenied,
    UnknownTool,
    InvalidToolDescriptor,
}
