namespace Keji.Security.Models;

public class AccessTokenResult
{
    public string Token { get; }
    public int ExpiresIn { get; }

    public AccessTokenResult(string token, int expiresIn)
    {
        Token = token;
        ExpiresIn = expiresIn;
    }
}
