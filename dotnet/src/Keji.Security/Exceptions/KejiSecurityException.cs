namespace Keji.Security.Exceptions;

public class KejiSecurityException : Exception
{
    public KejiSecurityException(string message) : base(message) { }
    public KejiSecurityException(string message, Exception inner) : base(message, inner) { }
}
