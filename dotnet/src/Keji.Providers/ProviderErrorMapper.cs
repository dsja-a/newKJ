using System.Net;

namespace Keji.Providers;

public static class ProviderErrorMapper
{
    public static (string Code, string Message) Map(HttpStatusCode statusCode, string? responseBody)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => ("AUTH_FAILED", "Provider authentication failed"),
            HttpStatusCode.Forbidden => ("AUTH_FAILED", "Provider access denied"),
            HttpStatusCode.NotFound => ("ENDPOINT_NOT_FOUND", "Provider endpoint not found"),
            HttpStatusCode.RequestEntityTooLarge => ("REQUEST_TOO_LARGE", "Request exceeds provider limits"),
            HttpStatusCode.TooManyRequests => ("RATE_LIMITED", "Provider rate limit exceeded"),
            HttpStatusCode.ServiceUnavailable => ("SERVICE_UNAVAILABLE", "Provider service unavailable"),
            HttpStatusCode.BadGateway => ("GATEWAY_ERROR", "Provider gateway error"),
            HttpStatusCode.GatewayTimeout => ("GATEWAY_TIMEOUT", "Provider gateway timeout"),
            _ when (int)statusCode >= 500 => ("SERVER_ERROR", "Provider server error"),
            _ when (int)statusCode >= 400 => ("REQUEST_ERROR", "Provider rejected request"),
            _ => ("UNKNOWN", "Unknown provider error")
        };
    }

    public static (string Code, string Message) MapFromException(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => ("TIMEOUT", "Provider request timed out"),
            OperationCanceledException => ("CANCELLED", "Request was cancelled"),
            HttpRequestException httpEx when httpEx.StatusCode.HasValue => Map(httpEx.StatusCode.Value, null),
            _ => ("CONNECTION_ERROR", "Failed to connect to provider")
        };
    }
}
