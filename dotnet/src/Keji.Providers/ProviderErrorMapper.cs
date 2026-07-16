using System.Net;
using System.Text.Json;

namespace Keji.Providers;

public static class ProviderErrorMapper
{
    private const int MaxErrorBodyBytes = 64 * 1024;

    public static (KejiProviderErrorCode Code, string Message) Map(HttpStatusCode statusCode, string? responseBody)
    {
        if (statusCode == HttpStatusCode.TooManyRequests)
            return MapRateLimit(responseBody);

        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => (KejiProviderErrorCode.Timeout, "Provider request timed out"),
            HttpStatusCode.Unauthorized => (KejiProviderErrorCode.AuthFailed, "Provider authentication failed"),
            HttpStatusCode.Forbidden => (KejiProviderErrorCode.AuthFailed, "Provider access denied"),
            HttpStatusCode.NotFound => (KejiProviderErrorCode.EndpointNotFound, "Provider endpoint not found"),
            HttpStatusCode.RequestEntityTooLarge => (KejiProviderErrorCode.RequestTooLarge, "Request exceeds provider limits"),
            HttpStatusCode.ServiceUnavailable => (KejiProviderErrorCode.ServiceUnavailable, "Provider service unavailable"),
            HttpStatusCode.BadGateway => (KejiProviderErrorCode.GatewayError, "Provider gateway error"),
            HttpStatusCode.GatewayTimeout => (KejiProviderErrorCode.GatewayTimeout, "Provider gateway timeout"),
            _ when (int)statusCode >= 500 => (KejiProviderErrorCode.ServerError, "Provider server error"),
            _ when (int)statusCode >= 400 => (KejiProviderErrorCode.RequestError, "Provider rejected request"),
            _ => (KejiProviderErrorCode.ProviderError, "Unknown provider error")
        };
    }

    public static (KejiProviderErrorCode Code, string Message) MapFromException(Exception ex)
    {
        return ex switch
        {
            TaskCanceledException => (KejiProviderErrorCode.Timeout, "Provider request timed out"),
            OperationCanceledException => (KejiProviderErrorCode.Cancelled, "Request was cancelled"),
            HttpRequestException httpEx when httpEx.StatusCode.HasValue => Map(httpEx.StatusCode.Value, null),
            _ => (KejiProviderErrorCode.ConnectionError, "Failed to connect to provider")
        };
    }

    public static (KejiProviderErrorCode Code, string Message) Sanitize(KejiProviderErrorCode errorCode) =>
        errorCode switch
        {
            KejiProviderErrorCode.AuthFailed => (KejiProviderErrorCode.AuthFailed, "Provider authentication failed"),
            KejiProviderErrorCode.EndpointNotFound => (KejiProviderErrorCode.EndpointNotFound, "Provider endpoint not found"),
            KejiProviderErrorCode.RequestTooLarge => (KejiProviderErrorCode.RequestTooLarge, "Request exceeds provider limits"),
            KejiProviderErrorCode.RateLimited => (KejiProviderErrorCode.RateLimited, "Provider rate limit exceeded"),
            KejiProviderErrorCode.ServiceUnavailable => (KejiProviderErrorCode.ServiceUnavailable, "Provider service unavailable"),
            KejiProviderErrorCode.GatewayError => (KejiProviderErrorCode.GatewayError, "Provider gateway error"),
            KejiProviderErrorCode.GatewayTimeout => (KejiProviderErrorCode.GatewayTimeout, "Provider gateway timeout"),
            KejiProviderErrorCode.ServerError => (KejiProviderErrorCode.ServerError, "Provider server error"),
            KejiProviderErrorCode.RequestError => (KejiProviderErrorCode.RequestError, "Provider rejected request"),
            KejiProviderErrorCode.InvalidRequest => (KejiProviderErrorCode.InvalidRequest, "Model request is invalid"),
            KejiProviderErrorCode.InvalidResponse => (KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response"),
            KejiProviderErrorCode.ResponseTooLarge => (KejiProviderErrorCode.ResponseTooLarge, "Provider response exceeded the size limit"),
            KejiProviderErrorCode.InvalidContentType => (KejiProviderErrorCode.InvalidContentType, "Provider returned an invalid stream content type"),
            KejiProviderErrorCode.StreamProtocolError => (KejiProviderErrorCode.StreamProtocolError, "Provider stream violated the protocol"),
            KejiProviderErrorCode.StreamTruncated => (KejiProviderErrorCode.StreamTruncated, "Provider stream ended unexpectedly"),
            KejiProviderErrorCode.StreamInterrupted => (KejiProviderErrorCode.StreamInterrupted, "Provider stream was interrupted"),
            KejiProviderErrorCode.Timeout => (KejiProviderErrorCode.Timeout, "Provider request timed out"),
            KejiProviderErrorCode.Cancelled => (KejiProviderErrorCode.Cancelled, "Request was cancelled"),
            KejiProviderErrorCode.ConnectionError => (KejiProviderErrorCode.ConnectionError, "Failed to connect to provider"),
            KejiProviderErrorCode.QuotaExceeded => (KejiProviderErrorCode.QuotaExceeded, "Provider quota exceeded"),
            _ => (KejiProviderErrorCode.ProviderError, "Model provider request failed"),
        };

    public static string? ReadErrorBodySafely(string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
            return null;

        if (responseBody.Length > MaxErrorBodyBytes)
            responseBody = responseBody[..MaxErrorBodyBytes];

        if (responseBody.Any(c => c is '\0'))
            return null;

        return responseBody;
    }

    private static (KejiProviderErrorCode Code, string Message) MapRateLimit(string? responseBody)
    {
        var safeBody = ReadErrorBodySafely(responseBody);
        if (safeBody is null)
            return (KejiProviderErrorCode.RateLimited, "Provider rate limit exceeded");

        try
        {
            using var doc = JsonDocument.Parse(safeBody, new JsonDocumentOptions { MaxDepth = 8 });
            var root = doc.RootElement;

            var errorType = FindErrorField(root, "type");
            var errorCode = FindErrorField(root, "code");

            var combined = (errorType ?? errorCode) ?? string.Empty;

            if (combined is "insufficient_quota" or "quota_exceeded" or "quota_exhausted" or
                "billing_hard_limit_reached" or "insufficient_balance" or "payment_required")
            {
                return (KejiProviderErrorCode.QuotaExceeded, "Provider quota exceeded");
            }

            return (KejiProviderErrorCode.RateLimited, "Provider rate limit exceeded");
        }
        catch (JsonException)
        {
            return (KejiProviderErrorCode.RateLimited, "Provider rate limit exceeded");
        }
    }

    private static string? FindErrorField(JsonElement root, string fieldName)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("error", out var errorObj) &&
            errorObj.ValueKind == JsonValueKind.Object &&
            errorObj.TryGetProperty(fieldName, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(fieldName, out var topValue) &&
            topValue.ValueKind == JsonValueKind.String)
        {
            return topValue.GetString();
        }

        return null;
    }
}
