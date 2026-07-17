using Keji.Security.Authorization;

namespace Keji.Api.Takeover;

public static class KejiApiTakeoverEndpoints
{
    private static readonly HashSet<string> Implemented = new(StringComparer.Ordinal)
    {
        Key("GET","/"), Key("GET","/health"), Key("GET","/favicon.ico"),
        Key("POST","/chat"),Key("POST","/chat/stream"),
        Key("POST","/api/auth/login"), Key("GET","/api/auth/me"),
        Key("GET","/api/security/status"),
        Key("GET","/api/conversations"),Key("GET","/api/conversations/{conv_id}"),
        Key("DELETE","/api/conversations/{conv_id}"),Key("POST","/api/smart-query"),
        Key("POST","/api/smart-query/with-steps"),Key("POST","/api/smart-query/stream")
    };

    public static IEndpointRouteBuilder MapKejiApiTakeover(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", static () => Results.Json(new
            { service = "Keji.Api", publicApi = "aspnet-core", status = "ok" }))
            .WithMetadata(new KejiAllowAnonymousAttribute());
        endpoints.MapGet("/health", static () => Results.Json(new { status = "ok" }))
            .WithMetadata(new KejiAllowAnonymousAttribute());
        endpoints.MapGet("/favicon.ico", static () => Results.NoContent())
            .WithMetadata(new KejiAllowAnonymousAttribute());

        foreach(var route in KejiApiTakeoverCatalog.Routes)
        {
            if(Implemented.Contains(Key(route.Method,route.Pattern)))continue;
            var builder=endpoints.MapMethods(route.Pattern,[route.Method],DeferredAsync);
            if(route.IsPublic)builder.WithMetadata(new KejiAllowAnonymousAttribute());
            else builder.WithMetadata(new KejiRequirePermissionAttribute(route.Permission));
        }
        return endpoints;
    }

    private static Task DeferredAsync(HttpContext context)
    {
        context.Response.StatusCode=StatusCodes.Status503ServiceUnavailable;
        return context.Response.WriteAsJsonAsync(new
        {
            type="about:blank",title="Capability temporarily unavailable",status=503,
            code="CAPABILITY_DEFERRED",correlationId=context.TraceIdentifier
        },context.RequestAborted);
    }

    private static string Key(string method,string pattern)=>method+" "+pattern;
}
