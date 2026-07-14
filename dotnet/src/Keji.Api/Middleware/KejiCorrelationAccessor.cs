using Keji.Auditing.Abstractions;

namespace Keji.Api.Middleware;

public sealed class KejiCorrelationAccessor : IKejiAuditCorrelationAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public KejiCorrelationAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? CorrelationId
    {
        get
        {
            var ctx = _httpContextAccessor.HttpContext;
            if (ctx is null) return null;

            var traceId = ctx.TraceIdentifier;
            if (string.IsNullOrEmpty(traceId)) return null;

            var cleaned = SanitizeCorrelationId(traceId);
            if (cleaned.Length > 128) cleaned = cleaned[..128];
            if (cleaned.Length == 0) return null;

            return cleaned;
        }
    }

    private static string SanitizeCorrelationId(string value)
    {
        var span = value.AsSpan();
        var cleaned = new char[span.Length];
        var written = 0;
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (char.IsControl(c)) continue;
            cleaned[written++] = c;
        }
        return new string(cleaned, 0, written);
    }
}
