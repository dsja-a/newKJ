namespace Keji.Configuration.Models;

public class KejiConfigurationLoadOptions
{
    public string ProjectRoot { get; set; } = AppDomain.CurrentDomain.BaseDirectory;
    public string ConfigFileName { get; set; } = "config.yaml";
    public string DotEnvFileName { get; set; } = ".env";
    public bool RequireConfigFile { get; set; } = true;
    public bool FailOnMissingEnvironmentVariable { get; set; } = true;
    public long MaxConfigFileBytes { get; set; } = 1 * 1024 * 1024;
    public long MaxDotEnvFileBytes { get; set; } = 1 * 1024 * 1024;
    public int MaxDepth { get; set; } = 32;
    public int MaxNodeCount { get; set; } = 10000;
}
