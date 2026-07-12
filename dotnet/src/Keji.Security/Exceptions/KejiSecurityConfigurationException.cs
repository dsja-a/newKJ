namespace Keji.Security.Exceptions;

public class KejiSecurityConfigurationException : KejiSecurityException
{
    public KejiSecurityConfigurationException(string message) : base(message) { }
    public KejiSecurityConfigurationException(string message, Exception inner) : base(message, inner) { }
}
