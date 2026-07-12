using System.Text.Json.Serialization;

namespace Keji.Contracts.DTOs;

public class LoginResponse
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("user")]
    public PublicUserResponse? User { get; set; }
}
