using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Security.Auth;
using Keji.Security.Exceptions;
using Keji.Security.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Keji.Security.Authorization;

public sealed class KejiAuthorizationMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public KejiAuthorizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        KejiSecurityOptions options,
        ICurrentUserAccessor userAccessor,
        IKejiAuthorizationService authService,
        ILogger<KejiAuthorizationMiddleware> logger,
        IKejiAuditBridge? auditBridge = null)
    {
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        var endpoint = context.GetEndpoint();
        if (endpoint == null)
        {
            await _next(context);
            return;
        }

        var hasAllowAnonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() != null;
        var requiredPermissions = endpoint.Metadata
            .GetOrderedMetadata<KejiRequirePermissionAttribute>();

        if (hasAllowAnonymous && requiredPermissions.Count > 0)
        {
            logger.LogError("Conflicting authorization metadata was detected.");
            throw new KejiSecurityConfigurationException(
                "An endpoint cannot be both anonymous and permission protected.");
        }

        if (hasAllowAnonymous)
        {
            await _next(context);
            return;
        }

        if (requiredPermissions.Count == 0)
        {
            await AuditAuthorizationAsync(auditBridge, "denied", "missing_permission_metadata", logger, context.RequestAborted);
            await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "权限不足");
            return;
        }

        var user = userAccessor.CurrentUser;
        if (user == null)
        {
            await AuditAuthorizationAsync(auditBridge, "denied", "unauthenticated", logger, context.RequestAborted);
            await WriteErrorAsync(context, StatusCodes.Status401Unauthorized, "未登录，请先登录");
            return;
        }

        var permissions = requiredPermissions.Select(a => a.Permission).ToList();
        var result = authService.AuthorizeAll(user, permissions);

        if (result.IsAllowed)
        {
            await AuditAuthorizationAsync(auditBridge, "allowed", "allowed", logger, context.RequestAborted);
            await _next(context);
            return;
        }

        await AuditAuthorizationAsync(auditBridge, "denied", result.FailureReason.ToString(), logger, context.RequestAborted);

        var detail = result.FailureReason switch
        {
            KejiAuthorizationFailureReason.Unauthenticated => "未登录，请先登录",
            KejiAuthorizationFailureReason.AdminRequired => "需要管理员权限",
            KejiAuthorizationFailureReason.ReadonlyWriteDenied => "当前账号无写入权限",
            _ => "权限不足",
        };

        var statusCode = result.FailureReason == KejiAuthorizationFailureReason.Unauthenticated
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status403Forbidden;

        await WriteErrorAsync(context, statusCode, detail);
    }

    private static async Task AuditAuthorizationAsync(IKejiAuditBridge? bridge, string outcome, string reason, ILogger<KejiAuthorizationMiddleware> logger, CancellationToken ct)
    {
        if (bridge is null) return;
        try
        {
            await bridge.AuditAuthorizationAsync(outcome, reason, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
    }

    private static async Task WriteErrorAsync(HttpContext context, int statusCode, string detail)
    {
        if (context.Response.HasStarted)
            return;

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var responseBody = JsonSerializer.Serialize(new { detail }, JsonOptions);
        await context.Response.WriteAsync(responseBody, context.RequestAborted);
    }
}
