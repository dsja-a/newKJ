namespace Keji.Security.Models;

public class AccessTokenClaims
{
    public string Sub { get; }
    public string Username { get; }
    public string Role { get; }
    public long Iat { get; }
    public long Exp { get; }
    public string Jti { get; }

    public AccessTokenClaims(string sub, string username, string role, long iat, long exp, string jti)
    {
        Sub = sub;
        Username = username;
        Role = role;
        Iat = iat;
        Exp = exp;
        Jti = jti;
    }
}
