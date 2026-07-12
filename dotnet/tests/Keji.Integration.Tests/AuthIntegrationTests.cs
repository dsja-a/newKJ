using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Keji.Contracts.DTOs;
using Keji.Persistence;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Keji.Integration.Tests;

public class AuthIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly string _jwtSecret = "this-is-a-test-secret-that-is-at-least-32-bytes-long!!";
    private readonly string _apiKey = "test-api-key-that-is-at-least-32-bytes-lon!!";
    private readonly string _adminPassword = "test-admin-password-123!!";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public AuthIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"keji_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test.db");
        _factory = CreateFactory();
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _factory?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private WebApplicationFactory<Program> CreateFactory(
        KejiAuthMode authMode = KejiAuthMode.Both,
        string? jwtSecret = null,
        string? apiKey = null,
        bool allowLocalhostWithoutAuth = false,
        bool allowApiKeyInQuery = false,
        string? adminPassword = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                    if (pDesc != null) services.Remove(pDesc);
                    services.AddSingleton(new KejiPersistenceOptions
                    {
                        ProjectRoot = _tempDir,
                        DatabasePath = _dbPath,
                        BusyTimeoutMilliseconds = 5000,
                        EnableWal = true,
                        EnableForeignKeys = true,
                        CreateDirectoryIfMissing = true,
                    });

                    var sDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiSecurityOptions));
                    if (sDesc != null) services.Remove(sDesc);
                    services.AddSingleton(new KejiSecurityOptions
                    {
                        Enabled = true,
                        AuthMode = authMode,
                        ApiKey = apiKey ?? _apiKey,
                        JwtSecret = jwtSecret ?? _jwtSecret,
                        JwtExpireHours = 72,
                        JwtClockSkewSeconds = 0,
                        AllowLocalhostWithoutAuth = allowLocalhostWithoutAuth,
                        AllowApiKeyInQuery = allowApiKeyInQuery,
                        PublicPaths = new List<string> { "/api/auth/login" },
                        BootstrapAdmin = new BootstrapAdminOptions
                        {
                            Username = "admin",
                            Password = adminPassword ?? _adminPassword,
                            DisplayName = "系统管理员",
                        },
                    });
                });
            });
    }

    private async Task<LoginResponse> LoginAsAdminAsync()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        return result;
    }

    private string CreateValidToken(string sub, string role, string username = "testuser")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, sub),
                new Claim(JwtRegisteredClaimNames.UniqueName, username),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
            }),
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    [Fact]
    public async Task Login_Succeeds_With_Correct_Credentials()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.NotNull(result.User);
        Assert.Equal("admin", result.User.Username);
    }

    [Fact]
    public async Task Login_Response_Uses_Expires_In_Field()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("expires_in", out _));
        Assert.False(root.TryGetProperty("expiresIn", out _));
    }

    [Fact]
    public async Task Login_User_Json_Uses_Snake_Case_Fields()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var user = doc.RootElement.GetProperty("user");

        var expectedFields = new[] { "id", "username", "display_name", "role", "is_active", "created_at", "last_login_at" };
        foreach (var field in expectedFields)
            Assert.True(user.TryGetProperty(field, out _), $"Missing field: {field}");
    }

    [Fact]
    public async Task Wrong_Password_Returns_401()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = "wrong-password-123!!" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_Existent_User_Returns_401()
    {
        var loginReq = new LoginRequest { Username = "nonexistent_user", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Disabled_User_Returns_401()
    {
        var loginData = await LoginAsAdminAsync();
        Assert.NotNull(loginData.User);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var userId = loginData.User.Id;
        await userRepo.UpdateAsync(userId, new UpdateUserCommand { IsActive = false });

        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_Invalid_Request_Returns_422()
    {
        var emptyReq = new LoginRequest { Username = "", Password = "" };
        var response1 = await _client.PostAsJsonAsync("/api/auth/login", emptyReq);
        Assert.Equal(422, (int)response1.StatusCode);

        var missingFields = new { };
        var response2 = await _client.PostAsJsonAsync("/api/auth/login", missingFields);
        Assert.Equal(422, (int)response2.StatusCode);
    }

    [Fact]
    public async Task Jwt_Token_Can_Call_Me()
    {
        var loginData = await LoginAsAdminAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var meResult = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.NotNull(meResult);
        Assert.NotNull(meResult.User);
        Assert.Equal(loginData.User!.Id, meResult.User.Id);
        Assert.Equal("admin", meResult.User.Role);
    }

    [Fact]
    public async Task No_Token_Calling_Me_Returns_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_Jwt_Returns_401()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSecret));
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, "anyuser"),
                new Claim(JwtRegisteredClaimNames.UniqueName, "anyuser"),
                new Claim(ClaimTypes.Role, "member"),
            }),
            IssuedAt = DateTime.UtcNow.AddHours(-2),
            NotBefore = DateTime.UtcNow.AddHours(-2),
            Expires = DateTime.UtcNow.AddHours(-1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        var expiredToken = handler.WriteToken(handler.CreateToken(descriptor));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Tampered_Jwt_Returns_401()
    {
        var validToken = CreateValidToken("someuser", "member");
        var parts = validToken.Split('.');
        var tamperedPayload = Base64UrlEncoder.Encode(
            Encoding.UTF8.GetBytes("{\"sub\":\"hacker\",\"unique_name\":\"hacker\",\"role\":\"admin\"}"));
        var tamperedToken = $"{parts[0]}.{tamperedPayload}.{parts[2]}";

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tamperedToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_Disabled_After_Token_Issued_Returns_401()
    {
        var loginData = await LoginAsAdminAsync();
        Assert.NotNull(loginData.User);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpdateAsync(loginData.User.Id, new UpdateUserCommand { IsActive = false });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task User_Role_Changed_After_Token_Issued_Returns_New_Role()
    {
        var loginData = await LoginAsAdminAsync();
        Assert.NotNull(loginData.User);
        Assert.Equal("admin", loginData.User.Role);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        await userRepo.UpdateAsync(loginData.User.Id, new UpdateUserCommand { Role = "readonly" });

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var meResult = await response.Content.ReadFromJsonAsync<AuthMeResponse>();
        Assert.NotNull(meResult);
        Assert.NotNull(meResult.User);
        Assert.Equal("readonly", meResult.User.Role);
    }

    [Fact]
    public async Task Api_Key_Calling_Me_Returns_401_With_Account_Disabled()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-API-Key", _apiKey);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("账号已禁用或不存在", body);
    }

    [Fact]
    public async Task Localhost_Identity_Follows_DB_Semantics()
    {
        using var localhostFactory = CreateFactory(allowLocalhostWithoutAuth: true);
        using var localhostClient = localhostFactory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-API-Key", _apiKey);
        var response = await localhostClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("账号已禁用或不存在", body);
    }

    [Fact]
    public async Task UserOnly_Mode_Rejects_API_Key()
    {
        using var userOnlyFactory = CreateFactory(
            authMode: KejiAuthMode.UserOnly,
            apiKey: null,
            adminPassword: _adminPassword);
        using var userOnlyClient = userOnlyFactory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-API-Key", _apiKey);
        var response = await userOnlyClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiKeyOnly_Mode_Rejects_JWT()
    {
        using var apiKeyOnlyFactory = CreateFactory(
            authMode: KejiAuthMode.ApiKeyOnly,
            jwtSecret: _jwtSecret,
            adminPassword: _adminPassword);
        using var apiKeyOnlyClient = apiKeyOnlyFactory.CreateClient();

        var token = CreateValidToken("testuser", "member");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await apiKeyOnlyClient.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Both_Mode_Accepts_Both_Auth_Types()
    {
        var loginData = await LoginAsAdminAsync();

        var jwtRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        jwtRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var jwtResponse = await _client.SendAsync(jwtRequest);
        Assert.Equal(HttpStatusCode.OK, jwtResponse.StatusCode);

        var apiKeyRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        apiKeyRequest.Headers.Add("X-API-Key", _apiKey);
        var apiKeyResponse = await _client.SendAsync(apiKeyRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, apiKeyResponse.StatusCode);
        var body = await apiKeyResponse.Content.ReadAsStringAsync();
        Assert.Contains("账号已禁用或不存在", body);
    }

    [Fact]
    public async Task Query_API_Key_Is_Rejected_By_Default()
    {
        var response = await _client.GetAsync($"/api/auth/me?api_key={_apiKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_Admin_Is_Created_Only_Once()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var loginData = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginData);
        Assert.Equal("admin", loginData.User!.Username);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var count = await userRepo.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Response_Does_Not_Contain_PasswordHash()
    {
        var loginData = await LoginAsAdminAsync();

        var json = JsonSerializer.Serialize(loginData);
        Assert.DoesNotContain("PasswordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", json, StringComparison.OrdinalIgnoreCase);

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var meResponse = await _client.SendAsync(meRequest);
        var meJson = await meResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", meJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password_hash", meJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Response_Does_Not_Contain_Sensitive_Secrets()
    {
        var loginData = await LoginAsAdminAsync();

        var json = JsonSerializer.Serialize(loginData);
        Assert.DoesNotContain(_apiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(_jwtSecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(_adminPassword, json, StringComparison.Ordinal);

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var meResponse = await _client.SendAsync(meRequest);
        var meJson = await meResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(_apiKey, meJson, StringComparison.Ordinal);
        Assert.DoesNotContain(_jwtSecret, meJson, StringComparison.Ordinal);
        Assert.DoesNotContain(_adminPassword, meJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Program_Startup_Completes_DB_Init_Before_Accepting_Requests()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = _adminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var count = await userRepo.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Fail_Closed_When_Config_Lacks_Secrets()
    {
        using var brokenFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                    if (pDesc != null) services.Remove(pDesc);
                    services.AddSingleton(new KejiPersistenceOptions
                    {
                        ProjectRoot = _tempDir,
                        DatabasePath = _dbPath,
                        BusyTimeoutMilliseconds = 5000,
                        EnableWal = true,
                        EnableForeignKeys = true,
                        CreateDirectoryIfMissing = true,
                    });

                    var sDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiSecurityOptions));
                    if (sDesc != null) services.Remove(sDesc);
                    services.AddSingleton(new KejiSecurityOptions
                    {
                        Enabled = true,
                        AuthMode = KejiAuthMode.UserOnly,
                        JwtSecret = null,
                        ApiKey = null,
                        JwtExpireHours = 72,
                        JwtClockSkewSeconds = 0,
                        AllowLocalhostWithoutAuth = false,
                        AllowApiKeyInQuery = false,
                        PublicPaths = new List<string> { "/api/auth/login" },
                        BootstrapAdmin = new BootstrapAdminOptions
                        {
                            Username = "admin",
                            Password = _adminPassword,
                            DisplayName = "系统管理员",
                        },
                    });
                });
            });

        using var brokenClient = brokenFactory.CreateClient();

        var response = await brokenClient.GetAsync("/api/auth/me");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
