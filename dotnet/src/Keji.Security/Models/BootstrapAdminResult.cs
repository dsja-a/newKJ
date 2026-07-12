namespace Keji.Security.Models;

public class BootstrapAdminResult
{
    public bool IsCreated { get; }
    public string? AdminId { get; }
    public string Message { get; }

    private BootstrapAdminResult(bool isCreated, string? adminId, string message)
    {
        IsCreated = isCreated;
        AdminId = adminId;
        Message = message;
    }

    public static BootstrapAdminResult Created(string adminId)
        => new(true, adminId, "Admin user created.");

    public static BootstrapAdminResult AlreadyCreated(string adminId)
        => new(false, adminId, "Admin user was already created by another process.");

    public static BootstrapAdminResult SkippedExistingUsers()
        => new(false, null, "Users already exist, skipping bootstrap admin creation.");
}
