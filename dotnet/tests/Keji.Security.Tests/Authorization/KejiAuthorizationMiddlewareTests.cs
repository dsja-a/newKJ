using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Security.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keji.Security.Tests.Authorization;

public sealed class KejiAuthorizationMiddlewareTests
{
    [Fact]
    public async Task StartedResponse_DenialDoesNotRewriteOrContinue()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(),
            "started-response"));
        await context.Response.StartAsync();

        var nextCalled = false;
        var middleware = new KejiAuthorizationMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(
            context,
            new KejiSecurityOptions { Enabled = true },
            new NullCurrentUserAccessor(),
            new KejiAuthorizationService(new KejiRolePermissionMatrix()),
            NullLogger<KejiAuthorizationMiddleware>.Instance);

        Assert.True(context.Response.HasStarted);
        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    private sealed class NullCurrentUserAccessor : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => null;
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
