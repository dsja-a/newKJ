using System.Text.Json.Serialization;

namespace Keji.Contracts.DTOs;

public class PublicUserResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; } = true;

    [JsonPropertyName("created_at")]
    public double? CreatedAt { get; set; }

    [JsonPropertyName("last_login_at")]
    public double? LastLoginAt { get; set; }
}
