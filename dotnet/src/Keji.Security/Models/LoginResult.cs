using Keji.Contracts.DTOs;

namespace Keji.Security.Models;

public class LoginResult
{
    public bool Success { get; }
    public string? Token { get; }
    public int ExpiresIn { get; }
    public PublicUserResponse? User { get; }
    public string? ErrorMessage { get; }

    private LoginResult(bool success, string? token, int expiresIn, PublicUserResponse? user, string? errorMessage)
    {
        Success = success;
        Token = token;
        ExpiresIn = expiresIn;
        User = user;
        ErrorMessage = errorMessage;
    }

    public static LoginResult Succeeded(string token, int expiresIn, PublicUserResponse user)
        => new(true, token, expiresIn, user, null);

    public static LoginResult Failed(string message)
        => new(false, null, 0, null, message);
}
