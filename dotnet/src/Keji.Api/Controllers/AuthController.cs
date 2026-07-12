using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Contracts.DTOs;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Models;
using Keji.Security.Options;
using Keji.Security.Services;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IKejiLoginService _loginService;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly KejiSecurityOptions _securityOptions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AuthController(
        IKejiLoginService loginService,
        IUserRepository userRepository,
        ICurrentUserAccessor currentUserAccessor,
        KejiSecurityOptions securityOptions)
    {
        _loginService = loginService;
        _userRepository = userRepository;
        _currentUserAccessor = currentUserAccessor;
        _securityOptions = securityOptions;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (_securityOptions.AuthMode == KejiAuthMode.ApiKeyOnly)
            return StatusCode(503, new ApiErrorResponse { Detail = "当前认证模式不支持用户登录" });

        var validationError = ValidateLoginRequest(request);
        if (validationError != null)
            return validationError;

        var result = await _loginService.LoginAsync(request!.Username!, request!.Password!, cancellationToken);

        if (!result.Success)
        {
            return StatusCode(401, new ApiErrorResponse { Detail = "用户名或密码错误" });
        }

        var response = new LoginResponse
        {
            Token = result.Token!,
            ExpiresIn = result.ExpiresIn,
            User = result.User,
        };

        return Ok(response);
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var currentUser = _currentUserAccessor.CurrentUser;

        if (currentUser == null)
        {
            return StatusCode(401, new ApiErrorResponse { Detail = "未登录，请先登录" });
        }

        var user = await _userRepository.GetByIdAsync(currentUser.Id, cancellationToken);
        if (user == null || !user.IsActive)
        {
            return StatusCode(401, new ApiErrorResponse { Detail = "账号已禁用或不存在" });
        }

        var publicUser = new PublicUserResponse
        {
            Id = user.Id,
            Username = user.Username,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
        };

        return Ok(new AuthMeResponse { User = publicUser });
    }

    private IActionResult? ValidateLoginRequest(LoginRequest? request)
    {
        if (request == null)
            return StatusCode(422, new ApiErrorResponse { Detail = "请求格式错误" });

        if (string.IsNullOrEmpty(request.Username) || request.Username.Length > 64)
            return StatusCode(422, new ApiErrorResponse { Detail = "请求格式错误" });

        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return StatusCode(422, new ApiErrorResponse { Detail = "请求格式错误" });

        return null;
    }
}
