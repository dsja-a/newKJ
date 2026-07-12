using System.Text.Json.Serialization;

namespace Keji.Contracts.DTOs;

public class AuthMeResponse
{
    [JsonPropertyName("user")]
    public PublicUserResponse? User { get; set; }
}
