using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Keji.Api.Takeover;
using Keji.Integration.Tests.Authorization;
using Keji.Security.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Integration.Tests;

public sealed class ApiTakeoverIntegrationTests : IClassFixture<AuthorizationIntegrationFixture>
{
    private readonly AuthorizationIntegrationFixture _fixture;
    public ApiTakeoverIntegrationTests(AuthorizationIntegrationFixture fixture)=>_fixture=fixture;

    [Fact]
    public void CompatibilityCatalogOwnsExactlyEightySevenUniqueMethodPaths()
    {
        Assert.Equal(87,KejiApiTakeoverCatalog.Routes.Count);
        Assert.Equal(87,KejiApiTakeoverCatalog.Routes
            .Select(static route=>route.Method+" "+route.Pattern)
            .Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryCompatibilityRouteIsRegisteredByAspNetCore()
    {
        var endpoints=_fixture.DefaultFactory.Services.GetServices<EndpointDataSource>()
            .SelectMany(static source=>source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var missing=KejiApiTakeoverCatalog.Routes.Where(route=>!endpoints.Any(endpoint=>
            string.Equals("/"+endpoint.RoutePattern.RawText?.TrimStart('/'),route.Pattern,StringComparison.Ordinal)&&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains(route.Method)==true))
            .Select(static route=>route.Method+" "+route.Pattern).ToArray();
        Assert.True(missing.Length==0,string.Join(", ",missing));
    }

    [Fact]
    public void EveryProtectedTakeoverRouteHasExplicitPermission()
    {
        Assert.All(KejiApiTakeoverCatalog.Routes.Where(static route=>!route.IsPublic),
            static route=>Assert.True(KejiPermissionCatalog.IsDefined(route.Permission)));
    }

    [Fact]
    public async Task DeferredRouteRejectsAnonymousBeforeReturningUnavailable()
    {
        var response=await _fixture.DefaultClient.GetAsync("/api/knowledge/documents");
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Fact]
    public async Task AuthorizedDeferredRouteReturnsFixedSafe503()
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/knowledge/documents");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_fixture.AdminToken);
        var response=await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.Contains("\"code\":\"CAPABILITY_DEFERRED\"",body,StringComparison.Ordinal);
        Assert.DoesNotContain("python",body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkConfigurationIsNotAnonymous()
    {
        var response=await _fixture.DefaultClient.PostAsJsonAsync("/api/work/configure",new{});
        Assert.Equal(HttpStatusCode.Unauthorized,response.StatusCode);
    }

    [Fact]
    public async Task CallbackIsOwnedPubliclyButDeferredUntilWorkerTask()
    {
        var response=await _fixture.DefaultClient.GetAsync("/api/work/callback");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
    }

    [Fact]
    public async Task CorrelationIdIsGeneratedAndSecurityHeadersArePresent()
    {
        var response=await _fixture.DefaultClient.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Correlation-ID",out var values));
        Assert.Matches("^[0-9a-f]{32}$",Assert.Single(values));
        Assert.Equal("nosniff",Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("DENY",Assert.Single(response.Headers.GetValues("X-Frame-Options")));
        Assert.Equal("no-store",Assert.Single(response.Headers.GetValues("Cache-Control")));
    }

    [Fact]
    public async Task UnsafeCorrelationIdIsNeverReflected()
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID","unsafe\r\nvalue");
        var response=await _fixture.DefaultClient.SendAsync(request);
        var value=Assert.Single(response.Headers.GetValues("X-Correlation-ID"));
        Assert.Matches("^[0-9a-f]{32}$",value);
    }

    [Fact]
    public async Task OversizedBodyIsRejectedBeforeController()
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/auth/login")
        {
            Content=new ByteArrayContent(new byte[Keji.Api.Middleware.KejiApiBoundaryMiddleware.MaximumRequestBodyBytes+1])
        };
        request.Content.Headers.ContentType=new("application/json");
        var response=await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge,response.StatusCode);
    }

    [Fact]
    public async Task SecurityStatusIsRealAspNetCoreCapability()
    {
        var response=await _fixture.DefaultClient.GetAsync("/api/security/status");
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.Contains("\"enabled\":true",body,StringComparison.Ordinal);
        Assert.DoesNotContain("secret",body,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConversationListUsesAcceptedCSharpPersistenceService()
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,"/api/conversations");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_fixture.MemberToken);
        var response=await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("[]",await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SmartQueryEndpointRunsAcceptedCSharpValidationChain()
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/smart-query");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_fixture.MemberToken);
        request.Content=JsonContent.Create(new
        {
            runId="invalid",dataSourceId="source",question="count rows",requestedLimit=100
        });
        var response=await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var body=await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":2",body,StringComparison.Ordinal);
        Assert.Contains("\"safeCode\":\"SMART_QUERY_INVALID_REQUEST\"",body,StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatStreamRunsAcceptedAgentAndTask012SseFormatter()
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/chat/stream");
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",_fixture.MemberToken);
        request.Content=JsonContent.Create(new
        {
            runId="invalid",conversationId="conversation",message="hello"
        });
        var response=await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        Assert.Equal("text/event-stream",response.Content.Headers.ContentType?.MediaType);
        var body=await response.Content.ReadAsStringAsync();
        Assert.Contains("event: error",body,StringComparison.Ordinal);
        Assert.Contains("event: done",body,StringComparison.Ordinal);
        Assert.Contains("\"protocol_version\":1",body,StringComparison.Ordinal);
        Assert.DoesNotContain("hello",body,StringComparison.Ordinal);
    }
}
