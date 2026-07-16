using System.Net;

namespace Keji.Providers;

public static class ProviderErrorMapper
{
    public static (string Code, string Message) Map(HttpStatusCode statusCode, string? responseBody)
    {
        _ = responseBody;
        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => ("TIMEOUT", "Provider request timed out"),
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

    public static (string Code, string Message) Sanitize(string? errorCode)
    {
        if (errorCode is null || errorCode.Length is 0 or > 64 || errorCode.Any(char.IsControl))
            return ("PROVIDER_ERROR", "Model provider request failed");

        var normalizedCode = errorCode?.Trim().ToUpperInvariant();
        return normalizedCode switch
        {
            "AUTH_FAILED" => ("AUTH_FAILED", "Provider authentication failed"),
            "ENDPOINT_NOT_FOUND" => ("ENDPOINT_NOT_FOUND", "Provider endpoint not found"),
            "REQUEST_TOO_LARGE" => ("REQUEST_TOO_LARGE", "Request exceeds provider limits"),
            "RATE_LIMITED" => ("RATE_LIMITED", "Provider rate limit exceeded"),
            "SERVICE_UNAVAILABLE" => ("SERVICE_UNAVAILABLE", "Provider service unavailable"),
            "GATEWAY_ERROR" => ("GATEWAY_ERROR", "Provider gateway error"),
            "GATEWAY_TIMEOUT" => ("GATEWAY_TIMEOUT", "Provider gateway timeout"),
            "SERVER_ERROR" => ("SERVER_ERROR", "Provider server error"),
            "REQUEST_ERROR" => ("REQUEST_ERROR", "Provider rejected request"),
            "INVALID_REQUEST" => ("INVALID_REQUEST", "Model request is invalid"),
            "INVALID_RESPONSE" => ("INVALID_RESPONSE", "Provider returned an invalid response"),
            "RESPONSE_TOO_LARGE" => ("RESPONSE_TOO_LARGE", "Provider response exceeded the size limit"),
            "INVALID_CONTENT_TYPE" => ("INVALID_CONTENT_TYPE", "Provider returned an invalid stream content type"),
            "STREAM_PROTOCOL_ERROR" => ("STREAM_PROTOCOL_ERROR", "Provider stream violated the protocol"),
            "STREAM_TRUNCATED" => ("STREAM_TRUNCATED", "Provider stream ended unexpectedly"),
            "STREAM_INTERRUPTED" => ("STREAM_INTERRUPTED", "Provider stream was interrupted"),
            "TIMEOUT" => ("TIMEOUT", "Provider request timed out"),
            "CANCELLED" => ("CANCELLED", "Request was cancelled"),
            "CONNECTION_ERROR" => ("CONNECTION_ERROR", "Failed to connect to provider"),
            _ => ("PROVIDER_ERROR", "Model provider request failed"),
        };
    }
}
