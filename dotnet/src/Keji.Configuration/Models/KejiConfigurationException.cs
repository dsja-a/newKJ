namespace Keji.Configuration.Models;

public class KejiConfigurationException : Exception
{
    public string? ConfigPath { get; }
    public string? FilePath { get; }
    public int? LineNumber { get; }
    public int? ColumnNumber { get; }

    public KejiConfigurationException(string message)
        : base(message)
    {
    }

    public KejiConfigurationException(string message, string? configPath)
        : base(message)
    {
        ConfigPath = configPath;
    }

    public KejiConfigurationException(string message, string? configPath, string? filePath, int? lineNumber, int? columnNumber)
        : base(message)
    {
        ConfigPath = configPath;
        FilePath = filePath;
        LineNumber = lineNumber;
        ColumnNumber = columnNumber;
    }

    public KejiConfigurationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
