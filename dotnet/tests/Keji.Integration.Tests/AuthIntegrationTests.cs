using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Keji.Api.HostedServices;
using Keji.Contracts.DTOs;
using Keji.Persistence;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Exceptions;
using Keji.Security.Options;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace Keji.Integration.Tests;

public class AuthIntegrationTests : IDisposable
{
    private static readonly string JwtSecret = "this-is-a-test-secret-that-is-at-least-32-bytes-long!!";
    private static readonly string ApiKey = "test-api-key-that-is-at-least-32-bytes-lon!!";
    private static readonly string AdminPassword = "test-admin-password-123!!";

    private static readonly string EnvJwt = "TEST_JWT_SECRET";
    private static readonly string EnvApiKey = "TEST_API_KEY";
    private static readonly string EnvAdminPw = "TEST_ADMIN_PASSWORD";

    private readonly string _tempDir;
    private readonly Dictionary<string, string?> _savedEnv = new();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    static AuthIntegrationTests()
    {
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
    }

    public AuthIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"keji_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        WriteConfig(_tempDir, DefaultConfig());

        SaveEnv(EnvJwt, JwtSecret);
        SaveEnv(EnvApiKey, ApiKey);
        SaveEnv(EnvAdminPw, AdminPassword);

        _factory = CreateFactory(_tempDir);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _factory?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var (key, value) in _savedEnv)
            Environment.SetEnvironmentVariable(key, value);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private void SaveEnv(string key, string? value)
    {
        _savedEnv.TryAdd(key, Environment.GetEnvironmentVariable(key));
        Environment.SetEnvironmentVariable(key, value);
    }

    private static string DefaultConfig() => @"database:
  path: test.db
security:
  auth_mode: both
  jwt_secret: ${TEST_JWT_SECRET}
  api_key: ${TEST_API_KEY}
  bootstrap_admin:
    username: admin
    password: ${TEST_ADMIN_PASSWORD}
    display_name: Admin
";

    private static string UserOnlyConfig() => @"database:
  path: test.db
security:
  auth_mode: user_only
  jwt_secret: ${TEST_JWT_SECRET}
  bootstrap_admin:
    username: admin
    password: ${TEST_ADMIN_PASSWORD}
    display_name: Admin
";

    private static string ApiKeyOnlyConfig() => @"database:
  path: test.db
security:
  auth_mode: api_key_only
  api_key: ${TEST_API_KEY}
  bootstrap_admin:
    username: admin
    password: ${TEST_ADMIN_PASSWORD}
    display_name: Admin
";

    private static string EnabledFalseConfig() => @"database:
  path: test.db
security:
  enabled: false
";

    private static void WriteConfig(string dir, string yaml)
    {
        File.WriteAllText(Path.Combine(dir, "config.yaml"), yaml);
    }

    private static WebApplicationFactory<Program> CreateFactory(string projectRoot)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Keji:ProjectRoot", projectRoot);
                builder.ConfigureServices(services =>
                {
                    var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                    if (pDesc != null) services.Remove(pDesc);
                    services.AddSingleton(new KejiPersistenceOptions
                    {
                        ProjectRoot = projectRoot,
                        DatabasePath = Path.Combine(projectRoot, "test.db"),
                        BusyTimeoutMilliseconds = 5000,
                        EnableWal = true,
                        EnableForeignKeys = true,
                        CreateDirectoryIfMissing = true,
                    });
                });
            });
    }

    private async Task<LoginResponse> LoginAsAdminAsync()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(result);
        return result;
    }

    private string CreateValidToken(string sub, string role, string username = "testuser")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", sub),
                new Claim("username", username),
                new Claim("role", role),
                new Claim("jti", Guid.NewGuid().ToString("N")),
                new Claim("iat",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
            }),
            IssuedAt = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };
        return handler.WriteToken(handler.CreateToken(descriptor));
    }

    private static async Task AssertJsonResponseAsync(HttpResponseMessage response, HttpStatusCode expectedStatus, string expectedBody)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedBody, body);
    }

    private sealed class ThrowingUserRepository : IUserRepository
    {
        public Task<int> CountAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<UserAccountRecord?> GetByUsernameAsync(string username, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<UserAccountRecord?> GetByIdAsync(string userId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<List<UserSummaryRecord>> ListAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<string> CreateAsync(string username, string passwordHash, string role = "member", string displayName = "", CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<bool> UpdateAsync(string userId, UpdateUserCommand command, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task TouchLoginAsync(string userId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
        public Task<bool> DeleteAsync(string userId, CancellationToken ct = default)
            => throw new InvalidOperationException("Simulated repository failure");
    }

    // ── Default public paths tests ────────────────────

    [Fact]
    public async Task Login_Public_Without_Credentials()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LoginEvil_Returns_401()
    {
        var response = await _client.GetAsync("/api/auth/login-evil");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Static_Path_Is_Public()
    {
        var response = await _client.GetAsync("/static/test.js");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SecurityStatus_Path_Is_Public()
    {
        var response = await _client.GetAsync("/api/security/status");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WorkPath_Is_Public()
    {
        var response = await _client.GetAsync("/api/work/callback");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_Is_Public()
    {
        var response = await _client.GetAsync("/health");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Root_Is_Public()
    {
        var response = await _client.GetAsync("/");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Favicon_Is_Public()
    {
        var response = await _client.GetAsync("/favicon.ico");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Login tests ───────────────────────────────────

    [Fact]
    public async Task Login_Succeeds_With_Correct_Credentials()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
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
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
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
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
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

        await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"用户名或密码错误\"}");
    }

    [Fact]
    public async Task Non_Existent_User_Returns_401()
    {
        var loginReq = new LoginRequest { Username = "nonexistent_user", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"用户名或密码错误\"}");
    }

    [Fact]
    public async Task Disabled_User_Returns_401()
    {
        var loginData = await LoginAsAdminAsync();
        Assert.NotNull(loginData.User);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var userId = loginData.User.Id;
        await userRepo.UpdateAsync(userId, new UpdateUserCommand { IsActive = false });

        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"用户名或密码错误\"}");
    }

    [Fact]
    public async Task Login_Invalid_Request_Returns_422()
    {
        var emptyReq = new LoginRequest { Username = "", Password = "" };
        var response1 = await _client.PostAsJsonAsync("/api/auth/login", emptyReq);
        await AssertJsonResponseAsync(response1, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");

        var missingFields = new { };
        var response2 = await _client.PostAsJsonAsync("/api/auth/login", missingFields);
        await AssertJsonResponseAsync(response2, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    // ── Me endpoint tests ─────────────────────────────

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
    public async Task Me_401_Body_Exact_Match()
    {
        var response = await _client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var expected = "{\"detail\":\"未授权：请登录（/api/auth/login）或使用有效 API Key\"}";
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task Expired_Jwt_Returns_401()
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret));
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", "anyuser"),
                new Claim("username", "anyuser"),
                new Claim("role", "member"),
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
            Encoding.UTF8.GetBytes("{\"sub\":\"hacker\",\"username\":\"hacker\",\"role\":\"admin\"}"));
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

    // ── API Key tests ─────────────────────────────────

    [Fact]
    public async Task Api_Key_Calling_Me_Returns_401_With_Account_Disabled()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("X-API-Key", ApiKey);
        var response = await _client.SendAsync(request);

        await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"账号已禁用或不存在\"}");
    }

    [Fact]
    public async Task UserOnly_Mode_Rejects_API_Key()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_uo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, UserOnlyConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Add("X-API-Key", ApiKey);
                var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public async Task ApiKeyOnly_Mode_Rejects_JWT()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_ako_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, ApiKeyOnlyConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var token = CreateValidToken("testuser", "member");

                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await client.SendAsync(request);

                Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
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
        apiKeyRequest.Headers.Add("X-API-Key", ApiKey);
        var apiKeyResponse = await _client.SendAsync(apiKeyRequest);
        await AssertJsonResponseAsync(apiKeyResponse, HttpStatusCode.Unauthorized, "{\"detail\":\"账号已禁用或不存在\"}");
    }

    [Fact]
    public async Task Query_API_Key_Is_Rejected_By_Default()
    {
        var response = await _client.GetAsync($"/api/auth/me?api_key={ApiKey}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Auth mode specific tests ──────────────────────

    [Fact]
    public async Task ApiKeyOnly_Login_Returns_503()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_ako_login_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, ApiKeyOnlyConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
                var response = await client.PostAsJsonAsync("/api/auth/login", loginReq);

                await AssertJsonResponseAsync(response, (HttpStatusCode)503, "{\"detail\":\"当前认证模式不支持用户登录\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    // ── Bootstrap tests ───────────────────────────────

    [Fact]
    public async Task Bootstrap_Admin_Is_Created_Only_Once()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
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
    public async Task Program_Startup_Completes_DB_Init_Before_Accepting_Requests()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var userRepo = _factory.Services.GetRequiredService<IUserRepository>();
        var count = await userRepo.CountAsync();
        Assert.Equal(1, count);
    }

    // ── Security response tests ───────────────────────

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
        Assert.DoesNotContain(ApiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain(JwtSecret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminPassword, json, StringComparison.Ordinal);

        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", loginData.Token);
        var meResponse = await _client.SendAsync(meRequest);
        var meJson = await meResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(ApiKey, meJson, StringComparison.Ordinal);
        Assert.DoesNotContain(JwtSecret, meJson, StringComparison.Ordinal);
        Assert.DoesNotContain(AdminPassword, meJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Localhost_Identity_Follows_DB_Semantics()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_localhost_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        var config = @"database:
  path: test.db
security:
  auth_mode: both
  jwt_secret: ${TEST_JWT_SECRET}
  api_key: ${TEST_API_KEY}
  allow_localhost_without_auth: true
  bootstrap_admin:
    username: admin
    password: ${TEST_ADMIN_PASSWORD}
    display_name: Admin
";
        try
        {
            WriteConfig(subDir, config);
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Add("X-API-Key", ApiKey);
                var response = await client.SendAsync(request);

                await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"账号已禁用或不存在\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    // ── Fail-closed tests ─────────────────────────────

    [Fact]
    public void Fail_Closed_When_No_JwtSecret_UserOnly()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_nojwt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, UserOnlyConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, null);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                var ex = Assert.Throws<KejiSecurityConfigurationException>(() => factory.CreateClient());
                Assert.Contains("JWT Secret", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public void Fail_Closed_When_No_ApiKey_ApiKeyOnly()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_noak_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, ApiKeyOnlyConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, null);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = CreateFactory(subDir);
                var ex = Assert.Throws<KejiSecurityConfigurationException>(() => factory.CreateClient());
                Assert.Contains("API Key", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public void Fail_Closed_When_No_BootstrapPassword_ZeroUsers()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_nopw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, DefaultConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, null);

                using var factory = CreateFactory(subDir);
                var ex = Assert.Throws<KejiSecurityConfigurationException>(() => factory.CreateClient());
                Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingUser_WithoutBootstrapPassword_StartsSuccessfully()
    {
        // Bootstrap first with password
        var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Now create a factory without bootstrap password - should work since user exists
        var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
        var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
        var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
        try
        {
            Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
            Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
            Environment.SetEnvironmentVariable(EnvAdminPw, null);

            using var factory = CreateFactory(_tempDir);
            using var client = factory.CreateClient();
            var response2 = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, response2.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
            Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
            Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
        }
    }

    // ── New tests: Repository failure → 500 ───────────

    [Fact]
    public async Task Login_RepositoryFailure_Returns500()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_500login_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, DefaultConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseSetting("Keji:ProjectRoot", subDir);
                        builder.ConfigureServices(services =>
                        {
                            var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                            if (pDesc != null) services.Remove(pDesc);
                            services.AddSingleton(new KejiPersistenceOptions
                            {
                                ProjectRoot = subDir,
                                DatabasePath = Path.Combine(subDir, "test.db"),
                                BusyTimeoutMilliseconds = 5000,
                                EnableWal = true,
                                EnableForeignKeys = true,
                                CreateDirectoryIfMissing = true,
                            });

                            var initDesc = services.FirstOrDefault(d =>
                                d.ServiceType == typeof(IHostedService) &&
                                d.ImplementationType == typeof(KejiStartupInitializer));
                            if (initDesc != null) services.Remove(initDesc);

                            var rDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IUserRepository));
                            if (rDesc != null) services.Remove(rDesc);
                            services.AddSingleton<IUserRepository>(new ThrowingUserRepository());
                        });
                    });
                using var client = factory.CreateClient();

                var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
                var httpResponse = await client.PostAsJsonAsync("/api/auth/login", loginReq);
                await AssertJsonResponseAsync(httpResponse, HttpStatusCode.InternalServerError, "{\"detail\":\"服务器内部错误\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public async Task Me_RepositoryFailure_Returns500()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_500me_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, DefaultConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, JwtSecret);
                Environment.SetEnvironmentVariable(EnvApiKey, ApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, AdminPassword);

                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseSetting("Keji:ProjectRoot", subDir);
                        builder.ConfigureServices(services =>
                        {
                            var pDesc = services.SingleOrDefault(d => d.ServiceType == typeof(KejiPersistenceOptions));
                            if (pDesc != null) services.Remove(pDesc);
                            services.AddSingleton(new KejiPersistenceOptions
                            {
                                ProjectRoot = subDir,
                                DatabasePath = Path.Combine(subDir, "test.db"),
                                BusyTimeoutMilliseconds = 5000,
                                EnableWal = true,
                                EnableForeignKeys = true,
                                CreateDirectoryIfMissing = true,
                            });

                            var initDesc = services.FirstOrDefault(d =>
                                d.ServiceType == typeof(IHostedService) &&
                                d.ImplementationType == typeof(KejiStartupInitializer));
                            if (initDesc != null) services.Remove(initDesc);

                            var rDesc = services.SingleOrDefault(d => d.ServiceType == typeof(IUserRepository));
                            if (rDesc != null) services.Remove(rDesc);
                            services.AddSingleton<IUserRepository>(new ThrowingUserRepository());
                        });
                    });
                using var client = factory.CreateClient();

                // Login fails due to repository failure
                var loginReq = new LoginRequest { Username = "admin", Password = AdminPassword };
                var loginResponse = await client.PostAsJsonAsync("/api/auth/login", loginReq);
                await AssertJsonResponseAsync(loginResponse, HttpStatusCode.InternalServerError, "{\"detail\":\"服务器内部错误\"}");

                // Even with a valid token, me endpoint fails
                var token = CreateValidToken("testuser", "member");
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var meResponse = await client.SendAsync(request);
                await AssertJsonResponseAsync(meResponse, HttpStatusCode.InternalServerError, "{\"detail\":\"服务器内部错误\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    // ── New tests: 422 validation ─────────────────────

    [Fact]
    public async Task Login_MalformedJson_Returns422()
    {
        var content = new StringContent("{", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_WrongRootType_Returns422()
    {
        var content = new StringContent("\"not json\"", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_NullBody_Returns422()
    {
        var content = new StringContent("", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_WrongJsonType_Returns422()
    {
        var content = new StringContent("{\"username\":123, \"password\":true}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_MissingUsername_Returns422()
    {
        var content = new StringContent("{\"password\":\"valid123\"}", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/auth/login", content);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_EmptyUsername_Returns422()
    {
        var loginReq = new LoginRequest { Username = "", Password = "valid123" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_UsernameTooLong_Returns422()
    {
        var loginReq = new LoginRequest { Username = new string('a', 65), Password = "valid123" };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    [Fact]
    public async Task Login_PasswordTooLong_Returns422()
    {
        var loginReq = new LoginRequest { Username = "admin", Password = new string('b', 129) };
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginReq);
        await AssertJsonResponseAsync(response, HttpStatusCode.UnprocessableEntity, "{\"detail\":\"请求格式错误\"}");
    }

    // ── New tests: Enabled=false ──────────────────────

    [Fact]
    public async Task EnabledFalse_NoSecrets_StartsAndMeReturnsNotLoggedIn()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_ef_ns_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, EnabledFalseConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                // Don't set any env vars - not needed since security is disabled
                Environment.SetEnvironmentVariable(EnvJwt, null);
                Environment.SetEnvironmentVariable(EnvApiKey, null);
                Environment.SetEnvironmentVariable(EnvAdminPw, null);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var response = await client.GetAsync("/api/auth/me");
                await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未登录，请先登录\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }

    [Fact]
    public async Task EnabledFalse_ProtectedPath_PassesThroughToController()
    {
        var subDir = Path.Combine(Path.GetTempPath(), $"keji_test_ef_pp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(subDir);
        try
        {
            WriteConfig(subDir, EnabledFalseConfig());
            var savedJwt = Environment.GetEnvironmentVariable(EnvJwt);
            var savedApiKey = Environment.GetEnvironmentVariable(EnvApiKey);
            var savedAdminPw = Environment.GetEnvironmentVariable(EnvAdminPw);
            try
            {
                Environment.SetEnvironmentVariable(EnvJwt, null);
                Environment.SetEnvironmentVariable(EnvApiKey, null);
                Environment.SetEnvironmentVariable(EnvAdminPw, null);

                using var factory = CreateFactory(subDir);
                using var client = factory.CreateClient();

                var response = await client.GetAsync("/api/auth/me");
                await AssertJsonResponseAsync(response, HttpStatusCode.Unauthorized, "{\"detail\":\"未登录，请先登录\"}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EnvJwt, savedJwt);
                Environment.SetEnvironmentVariable(EnvApiKey, savedApiKey);
                Environment.SetEnvironmentVariable(EnvAdminPw, savedAdminPw);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(subDir))
                Directory.Delete(subDir, recursive: true);
        }
    }
}
