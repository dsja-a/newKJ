namespace Keji.Security.Exceptions;

public class TokenValidationException : KejiSecurityException
{
    public TokenValidationException(string message) : base(message) { }
    public TokenValidationException(string message, Exception inner) : base(message, inner) { }
}
