namespace Keji.Configuration.Models;

public readonly struct ApiKeySettingsMask
{
    public bool IsConfigured { get; }
    public string DisplayValue { get; }

    public ApiKeySettingsMask(bool isConfigured, string displayValue)
    {
        IsConfigured = isConfigured;
        DisplayValue = displayValue;
    }
}
