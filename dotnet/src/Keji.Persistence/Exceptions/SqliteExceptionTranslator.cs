using Microsoft.Data.Sqlite;

namespace Keji.Persistence;

public static class SqliteExceptionTranslator
{
    public static KejiPersistenceException Create(SqliteException exception, string operationName, string? safeEntityId = null)
    {
        var idPart = safeEntityId is not null ? $" (id: {safeEntityId})" : "";
        var message = $"Database operation '{operationName}' failed{idPart}. Error code: {exception.SqliteErrorCode}.";
        return new KejiPersistenceException(message, exception.SqliteErrorCode);
    }

    public static void ThrowTranslated(SqliteException ex, string operationName, string? safeEntityId = null)
    {
        throw Create(ex, operationName, safeEntityId);
    }

    public static async Task SafeRollbackAsync(Microsoft.Data.Sqlite.SqliteTransaction? transaction)
    {
        if (transaction is null) return;
        try
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    public static string NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            throw new KejiPersistenceException("Role must not be null, empty, or whitespace.");

        var normalized = role.Trim().ToLowerInvariant();

        if (normalized != "admin" && normalized != "member" && normalized != "readonly")
            throw new KejiPersistenceException($"Invalid role: '{role}'. Must be one of: admin, member, readonly.");

        return normalized;
    }
}
