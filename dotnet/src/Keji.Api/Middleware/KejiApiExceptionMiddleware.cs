using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Persistence;
using Keji.Security.Exceptions;

namespace Keji.Api.Middleware;

public class KejiApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public KejiApiExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KejiPersistenceException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { detail = "服务器内部错误" }, JsonOptions));
            }
            else
            {
                throw;
            }
        }
        catch (KejiSecurityException)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { detail = "服务器内部错误" }, JsonOptions));
            }
            else
            {
                throw;
            }
        }
        catch (Exception)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(
                    new { detail = "服务器内部错误" }, JsonOptions));
            }
            else
            {
                throw;
            }
        }
    }
}
