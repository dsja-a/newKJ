using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Microsoft.AspNetCore.Routing;

namespace Keji.Api.Middleware;

public sealed class KejiApiAuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<KejiApiAuditMiddleware> _logger;
    public KejiApiAuditMiddleware(RequestDelegate next,ILogger<KejiApiAuditMiddleware> logger)
    {
        _next=next;_logger=logger;
    }

    public async Task InvokeAsync(HttpContext context,IKejiAuditService audit)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        finally
        {
            var route=(context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            if(route is not null&&route.Length<=256)
                await WriteSafeAsync(audit,context.Request.Method,route,context.Response.StatusCode,
                    context.TraceIdentifier).ConfigureAwait(false);
        }
    }

    private async Task WriteSafeAsync(
        IKejiAuditService audit,string method,string route,int status,string correlationId)
    {
        try
        {
            var result=await audit.WriteAsync(KejiAuditCategory.DataAccess,"api_request",
                status is >=200 and <400?KejiAuditOutcome.Success:KejiAuditOutcome.Failure,
                status>=500?KejiAuditSeverity.Warning:KejiAuditSeverity.Information,
                "http_route",metadata:new Dictionary<string,string>(StringComparer.Ordinal)
                {
                    ["Method"]=method,["Route"]=route,["StatusCode"]=status.ToString(),
                    ["CorrelationId"]=correlationId
                },cancellationToken:CancellationToken.None).ConfigureAwait(false);
            if(result is KejiAuditResult.SinkError or KejiAuditResult.PartialFailure)
                _logger.LogWarning("ApiAuditFailure code=AUDIT_SINK_FAILURE");
        }
        catch(Exception exception)when(exception is not OperationCanceledException)
        {
            _logger.LogWarning("ApiAuditFailure code=AUDIT_UNEXPECTED_FAILURE");
        }
    }
}
