using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Keji.Security.Authorization;

namespace Keji.Integration.Tests.Authorization;

public sealed class AuthorizationMiddlewareIntegrationTests
    : IClassFixture<AuthorizationIntegrationFixture>
{
    private readonly AuthorizationIntegrationFixture _fixture;

    public AuthorizationMiddlewareIntegrationTests(AuthorizationIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Custom_Anonymous_Metadata_Bypasses_Authentication_And_Authorization()
    {
        var response = await _fixture.DefaultClient.GetAsync("/probe/anonymous");

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"anonymous\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Standard_Anonymous_Metadata_Bypasses_Authentication_And_Authorization()
    {
        var response = await _fixture.DefaultClient.GetAsync("/probe/standard-anonymous");

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"standard-anonymous\"}", requireExactContentType: false);
    }

    [Theory]
    [InlineData("admin", "/probe/file-write", "{\"result\":\"file-write\"}")]
    [InlineData("member", "/probe/file-write", "{\"result\":\"file-write\"}")]
    [InlineData("readonly", "/probe/file-read", "{\"result\":\"file-read\"}")]
    public async Task Jwt_Roles_Can_Use_Their_Granted_Permissions(
        string role,
        string path,
        string expectedBody)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(path, TokenFor(role));
        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, expectedBody, requireExactContentType: false);
    }

    [Fact]
    public async Task Api_Key_Uses_Admin_Authorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe/admin-users");
        request.Headers.Add("X-API-Key", AuthorizationIntegrationFixture.ApiKey);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"admin-users\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Api_Key_AuthMe_Uses_Admin_Authorization()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-API-Key", AuthorizationIntegrationFixture.ApiKey);

        var response = await _fixture.DefaultClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        await AssertJsonAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"账号已禁用或不存在\"}", requireExactContentType: false);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("readonly")]
    public async Task AuthMe_Is_Authorized_For_Every_Role(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/api/auth/me", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(new[] { "user" }, document.RootElement.EnumerateObject().Select(p => p.Name));
        var user = document.RootElement.GetProperty("user");
        Assert.Equal(new[] { "created_at", "display_name", "id", "is_active", "last_login_at", "role", "username" }, user.EnumerateObject().Select(p => p.Name).OrderBy(x => x));
        Assert.False(string.IsNullOrEmpty(user.GetProperty("id").GetString()));
        Assert.Equal(role == "admin" ? "admin" : $"task006-{role}", user.GetProperty("username").GetString());
        Assert.Equal(role == "admin" ? "TASK-006 Admin" : role == "member" ? "TASK-006 Member" : "TASK-006 Readonly", user.GetProperty("display_name").GetString());
        Assert.Equal(role, user.GetProperty("role").GetString());
        Assert.True(user.GetProperty("is_active").GetBoolean());
        Assert.Equal(JsonValueKind.Number, user.GetProperty("created_at").ValueKind);
        Assert.True(user.GetProperty("last_login_at").ValueKind is JsonValueKind.Number or JsonValueKind.Null);
        foreach (var forbidden in new[] { "password", "password_hash", "token", "secret", "api_key" }) Assert.False(user.TryGetProperty(forbidden, out _));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("readonly")]
    public async Task FileRead_Is_Authorized_For_Every_Role(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/file-read", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"file-read\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task AdminUsers_Is_Authorized_For_Admin()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/admin-users", _fixture.AdminToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"admin-users\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Localhost_Bypass_Uses_Admin_Authorization()
    {
        var (_, client) = _fixture.CreateLocalhostHost();

        var response = await client.GetAsync("/probe/admin-users");

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"admin-users\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Disabled_Security_Bypasses_Both_Middlewares()
    {
        var (_, client) = _fixture.CreateEnabledFalseHost();

        var response = await client.GetAsync("/probe/no-metadata");

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"no-metadata\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Disabled_Security_AuthMe_Returns_Controller_401()
    {
        var (_, client) = _fixture.CreateEnabledFalseHost();

        var response = await client.GetAsync("/api/auth/me");

        await AssertJsonAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未登录，请先登录\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task XForwardedFor_Cannot_Forge_Localhost_Bypass()
    {
        var (_, client) = _fixture.CreateForwardedForHost();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "127.0.0.1");

        var response = await client.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未授权：请登录（/api/auth/login）或使用有效 API Key\"}");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("readonly")]
    public async Task OpenApi_Requires_SystemRead_And_All_Roles_Have_It(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/openapi/v1.json", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("openapi", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenApi_WithoutCredentials_IsUnauthorized()
    {
        var response = await _fixture.DefaultClient.GetAsync("/openapi/v1.json");
        await AssertJsonAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未授权：请登录（/api/auth/login）或使用有效 API Key\"}");
    }

    [Fact]
    public void OpenApi_Endpoint_Has_Only_SystemRead_Metadata()
    {
        var endpoints = _fixture.DefaultFactory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        var endpoint = Assert.Single(endpoints,
            static e => e.RoutePattern.RawText == "/openapi/{documentName}.json");
        var permissions = endpoint.Metadata.GetOrderedMetadata<KejiRequirePermissionAttribute>();
        Assert.Single(permissions);
        Assert.Equal(KejiPermission.SystemRead, permissions[0].Permission);
        Assert.Null(endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>());
    }

    [Fact]
    public async Task Valid_Identity_Unknown_Route_Continues_To_404()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/does-not-exist", _fixture.AdminToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    [InlineData("readonly")]
    public async Task Missing_Permission_Metadata_Is_Forbidden_For_Every_Role(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/no-metadata", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"权限不足\"}");
    }

    [Fact]
    public async Task Permission_Metadata_Without_CurrentUser_Is_Unauthorized()
    {
        var (_, client) = _fixture.CreateNullCurrentUserHost();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/probe/account-read");
        request.Headers.Add("X-API-Key", AuthorizationIntegrationFixture.ApiKey);

        var response = await client.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未登录，请先登录\"}");
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("member")]
    public async Task MultiplePermissions_AdminAndMemberAllowed(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/multiple-permissions", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"multiple-permissions\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Multiple_Permissions_Containing_AdminOnly_Denies_ReadonlyAsAdminRequired()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/multiple-admin-permissions", _fixture.ReadonlyToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}");
    }

    [Theory]
    [InlineData("admin", HttpStatusCode.OK, "{\"result\":\"multiple-admin-permissions\"}")]
    [InlineData("member", HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}")]
    [InlineData("readonly", HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}")]
    public async Task MultipleAdminPermissions_AllRoles(string role, HttpStatusCode status, string body)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest("/probe/multiple-admin-permissions", TokenFor(role));
        await AssertJsonAsync(await _fixture.DefaultClient.SendAsync(request), status, body, requireExactContentType: false);
    }

    [Fact]
    public async Task Multiple_Permissions_Readonly_IsWriteDenied()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/multiple-permissions", _fixture.ReadonlyToken);
        var response = await _fixture.DefaultClient.SendAsync(request);
        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"当前账号无写入权限\"}");
    }

    [Fact]
    public async Task Readonly_Write_Is_Forbidden_With_Exact_Response()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/file-write", _fixture.ReadonlyToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"当前账号无写入权限\"}");
    }

    [Fact]
    public async Task AdminUsers_WritePermission_Member_IsDeniedAsAdminRequired()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/admin-users", _fixture.MemberToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}");
    }

    [Fact]
    public async Task AdminUsers_WritePermission_ReadonlyUsesAdminPriority()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/admin-users", _fixture.ReadonlyToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}");
    }

    [Theory]
    [InlineData("member")]
    [InlineData("readonly")]
    public async Task NonWriteAdminOnlyPermission_IsAdminRequiredForEveryNonAdmin(string role)
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/admin-conversations", TokenFor(role));

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"需要管理员权限\"}");
    }

    [Fact]
    public async Task Conflicting_Metadata_Is_Converted_By_Global_Middleware_To_Safe_500()
    {
        var response = await _fixture.DefaultClient.GetAsync("/probe/conflicting-metadata");

        await AssertJsonAsync(response, HttpStatusCode.InternalServerError, "{\"detail\":\"服务器内部错误\"}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("conflicting-metadata", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccountSelfRead", body, StringComparison.OrdinalIgnoreCase);
        foreach (var secret in new[] { "admin", "member", "readonly", "Authorization", "Bearer", "JWT", "API Key", _fixture.AdminToken, _fixture.MemberToken, _fixture.ReadonlyToken, "should-not-reach" })
            Assert.DoesNotContain(secret, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Controller_Exception_Is_Converted_By_Global_Middleware_To_Safe_500()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/throwing", _fixture.AdminToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.InternalServerError, "{\"detail\":\"服务器内部错误\"}");
    }

    private string TokenFor(string role) => role switch
    {
        "admin" => _fixture.AdminToken,
        "member" => _fixture.MemberToken,
        "readonly" => _fixture.ReadonlyToken,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown test role."),
    };

    private static async Task AssertJsonAsync(
        HttpResponseMessage response,
        HttpStatusCode expectedStatus,
        string expectedBody,
        bool requireExactContentType = true)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        if (requireExactContentType)
            Assert.Equal("application/json", response.Content.Headers.ContentType?.ToString());
        else
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(expectedBody, await response.Content.ReadAsStringAsync());
    }
}
