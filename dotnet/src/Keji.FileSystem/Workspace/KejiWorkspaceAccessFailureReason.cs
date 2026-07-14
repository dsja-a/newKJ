namespace Keji.FileSystem.Workspace;

public enum KejiWorkspaceAccessFailureReason
{
    None = 0,
    Unauthenticated,
    InvalidRequest,
    InvalidActorId,
    UnknownRole,
    InvalidOperation,
    CandidateRejected,
    OtherUserWorkspaceDenied,
    ReadonlyWriteDenied,
    PlatformNotSupported,
    WorkspaceRootMissing,
    PathInspectionFailed,
    ReparsePointDenied,
    HardLinkDenied,
    TargetTypeMismatch,
    TargetNotFound,
    TargetAlreadyExists,
    ParentNotDirectory,
    FinalPathEscapesRoot,
    RaceDetected,
    ContentTooLarge,
    InvalidContent,
    FileSystemError,
}
