using Keji.Persistence;
using Keji.Persistence.Repositories;
using Keji.Security.Exceptions;
using Keji.Security.Models;
using Keji.Security.Options;

namespace Keji.Security.Services;

public class BootstrapAdminService : IBootstrapAdminService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly KejiSecurityOptions _options;

    public BootstrapAdminService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        KejiSecurityOptions options)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _options = options;
    }

    public async Task<BootstrapAdminResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var count = await _userRepository.CountAsync(cancellationToken);

        if (count > 0)
            return BootstrapAdminResult.SkippedExistingUsers();

        var boot = _options.BootstrapAdmin;
        var username = boot.Username?.Trim() ?? "admin";
        var password = boot.Password;
        var displayName = boot.DisplayName ?? "系统管理员";

        if (string.IsNullOrEmpty(password))
            throw new KejiSecurityConfigurationException("Bootstrap admin password is required when no users exist. Configure security.bootstrap_admin.password in config.yaml or KEJI_ADMIN_PASSWORD environment variable.");

        if (password.Length < 12)
            throw new KejiSecurityConfigurationException("Bootstrap admin password must be at least 12 characters.");

        cancellationToken.ThrowIfCancellationRequested();

        var passwordHash = _passwordHasher.Hash(password);

        try
        {
            var uid = await _userRepository.CreateAsync(username, passwordHash, "admin", displayName, cancellationToken);
            return BootstrapAdminResult.Created(uid);
        }
        catch (DuplicateUsernameException)
        {
            var existing = await _userRepository.GetByUsernameAsync(username, cancellationToken);
            if (existing != null && existing.Role == "admin")
                return BootstrapAdminResult.AlreadyCreated(existing.Id);

            throw new KejiSecurityConfigurationException(
                $"Cannot bootstrap admin: username '{username}' is already taken by a non-admin user.");
        }
    }
}
