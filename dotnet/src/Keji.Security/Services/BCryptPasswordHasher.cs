using Keji.Security.Options;

namespace Keji.Security.Services;

public class BCryptPasswordHasher : IPasswordHasher
{
    private readonly PasswordHashOptions _options;

    public BCryptPasswordHasher(PasswordHashOptions options)
    {
        _options = options;
    }

    public string Hash(string password)
    {
        if (password is null)
            throw new ArgumentNullException(nameof(password));
        if (password.Length == 0)
            throw new ArgumentException("Password must not be empty.", nameof(password));

        return BCrypt.Net.BCrypt.HashPassword(password, _options.WorkFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (password is null || passwordHash is null)
            return false;
        if (password.Length == 0 || passwordHash.Length == 0)
            return false;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }

    public bool NeedsRehash(string passwordHash)
    {
        if (string.IsNullOrEmpty(passwordHash))
            return false;

        try
        {
            return BCrypt.Net.BCrypt.PasswordNeedsRehash(passwordHash, _options.WorkFactor);
        }
        catch
        {
            return false;
        }
    }
}
