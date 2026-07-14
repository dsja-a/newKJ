namespace Keji.FileSystem.Workspace;

public enum KejiPathSandboxFailureReason
{
    None = 0,
    InvalidScope,
    InvalidWorkspaceRoot,
    MissingRelativePath,
    WhitespaceRelativePath,
    PathTooLong,
    RootedPath,
    DriveQualifiedPath,
    UncPath,
    DevicePath,
    TraversalSegment,
    CurrentDirectorySegment,
    EmptySegment,
    InvalidCharacter,
    ControlCharacter,
    AlternateDataStream,
    TrailingDotOrSpace,
    ReservedDeviceName,
    SegmentTooLong,
    UnexpectedUserId,
    MissingUserId,
    InvalidUserId,
    PathEscapesRoot,
    PathNormalizationFailed,
}
