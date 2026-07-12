using Microsoft.Data.Sqlite;

namespace Keji.Persistence;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
