namespace Keji.Persistence.Models;

public class UserAccountRecord
{
    public string Id { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string PasswordHash { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = "member";
    public bool IsActive { get; init; } = true;
    public double CreatedAt { get; init; }
    public double? LastLoginAt { get; init; }
}

public class UserSummaryRecord
{
    public string Id { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Role { get; init; } = "member";
    public bool IsActive { get; init; } = true;
    public double CreatedAt { get; init; }
    public double? LastLoginAt { get; init; }
}

public class UpdateUserCommand
{
    public string? DisplayName { get; init; }
    public string? Role { get; init; }
    public bool? IsActive { get; init; }
    public string? PasswordHash { get; init; }
}
