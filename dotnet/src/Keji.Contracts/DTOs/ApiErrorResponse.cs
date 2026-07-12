using System.Text.Json.Serialization;

namespace Keji.Contracts.DTOs;

public class ApiErrorResponse
{
    [JsonPropertyName("detail")]
    public string Detail { get; set; } = string.Empty;
}
