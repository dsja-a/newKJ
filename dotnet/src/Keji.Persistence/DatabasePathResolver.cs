namespace Keji.Persistence;

public static class DatabasePathResolver
{
    public static string Resolve(KejiPersistenceOptions options)
    {
        var dbPath = options.DatabasePath;

        if (string.IsNullOrEmpty(dbPath))
            throw new KejiPersistenceException("DatabasePath must not be empty.");

        if (dbPath.Contains('\0'))
            throw new KejiPersistenceException("DatabasePath must not contain null characters.");

        if (Path.IsPathRooted(dbPath))
            return Path.GetFullPath(dbPath);

        return Path.GetFullPath(Path.Combine(options.ProjectRoot, dbPath));
    }
}
