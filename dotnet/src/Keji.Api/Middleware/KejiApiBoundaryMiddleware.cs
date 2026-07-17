using System.Text.Json;

namespace Keji.Api.Middleware;

public sealed class KejiApiBoundaryMiddleware
{
    public const long MaximumRequestBodyBytes = 2 * 1024 * 1024;
    private const string CorrelationHeader = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public KejiApiBoundaryMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[CorrelationHeader].ToString();
        var correlationId = IsSafeId(supplied) ? supplied : Guid.NewGuid().ToString("N");
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
        context.Response.Headers["Cache-Control"] = "no-store";

        if (context.Request.ContentLength is > MaximumRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/problem+json";
            await JsonSerializer.SerializeAsync(context.Response.Body,
                new { type = "about:blank", title = "Request body too large", status = 413, correlationId },
                cancellationToken: context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private static bool IsSafeId(string value) => value.Length == 32 &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
