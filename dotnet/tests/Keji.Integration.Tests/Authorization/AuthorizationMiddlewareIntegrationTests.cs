using System.Net;

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

    [Fact]
    public async Task Multiple_Permissions_Use_And_Semantics_When_All_Are_Granted()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/multiple-permissions", _fixture.MemberToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.OK, "{\"result\":\"multiple-permissions\"}", requireExactContentType: false);
    }

    [Fact]
    public async Task Multiple_Permissions_Use_And_Semantics_When_One_Is_Denied()
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
    public async Task AdminUsers_WritePermission_ReadonlyUsesWriteDenialPriority()
    {
        using var request = AuthorizationIntegrationFixture.BearerRequest(
            "/probe/admin-users", _fixture.ReadonlyToken);

        var response = await _fixture.DefaultClient.SendAsync(request);

        await AssertJsonAsync(response, HttpStatusCode.Forbidden, "{\"detail\":\"当前账号无写入权限\"}");
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
