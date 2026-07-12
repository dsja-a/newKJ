using Keji.Configuration.Models;

namespace Keji.Persistence;

public class KejiPersistenceOptions
{
    public string ProjectRoot { get; set; } = AppContext.BaseDirectory;
    public string DatabasePath { get; set; } = "data/keji.db";
    public int BusyTimeoutMilliseconds { get; set; } = 5000;
    public bool EnableWal { get; set; } = true;
    public bool EnableForeignKeys { get; set; } = true;
    public bool CreateDirectoryIfMissing { get; set; } = true;

    public static KejiPersistenceOptions FromConfiguration(
        KejiConfigurationDocument document,
        string projectRoot)
    {
        var options = new KejiPersistenceOptions
        {
            ProjectRoot = projectRoot,
        };

        var dbPath = document.GetOptionalString("database.path");
        if (!string.IsNullOrEmpty(dbPath))
            options.DatabasePath = dbPath;

        return options;
    }
}
