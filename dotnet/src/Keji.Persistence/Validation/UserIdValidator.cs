using System.Text.RegularExpressions;

namespace Keji.Persistence.Validation;

public static partial class UserIdValidator
{
    [GeneratedRegex("^[0-9a-f]{16}$")]
    private static partial Regex ValidUserIdRegex();

    public static string RequireValid(string? userId)
    {
        if (userId is null)
            throw new KejiPersistenceException("User ID must not be null.");

        if (userId.Length == 0)
            throw new KejiPersistenceException("User ID must not be empty.");

        if (userId.Any(c => char.IsControl(c)))
            throw new KejiPersistenceException("User ID must not contain control characters.");

        if (!ValidUserIdRegex().IsMatch(userId))
            throw new KejiPersistenceException("User ID must be exactly 16 lowercase hexadecimal characters (0-9, a-f).");

        return userId;
    }
}
