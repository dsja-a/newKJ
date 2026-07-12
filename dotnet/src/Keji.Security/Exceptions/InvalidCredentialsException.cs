namespace Keji.Security.Exceptions;

public class InvalidCredentialsException : KejiSecurityException
{
    public InvalidCredentialsException() : base("用户名或密码错误") { }
}
