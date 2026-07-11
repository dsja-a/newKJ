namespace Keji.Configuration.Models;

public class KejiConfigurationException : Exception
{
    public string? ConfigPath { get; }

    public KejiConfigurationException(string message)
        : base(message)
    {
    }

    public KejiConfigurationException(string message, string? configPath)
        : base(message)
    {
        ConfigPath = configPath;
    }

    public KejiConfigurationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
