using Keji.Persistence.Repositories;
using Keji.Security.Models;
using Keji.Security.Options;

namespace Keji.Security.Services;

public class KejiLoginService : IKejiLoginService
{
    private static readonly string DummyHash = BCrypt.Net.BCrypt.HashPassword("__dummy_placeholder__", 12);

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IAccessTokenService _tokenService;
    private readonly KejiSecurityOptions _options;

    public KejiLoginService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IAccessTokenService tokenService,
        KejiSecurityOptions options)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _options = options;
    }

    public async Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var trimmedUsername = username?.Trim() ?? string.Empty;

        var user = await _userRepository.GetByUsernameAsync(trimmedUsername, cancellationToken);

        if (user == null || !user.IsActive)
        {
            _passwordHasher.Verify(password, DummyHash);
            return LoginResult.Failed("用户名或密码错误");
        }

        var valid = _passwordHasher.Verify(password, user.PasswordHash);
        if (!valid)
        {
            return LoginResult.Failed("用户名或密码错误");
        }

        await _userRepository.TouchLoginAsync(user.Id, cancellationToken);

        var tokenResult = _tokenService.CreateToken(user.Id, user.Username, user.Role);

        var publicUser = new Keji.Contracts.DTOs.PublicUserResponse
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
        };

        return LoginResult.Succeeded(tokenResult.Token, tokenResult.ExpiresIn, publicUser);
    }
}
