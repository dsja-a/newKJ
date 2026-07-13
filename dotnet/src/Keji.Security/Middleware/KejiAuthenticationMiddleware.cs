using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Security.Auth;
using Keji.Security.Authentication;
using Keji.Security.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;

namespace Keji.Security.Middleware;

public class KejiAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly string[] DefaultPublicPaths =
    {
        "/", "/health", "/favicon.ico", "/static", "/api/security/status", "/api/auth/login", "/api/work",
    };

    public KejiAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, KejiSecurityOptions options, IRequestAuthenticator authenticator)
    {
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        var hasAllowAnonymous = endpoint?.Metadata.GetMetadata<IAllowAnonymous>() != null;

        if (hasAllowAnonymous)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        if (IsPublicPath(path, options))
        {
            await _next(context);
            return;
        }

        try
        {
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            var xApiKey = context.Request.Headers["X-API-Key"].FirstOrDefault();
            var queryApiKey = context.Request.Query["api_key"].FirstOrDefault();
            var remoteIp = context.Connection.RemoteIpAddress?.ToString();

            var result = await authenticator.AuthenticateAsync(authHeader, xApiKey, queryApiKey, remoteIp, context.RequestAborted);

            if (result.IsAuthenticated && result.User != null)
            {
                var claims = new List<Claim>
                {
                    new(ClaimTypes.NameIdentifier, result.User.Id),
                    new(ClaimTypes.Name, result.User.Username),
                    new(ClaimTypes.Role, result.User.Role),
                    new(KejiClaimTypes.DisplayName, result.User.DisplayName),
                    new(KejiClaimTypes.AuthenticationKind, result.User.AuthenticationKind.ToString()),
                };

                var identity = new ClaimsIdentity(claims, "KejiAuth");
                context.User = new ClaimsPrincipal(identity);

                await _next(context);
                return;
            }

            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new
            {
                detail = "未授权：请登录（/api/auth/login）或使用有效 API Key"
            }, JsonOptions);
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Keji.Persistence.KejiPersistenceException)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { detail = "服务器内部错误" }, JsonOptions);
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
        catch (Keji.Security.Exceptions.KejiSecurityException)
        {
            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new { detail = "服务器内部错误" }, JsonOptions);
            await context.Response.WriteAsync(body, context.RequestAborted);
        }
    }

    private static bool IsPublicPath(string path, KejiSecurityOptions options)
    {
        var allPublic = new List<string>(DefaultPublicPaths);
        allPublic.AddRange(options.PublicPaths);

        foreach (var p in allPublic)
        {
            if (p == "/")
            {
                if (path == "/")
                    return true;
                continue;
            }

            if (path.Equals(p, StringComparison.Ordinal))
                return true;

            var prefix = p.TrimEnd('/') + "/";
            if (path.StartsWith(prefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
