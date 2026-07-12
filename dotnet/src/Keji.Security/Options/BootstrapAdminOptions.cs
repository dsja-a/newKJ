namespace Keji.Security.Options;

public class BootstrapAdminOptions
{
    public string Username { get; set; } = "admin";
    public string? Password { get; set; }
    public string DisplayName { get; set; } = "系统管理员";
}
