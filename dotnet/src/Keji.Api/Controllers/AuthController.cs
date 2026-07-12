using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Contracts.DTOs;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Models;
using Keji.Security.Services;
using Microsoft.AspNetCore.Mvc;

namespace Keji.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IKejiLoginService _loginService;
    private readonly IUserRepository _userRepository;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public AuthController(
        IKejiLoginService loginService,
        IUserRepository userRepository,
        ICurrentUserAccessor currentUserAccessor)
    {
        _loginService = loginService;
        _userRepository = userRepository;
        _currentUserAccessor = currentUserAccessor;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.Username) || request.Username.Length > 64)
            return CreateError(422, "用户名或密码错误");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 128)
            return CreateError(422, "用户名或密码错误");

        var result = await _loginService.LoginAsync(request.Username, request.Password, cancellationToken);

        if (!result.Success)
        {
            return CreateError(401, result.ErrorMessage ?? "用户名或密码错误");
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
            return CreateError(401, "未登录，请先登录");
        }

        var user = await _userRepository.GetByIdAsync(currentUser.Id, cancellationToken);
        if (user == null || !user.IsActive)
        {
            return CreateError(401, "账号已禁用或不存在");
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

    private IActionResult CreateError(int statusCode, string detail)
    {
        return StatusCode(statusCode, new ApiErrorResponse { Detail = detail });
    }
}
