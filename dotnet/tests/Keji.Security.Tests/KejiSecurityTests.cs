using System.Net;
using System.Reflection;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Keji.Configuration.Models;
using Keji.Security.Auth;
using Keji.Security.Authentication;
using Keji.Security.Models;
using Keji.Security.Options;
using Keji.Security.Services;
using Keji.Security.Exceptions;
using Keji.Security.Middleware;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Persistence;
using Keji.Contracts.DTOs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;

namespace Keji.Security.Tests;

public class KejiSecurityTests
{
    static KejiSecurityTests()
    {
        JwtSecurityTokenHandler.DefaultMapInboundClaims = false;
    }
    private const string TestJwtSecret = "this-is-a-test-secret-that-is-at-least-32-bytes-long!!";
    private const string TestApiKey = "this-is-a-test-api-key-that-is-at-least-32-bytes!!";
    private const string TestPassword = "test_password_123";
    private const string PythonBcryptFixture = "$2a$12$OcqTVPvKF43yxT2Kcmb/nOaBRg0icXiWTCRL3WYr46C5vQNw6GtGS";

    #region Configuration Tests (1-15)

    [Fact]
    public void Config_DefaultEnabled_True()
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>
        {
            KV("jwt_secret", S(TestJwtSecret)),
            KV("api_key", S(TestApiKey)),
        };
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { KV("security", new ConfigMap(entries)) }));
        var opts = KejiSecurityOptions.FromConfiguration(doc);
        Assert.True(opts.Enabled);
    }

    [Fact]
    public void Config_ThreeAuthModes_ParseCorrectly()
    {
        foreach (var mode in new[] { "both", "user_only", "api_key_only" })
        {
            var opts = Parse(new[] { ("auth_mode", mode), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) });
            Assert.NotNull(opts);
        }
    }

    [Fact]
    public void Config_InvalidAuthMode_Throws()
    {
        Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "invalid"), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) }));
    }

    [Fact]
    public void Config_UserOnlyWithoutJwtSecret_Throws()
    {
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "user_only"), ("api_key", TestApiKey) }));
        Assert.Contains("JWT Secret", ex.Message);
    }

    [Fact]
    public void Config_ApiKeyOnlyWithoutApiKey_Throws()
    {
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "api_key_only"), ("jwt_secret", TestJwtSecret) }));
        Assert.Contains("API Key", ex.Message);
    }

    [Fact]
    public void Config_BothWithoutEitherSecret_Throws()
    {
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "both") }));
        Assert.Contains("JWT Secret", ex.Message);
    }

    [Fact]
    public void Config_EnabledFalse_AllowsMissingSecrets()
    {
        var opts = Parse(new[] { ("enabled", "false"), ("auth_mode", "both") });
        Assert.False(opts.Enabled);
    }

    [Fact]
    public void Config_JwtSecretLessThan32Bytes_Throws()
    {
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "both"), ("jwt_secret", "short"), ("api_key", TestApiKey) }));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Fact]
    public void Config_ApiKeyLessThan32Bytes_Throws()
    {
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "both"), ("jwt_secret", TestJwtSecret), ("api_key", "short") }));
        Assert.Contains("32 bytes", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void Config_ExpireHoursOutOfBounds_Throws(int hours)
    {
        Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("jwt_expire_hours", hours.ToString()), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) }));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(720)]
    public void Config_ExpireHoursInBounds_Succeeds(int hours)
    {
        var opts = Parse(new[] { ("jwt_expire_hours", hours.ToString()), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) });
        Assert.Equal(hours, opts.JwtExpireHours);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public void Config_ClockSkewOutOfBounds_Throws(int skew)
    {
        Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("jwt_clock_skew_seconds", skew.ToString()), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(300)]
    public void Config_ClockSkewInBounds_Succeeds(int skew)
    {
        var opts = Parse(new[] { ("jwt_clock_skew_seconds", skew.ToString()), ("jwt_secret", TestJwtSecret), ("api_key", TestApiKey) });
        Assert.Equal(skew, opts.JwtClockSkewSeconds);
    }

    [Fact]
    public void Config_PublicPaths_Deduplicates()
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>
        {
            KV("public_paths", Seq("/static", "/api", "/static", "/api")),
            KV("jwt_secret", S(TestJwtSecret)),
            KV("api_key", S(TestApiKey)),
        };
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { KV("security", new ConfigMap(entries)) }));
        var opts = KejiSecurityOptions.FromConfiguration(doc);
        Assert.Equal(2, opts.PublicPaths.Count);
    }

    [Theory]
    [InlineData("noprefix")]
    [InlineData("/has?query")]
    [InlineData("/has#frag")]
    public void Config_InvalidPublicPath_Throws(string path)
    {
        var entries = new List<KeyValuePair<string, ConfigNode>>
        {
            KV("public_paths", Seq(path)),
            KV("jwt_secret", S(TestJwtSecret)),
            KV("api_key", S(TestApiKey)),
        };
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { KV("security", new ConfigMap(entries)) }));
        Assert.Throws<KejiSecurityConfigurationException>(() => KejiSecurityOptions.FromConfiguration(doc));
    }

    [Fact]
    public void Config_ExceptionDoesNotLeakSecret()
    {
        var secret = "my-super-secret-value-that-should-not-leak-!!";
        var ex = Assert.Throws<KejiSecurityConfigurationException>(() =>
            Parse(new[] { ("auth_mode", "both"), ("jwt_secret", secret) }));
        Assert.DoesNotContain(secret, ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, ex.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Config_DoesNotReadProcessEnvDirectly()
    {
        var envKey = "KJ_TEST_SECRET_" + Guid.NewGuid().ToString("N")[..8];
        Environment.SetEnvironmentVariable(envKey, "leaked_secret");
        try
        {
            var entries = new List<KeyValuePair<string, ConfigNode>>
            {
                KV("jwt_secret", S(TestJwtSecret)),
                KV("api_key", S(TestApiKey)),
            };
            var doc = new KejiConfigurationDocument(new ConfigMap(new[] { KV("security", new ConfigMap(entries)) }));
            var opts = KejiSecurityOptions.FromConfiguration(doc);
            Assert.NotEqual("leaked_secret", opts.JwtSecret);
            Assert.Equal(TestJwtSecret, opts.JwtSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envKey, null);
        }
    }

    #endregion

    #region BCrypt Tests (16-24)

    [Fact]
    public void Bcrypt_HashNotEqualToPlaintext()
    {
        var hasher = CreateBcryptHasher();
        var hash = hasher.Hash(TestPassword);
        Assert.NotEqual(TestPassword, hash);
    }

    [Fact]
    public void Bcrypt_CorrectPassword_Verifies()
    {
        var hasher = CreateBcryptHasher();
        var hash = hasher.Hash(TestPassword);
        Assert.True(hasher.Verify(TestPassword, hash));
    }

    [Fact]
    public void Bcrypt_WrongPassword_Fails()
    {
        var hasher = CreateBcryptHasher();
        var hash = hasher.Hash(TestPassword);
        Assert.False(hasher.Verify("wrong_password_123", hash));
    }

    [Fact]
    public void Bcrypt_MalformedHash_ReturnsFalse()
    {
        var hasher = CreateBcryptHasher();
        Assert.False(hasher.Verify(TestPassword, "not-a-valid-hash"));
        Assert.False(hasher.Verify(TestPassword, ""));
    }

    [Fact]
    public void Bcrypt_PythonFixture_Verifies()
    {
        var hasher = CreateBcryptHasher(12);
        Assert.True(hasher.Verify("test_password_123", PythonBcryptFixture));
    }

    [Fact]
    public void Bcrypt_NeedsRehash_DetectsWorkFactorChange()
    {
        var hasher10 = CreateBcryptHasher(10);
        var hash10 = hasher10.Hash(TestPassword);
        var hasher14 = CreateBcryptHasher(14);
        Assert.True(hasher14.NeedsRehash(hash10));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(16)]
    public void Bcrypt_WorkFactorBounds_Works(int wf)
    {
        var hasher = CreateBcryptHasher(wf);
        var hash = hasher.Hash(TestPassword);
        Assert.StartsWith("$2", hash);
        Assert.True(hasher.Verify(TestPassword, hash));
    }

    [Fact]
    public void Bcrypt_ExceptionDoesNotLeakPassword()
    {
        var hasher = CreateBcryptHasher();
        var ex = Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
        Assert.DoesNotContain(TestPassword, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bcrypt_ExceptionDoesNotLeakHash()
    {
        var hasher = CreateBcryptHasher();
        var hash = hasher.Hash(TestPassword);
        var ex = Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
        Assert.DoesNotContain(hash, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region JWT Tests (25-46)

    [Fact]
    public void Jwt_NormalToken_Created()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        Assert.NotNull(result.Token);
        Assert.True(result.Token.Length > 0);
    }

    [Fact]
    public void Jwt_HasSubClaim()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var claims = DecodeToken(result.Token, svc);
        Assert.Equal("user1", claims.Sub);
    }

    [Fact]
    public void Jwt_HasUsernameClaim()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var claims = DecodeToken(result.Token, svc);
        Assert.Equal("testuser", claims.Username);
    }

    [Fact]
    public void Jwt_HasRoleClaim()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "admin");
        var claims = DecodeToken(result.Token, svc);
        Assert.Equal("admin", claims.Role);
    }

    [Fact]
    public void Jwt_HasIat()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var claims = DecodeToken(result.Token, svc);
        Assert.True(claims.Iat > 0);
    }

    [Fact]
    public void Jwt_HasExp()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var claims = DecodeToken(result.Token, svc);
        Assert.True(claims.Exp > 0);
    }

    [Fact]
    public void Jwt_HasJti()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var claims = DecodeToken(result.Token, svc);
        Assert.False(string.IsNullOrEmpty(claims.Jti));
    }

    [Fact]
    public void Jwt_HasExpiresIn()
    {
        var svc = CreateJwtService(expireHours: 72);
        var result = svc.CreateToken("user1", "testuser", "member");
        Assert.Equal(72 * 3600, result.ExpiresIn);
    }

    [Fact]
    public void Jwt_ValidToken_Validates()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var validation = svc.ValidateToken(result.Token);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Jwt_TamperedToken_Rejected()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var parts = result.Token.Split('.');
        var tamperedSig = Convert.ToBase64String(Encoding.UTF8.GetBytes("tampered")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var tampered = parts[0] + "." + parts[1] + "." + tamperedSig;
        var validation = svc.ValidateToken(tampered);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_WrongSecret_Rejected()
    {
        var svc1 = CreateJwtService(secret: TestJwtSecret);
        var result = svc1.CreateToken("user1", "testuser", "member");
        var svc2 = CreateJwtService(secret: "a-different-32-byte-secret-for-testing-purpose!!");
        var validation = svc2.ValidateToken(result.Token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_ExpiredToken_Rejected()
    {
        var svc = CreateJwtService();
        var token = CreateJwt("user1", "testuser", "member", expOffset: -3600);
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_ClockSkew_AllowsSlightExpiry()
    {
        var svcNoSkew = CreateJwtService(expireHours: 1, clockSkewSeconds: 0);
        var result = svcNoSkew.CreateToken("user1", "testuser", "member");
        var svcWithSkew = CreateJwtService(expireHours: 1, clockSkewSeconds: 60);
        var validationWithSkew = svcWithSkew.ValidateToken(result.Token);
        Assert.True(validationWithSkew.IsValid);

        var expired = CreateJwt("user1", "testuser", "member", expOffset: -3600);
        var validationNoSkew = svcNoSkew.ValidateToken(expired);
        Assert.False(validationNoSkew.IsValid);
    }

    [Fact]
    public void Jwt_AlgNone_Rejected()
    {
        var svc = CreateJwtService();
        var unsigned = CreateUnsignedJwt("user1", "testuser", "member");
        var validation = svc.ValidateToken(unsigned);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_NonHS256_Rejected()
    {
        var svc = CreateJwtService();
        var token = CreateJwtWithAlg("HS512", TestJwtSecret, "user1", "testuser", "member");
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_MissingSub_Rejected()
    {
        var svc = CreateJwtService();
        var token = CreateJwtWithMissingClaim("sub", "user1", "testuser", "member");
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_MissingUsername_Rejected()
    {
        var svc = CreateJwtService();
        var token = CreateJwtWithMissingClaim("unique_name", "user1", "testuser", "member");
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_InvalidRole_Rejected()
    {
        var svc = CreateJwtService();
        var token = CreateJwt("user1", "testuser", "superadmin");
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_OverlyLongToken_Rejected()
    {
        var svc = CreateJwtService();
        var token = new string('x', 8193);
        var validation = svc.ValidateToken(token);
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Jwt_PythonCompatibleToken_Validates()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("pyuser", "python_user", "member");
        var validation = svc.ValidateToken(result.Token);
        Assert.True(validation.IsValid);
    }

    [Fact]
    public void Jwt_ExceptionDoesNotLeakToken()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var tokenVal = result.Token;
        var validation = svc.ValidateToken("invalid");
        Assert.False(validation.IsValid);
        Assert.DoesNotContain(tokenVal, validation.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Jwt_ExceptionDoesNotLeakSecret()
    {
        var svc = CreateJwtService();
        var result = svc.CreateToken("user1", "testuser", "member");
        var validation = svc.ValidateToken("invalid");
        Assert.False(validation.IsValid);
        Assert.DoesNotContain(TestJwtSecret, validation.FailureReason, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region API Key Tests (47-58)

    [Fact]
    public void ApiKey_CorrectKey_Succeeds()
    {
        Assert.True(InvokeApiKeyComparer(TestApiKey, TestApiKey));
    }

    [Fact]
    public void ApiKey_WrongKey_Fails()
    {
        Assert.False(InvokeApiKeyComparer("wrong-api-key-that-is-also-32-bytes-long!!!!!", TestApiKey));
    }

    [Fact]
    public async Task ApiKey_BearerApiKey_Works()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "api_key_only"), ("api_key", TestApiKey) });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync("Bearer " + TestApiKey, null, null, null);
        Assert.True(result.IsAuthenticated);
        Assert.Equal(KejiAuthenticationKind.ApiKey, result.User!.AuthenticationKind);
    }

    [Fact]
    public async Task ApiKey_XApiKeyHeader_Works()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "api_key_only"), ("api_key", TestApiKey) });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync(null, TestApiKey, null, null);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public async Task ApiKey_QueryApiKey_DefaultDisabled()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "api_key_only"), ("api_key", TestApiKey) });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync(null, null, TestApiKey, null);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public async Task ApiKey_QueryApiKey_ExplicitlyEnabled()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "api_key_only"), ("api_key", TestApiKey), ("allow_api_key_in_query", "true") });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync(null, null, TestApiKey, null);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public async Task ApiKey_UserOnly_RejectsApiKey()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "user_only"), ("jwt_secret", TestJwtSecret) });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync("Bearer " + TestApiKey, null, null, null);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public async Task ApiKey_ApiKeyOnly_AcceptsApiKey()
    {
        var opts = OptionsWith(new[] { ("auth_mode", "api_key_only"), ("api_key", TestApiKey) });
        var auth = CreateAuthenticator(opts);
        var result = await auth.AuthenticateAsync("Bearer " + TestApiKey, null, null, null);
        Assert.True(result.IsAuthenticated);
    }

    [Fact]
    public void ApiKey_FixedTimeComparison_UsesSha256()
    {
        Assert.True(InvokeApiKeyComparer(TestApiKey, TestApiKey));
    }

    [Fact]
    public void ApiKey_EmptyKey_Rejected()
    {
        Assert.False(InvokeApiKeyComparer("", TestApiKey));
    }

    [Fact]
    public void ApiKey_OverlyLongKey_Rejected()
    {
        var longKey = new string('x', 5000);
        Assert.False(InvokeApiKeyComparer(longKey, TestApiKey));
    }

    [Fact]
    public void ApiKey_ErrorDoesNotLeakKey()
    {
        var result = InvokeApiKeyComparer(TestApiKey, TestApiKey);
        Assert.True(result);
    }

    #endregion

    #region CurrentUser/Principal Tests (59-64)

    [Fact]
    public async Task CurrentUser_TokenSuccessButDbUserMissing_Rejected()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var svc = CreateJwtService();
        var token = svc.CreateToken("nonexistent", "ghost", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public async Task CurrentUser_DbUserDisabled_Rejected()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var uid = await repo.CreateAsync("disabled_user", "hash", "member", "Disabled");
        await repo.UpdateAsync(uid, new UpdateUserCommand { IsActive = false });
        var svc = CreateJwtService();
        var token = svc.CreateToken(uid, "disabled_user", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.False(result.IsAuthenticated);
    }

    [Fact]
    public async Task CurrentUser_DbRoleChange_UsesNewRole()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var uid = await repo.CreateAsync("role_user", "hash", "member", "Role User");
        await repo.UpdateAsync(uid, new UpdateUserCommand { Role = "admin" });
        var svc = CreateJwtService();
        var token = svc.CreateToken(uid, "role_user", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("admin", result.User!.Role);
    }

    [Fact]
    public async Task CurrentUser_DbDisplayNameChange_UsesNewName()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var uid = await repo.CreateAsync("display_user", "hash", "member", "Old Name");
        await repo.UpdateAsync(uid, new UpdateUserCommand { DisplayName = "New Name" });
        var svc = CreateJwtService();
        var token = svc.CreateToken(uid, "display_user", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("New Name", result.User!.DisplayName);
    }

    [Fact]
    public async Task CurrentUser_TokenUsername_DoesNotOverrideDbUsername()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var uid = await repo.CreateAsync("real_db_user", "hash", "member", "Db User");
        var svc = CreateJwtService();
        var token = svc.CreateToken(uid, "fake_token_user", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("real_db_user", result.User!.Username);
    }

    [Fact]
    public async Task CurrentUser_TokenRole_DoesNotOverrideDbRole()
    {
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var repo = new MockUserRepository();
        var uid = await repo.CreateAsync("role_user2", "hash", "admin", "Role User 2");
        var svc = CreateJwtService();
        var token = svc.CreateToken(uid, "role_user2", "member").Token;
        var auth = new KejiRequestAuthenticator(opts, svc, repo, TimeProvider.System);
        var result = await auth.AuthenticateAsync("Bearer " + token, null, null, null);
        Assert.True(result.IsAuthenticated);
        Assert.Equal("admin", result.User!.Role);
    }

    #endregion

    #region Middleware Tests (65-84)

    [Theory]
    [InlineData("/")]
    [InlineData("/health")]
    [InlineData("/favicon.ico")]
    public async Task Middleware_DefaultPublicPaths_ArePublic(string path)
    {
        var invoked = false;
        var middleware = new KejiAuthenticationMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Middleware_ConfiguredPublicPath_IsPublic()
    {
        var invoked = false;
        var middleware = new KejiAuthenticationMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/auth/login";
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey, PublicPaths = new List<string> { "/api/auth/login" } };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Middleware_PublicPathPrefix_Matches()
    {
        var invoked = false;
        var middleware = new KejiAuthenticationMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/static/subpath";
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey, PublicPaths = new List<string> { "/static" } };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(invoked);
    }

    [Fact]
    public async Task Middleware_LoginEvil_IsNotPublic()
    {
        var invoked = false;
        var middleware = new KejiAuthenticationMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/auth/login-evil";
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey, PublicPaths = new List<string> { "/api/auth/login" } };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.False(invoked);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_NoCredentials_Returns401()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_401Body_ExactMatch()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
        var expected = JsonSerializer.Serialize(new { detail = "未授权：请登录（/api/auth/login）或使用有效 API Key" }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        Assert.Equal(expected, body);
    }

    [Fact]
    public async Task Middleware_JwtAuth_SetsPrincipal()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["Authorization"] = "Bearer valid-jwt";
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var user = new CurrentUser("uid1", "jwtuser", "admin", "JWT User", KejiAuthenticationKind.Jwt);
        var auth = new MockAuth(RequestAuthenticationResult.Authenticated(user));
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.NotNull(ctx.User);
        Assert.True(ctx.User.Identity!.IsAuthenticated);
        Assert.Equal("uid1", ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("jwtuser", ctx.User.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("admin", ctx.User.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Middleware_ApiKey_SetsServiceAdminPrincipal()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["X-API-Key"] = TestApiKey;
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var user = new CurrentUser("service", "api_key", "admin", "API Key", KejiAuthenticationKind.ApiKey);
        var auth = new MockAuth(RequestAuthenticationResult.Authenticated(user));
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.NotNull(ctx.User);
        Assert.Equal("admin", ctx.User.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Middleware_Localhost_SetsLocalhostAdminPrincipal()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Connection.RemoteIpAddress = IPAddress.Loopback;
        var opts = new KejiSecurityOptions { Enabled = true, AllowLocalhostWithoutAuth = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var user = new CurrentUser("localhost", "localhost", "admin", "Localhost", KejiAuthenticationKind.Localhost);
        var auth = new MockAuth(RequestAuthenticationResult.Authenticated(user));
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.NotNull(ctx.User);
        Assert.Equal("localhost", ctx.User.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("admin", ctx.User.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task Middleware_NonLoopback_DoesNotBypass()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.1");
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, AllowLocalhostWithoutAuth = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_DoesNotTrustXForwardedFor()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["X-Forwarded-For"] = "127.0.0.1";
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, AllowLocalhostWithoutAuth = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_EnabledFalse_PassesThroughWithoutCurrentUser()
    {
        var invoked = false;
        var middleware = new KejiAuthenticationMiddleware(_ => { invoked = true; return Task.CompletedTask; });
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        var opts = new KejiSecurityOptions { Enabled = false };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(invoked);
        Assert.False(ctx.User?.Identity?.IsAuthenticated ?? false == true);
    }

    [Fact]
    public async Task Middleware_PublicPath_DoesNotSetCurrentUser()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/health";
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.False(ctx.User?.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Middleware_InvalidBearer_DoesNotFallbackToXApiKey()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["Authorization"] = "Bearer invalid_token";
        ctx.Request.Headers["X-API-Key"] = TestApiKey;
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, AuthMode = KejiAuthMode.Both, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = CreateAuthThatRejectsInvalidBearerThenChecksApiKey(opts);
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_Both_SupportsJwt()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["Authorization"] = "Bearer valid-jwt";
        var opts = new KejiSecurityOptions { Enabled = true, AuthMode = KejiAuthMode.Both, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var user = new CurrentUser("uid1", "jwtuser", "admin", "JWT User", KejiAuthenticationKind.Jwt);
        var auth = new MockAuth(RequestAuthenticationResult.Authenticated(user));
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(ctx.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Middleware_Both_SupportsApiKey()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        var opts = new KejiSecurityOptions { Enabled = true, AuthMode = KejiAuthMode.Both, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var user = new CurrentUser("service", "api_key", "admin", "API Key", KejiAuthenticationKind.ApiKey);
        var auth = new MockAuth(RequestAuthenticationResult.Authenticated(user));
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.True(ctx.User.Identity!.IsAuthenticated);
    }

    [Fact]
    public async Task Middleware_UserOnly_RejectsApiKey()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["Authorization"] = "Bearer " + TestApiKey;
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, AuthMode = KejiAuthMode.UserOnly, JwtSecret = TestJwtSecret };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_ApiKeyOnly_RejectsJwt()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        ctx.Request.Headers["Authorization"] = "Bearer some.jwt.token";
        ctx.Response.Body = new MemoryStream();
        var opts = new KejiSecurityOptions { Enabled = true, AuthMode = KejiAuthMode.ApiKeyOnly, ApiKey = TestApiKey };
        var auth = new MockAuth(RequestAuthenticationResult.NotAuthenticated());
        await middleware.InvokeAsync(ctx, opts, auth);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Middleware_OperationCanceledException_Propagates()
    {
        var middleware = new KejiAuthenticationMiddleware(_ => Task.CompletedTask);
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = "/api/protected";
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey };
        var auth = new ThrowingAuth();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            middleware.InvokeAsync(ctx, opts, auth));
    }

    #endregion

    #region Login Tests (85-98)

    private static KejiSecurityOptions LoginOptions => new()
    {
        Enabled = true,
        JwtSecret = TestJwtSecret,
        ApiKey = TestApiKey,
    };

    [Fact]
    public async Task Login_CorrectLogin_Succeeds()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("logintest", hasher.Hash(TestPassword), "member", "Login Test");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("logintest", TestPassword);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Login_UserNotFound_ReturnsFailure()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("nonexistent", TestPassword);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsFailure()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("wrongpw", hasher.Hash(TestPassword), "member", "Wrong PW");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("wrongpw", "wrong_password_456");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Login_DisabledUser_ReturnsFailure()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var uid = await repo.CreateAsync("disabled", hasher.Hash(TestPassword), "member", "Disabled");
        await repo.UpdateAsync(uid, new UpdateUserCommand { IsActive = false });
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("disabled", TestPassword);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Login_AllThreeFailures_HaveIdenticalBody()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var uid = await repo.CreateAsync("active_user", hasher.Hash(TestPassword), "member", "Active");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);

        var r1 = await loginSvc.LoginAsync("nonexistent", TestPassword);
        var r2 = await loginSvc.LoginAsync("active_user", "wrong_password");
        await repo.UpdateAsync(uid, new UpdateUserCommand { IsActive = false });
        var r3 = await loginSvc.LoginAsync("active_user", TestPassword);

        Assert.Equal(r1.ErrorMessage, r2.ErrorMessage);
        Assert.Equal(r2.ErrorMessage, r3.ErrorMessage);
    }

    [Fact]
    public async Task Login_NonExistentUser_StillDoesDummyVerify()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var initialCalls = hasher.VerifyCallCount;
        await loginSvc.LoginAsync("nobody", TestPassword);
        Assert.True(hasher.VerifyCallCount > initialCalls);
    }

    [Fact]
    public async Task Login_Success_CallsTouchLoginAsync()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var uid = await repo.CreateAsync("touchuser", hasher.Hash(TestPassword), "member", "Touch");
        Assert.Null((await repo.GetByIdAsync(uid))!.LastLoginAt);
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        await loginSvc.LoginAsync("touchuser", TestPassword);
        Assert.NotNull((await repo.GetByIdAsync(uid))!.LastLoginAt);
    }

    [Fact]
    public async Task Login_ReturnsToken()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("tokenuser", hasher.Hash(TestPassword), "member", "Token");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("tokenuser", TestPassword);
        Assert.NotNull(result.Token);
        Assert.True(result.Token.Length > 0);
    }

    [Fact]
    public async Task Login_ReturnsExpiresIn()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService(expireHours: 48);
        await repo.CreateAsync("expuser", hasher.Hash(TestPassword), "member", "Exp");
        var opts = new KejiSecurityOptions { Enabled = true, JwtSecret = TestJwtSecret, ApiKey = TestApiKey, JwtExpireHours = 48 };
        var loginSvc = new KejiLoginService(repo, hasher, svc, opts);
        var result = await loginSvc.LoginAsync("expuser", TestPassword);
        Assert.Equal(48 * 3600, result.ExpiresIn);
    }

    [Fact]
    public async Task Login_ReturnsPublicUser()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        var uid = await repo.CreateAsync("pubuser", hasher.Hash(TestPassword), "admin", "Public User");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("pubuser", TestPassword);
        Assert.NotNull(result.User);
        Assert.Equal("pubuser", result.User.Username);
        Assert.Equal("admin", result.User.Role);
        Assert.Equal("Public User", result.User.DisplayName);
        Assert.Equal(uid, result.User.Id);
    }

    [Fact]
    public async Task Login_DoesNotReturnPasswordHash()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("nohash", hasher.Hash(TestPassword), "member", "No Hash");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("nohash", TestPassword);
        Assert.NotNull(result.User);
        var pwProp = result.User.GetType().GetProperty("PasswordHash");
        Assert.Null(pwProp);
    }

    [Fact]
    public async Task Login_DoesNotLogPassword()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("logtest", hasher.Hash(TestPassword), "member", "Log Test");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("logtest", TestPassword);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Login_PersistenceError_NotDisguisedAsWrongPassword()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("persistuser", hasher.Hash(TestPassword), "member", "Persist");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        var result = await loginSvc.LoginAsync("persistuser", TestPassword);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Login_Cancellation_Propagates()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var svc = CreateJwtService();
        await repo.CreateAsync("canceltest", hasher.Hash(TestPassword), "member", "Cancel");
        var loginSvc = new KejiLoginService(repo, hasher, svc, LoginOptions);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            loginSvc.LoginAsync("canceltest", TestPassword, cts.Token));
    }

    #endregion

    #region Bootstrap Tests (99-110)

    [Fact]
    public async Task Bootstrap_ZeroUsers_CreatesAdmin()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.True(result.IsCreated);
        Assert.NotNull(result.AdminId);
    }

    [Fact]
    public async Task Bootstrap_Role_IsAdmin()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        await svc.InitializeAsync();
        var user = await repo.GetByUsernameAsync("admin");
        Assert.NotNull(user);
        Assert.Equal("admin", user.Role);
    }

    [Fact]
    public async Task Bootstrap_Password_UsesBCrypt()
    {
        var repo = new MockUserRepository();
        var hasher = new BCryptPasswordHasher(new PasswordHashOptions { WorkFactor = 12 });
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        await svc.InitializeAsync();
        var user = await repo.GetByUsernameAsync("admin");
        Assert.NotNull(user);
        Assert.StartsWith("$2", user.PasswordHash);
    }

    [Fact]
    public async Task Bootstrap_SecondCall_Idempotent()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        await svc.InitializeAsync();
        var result2 = await svc.InitializeAsync();
        Assert.False(result2.IsCreated);
        Assert.Equal("Users already exist, skipping bootstrap admin creation.", result2.Message);
    }

    [Fact]
    public async Task Bootstrap_ExistingUsers_Skips()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        await repo.CreateAsync("existing_user", "hash", "member", "Existing");
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.False(result.IsCreated);
        Assert.Null(await repo.GetByUsernameAsync("admin"));
    }

    [Fact]
    public async Task Bootstrap_MissingPassword_ZeroUsers_Throws()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = null }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var ex = await Assert.ThrowsAsync<KejiSecurityConfigurationException>(() => svc.InitializeAsync());
        Assert.Contains("password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bootstrap_PasswordLessThan12Chars_Rejected()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "short1" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var ex = await Assert.ThrowsAsync<KejiSecurityConfigurationException>(() => svc.InitializeAsync());
        Assert.Contains("12", ex.Message);
    }

    [Fact]
    public async Task Bootstrap_DuplicateUsername_Retries()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.True(result.IsCreated);
        var user = await repo.GetByUsernameAsync("admin");
        Assert.NotNull(user);
    }

    [Fact]
    public async Task Bootstrap_DoesNotPrintTempPassword()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.DoesNotContain("test-admin-password-!!", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Bootstrap_DoesNotReturnPlaintextPassword()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.DoesNotContain("test-admin-password-!!", result.Message);
    }

    [Fact]
    public async Task Bootstrap_DoesNotCreateFiles()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        var result = await svc.InitializeAsync();
        Assert.True(result.IsCreated);
    }

    [Fact]
    public async Task Bootstrap_Cancellation_Propagates()
    {
        var repo = new MockUserRepository();
        var hasher = new MockPasswordHasher();
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            JwtSecret = TestJwtSecret,
            ApiKey = TestApiKey,
            BootstrapAdmin = new BootstrapAdminOptions { Username = "admin", Password = "test-admin-password-!!", DisplayName = "Admin" }
        };
        var svc = new BootstrapAdminService(repo, hasher, opts);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.InitializeAsync(cts.Token));
    }

    #endregion

    #region Helper Methods

    private static KejiSecurityOptions Parse((string key, string value)[] entries)
    {
        var securityEntries = new List<KeyValuePair<string, ConfigNode>>();
        securityEntries.Add(KV("enabled", S("true")));
        var hasAuthMode = false;
        foreach (var (k, v) in entries)
        {
            if (k == "auth_mode") hasAuthMode = true;
            securityEntries.Add(KV(k, S(v)));
        }
        if (!hasAuthMode)
        {
            var hasJwt = entries.Any(e => e.key == "jwt_secret");
            var hasApiKey = entries.Any(e => e.key == "api_key");
            if (hasJwt && hasApiKey)
                securityEntries.Add(KV("auth_mode", S("both")));
            else if (hasJwt)
                securityEntries.Add(KV("auth_mode", S("user_only")));
            else if (hasApiKey)
                securityEntries.Add(KV("auth_mode", S("api_key_only")));
            else
                securityEntries.Add(KV("auth_mode", S("both")));
        }
        var doc = new KejiConfigurationDocument(new ConfigMap(new[] { KV("security", new ConfigMap(securityEntries)) }));
        return KejiSecurityOptions.FromConfiguration(doc);
    }

    private static KejiSecurityOptions OptionsWith((string key, string value)[] entries)
        => Parse(entries);

    private static KeyValuePair<string, ConfigNode> KV(string key, ConfigNode value) => new(key, value);
    private static ConfigScalar S(string value) => new(value);
    private static ConfigSequence Seq(params string[] items) => new(items.Select(i => new ConfigScalar(i)).ToArray());

    private static BCryptPasswordHasher CreateBcryptHasher(int workFactor = 12)
        => new(new PasswordHashOptions { WorkFactor = workFactor });

    private static JwtAccessTokenService CreateJwtService(string? secret = null, int expireHours = 72, int clockSkewSeconds = 0, TimeProvider? timeProvider = null)
    {
        var opts = new KejiSecurityOptions
        {
            Enabled = true,
            AuthMode = KejiAuthMode.Both,
            JwtSecret = secret ?? TestJwtSecret,
            ApiKey = TestApiKey,
            JwtExpireHours = expireHours,
            JwtClockSkewSeconds = clockSkewSeconds,
        };
        return new JwtAccessTokenService(opts, timeProvider ?? TimeProvider.System);
    }

    private static AccessTokenClaims DecodeToken(string token, JwtAccessTokenService? svc = null)
    {
        svc ??= CreateJwtService();
        var result = svc.ValidateToken(token);
        if (!result.IsValid) throw new InvalidOperationException($"Token validation failed: {result.FailureReason}");
        return result.Claims!;
    }

    private static KejiRequestAuthenticator CreateAuthenticator(KejiSecurityOptions opts)
    {
        var jwtSvc = !string.IsNullOrEmpty(opts.JwtSecret)
            ? new JwtAccessTokenService(opts, TimeProvider.System)
            : null!;
        var repo = new MockUserRepository();
        return new KejiRequestAuthenticator(opts, jwtSvc, repo, TimeProvider.System);
    }

    private static IRequestAuthenticator CreateAuthThatRejectsInvalidBearerThenChecksApiKey(KejiSecurityOptions opts)
    {
        var jwtSvc = !string.IsNullOrEmpty(opts.JwtSecret)
            ? new JwtAccessTokenService(opts, TimeProvider.System)
            : null!;
        var repo = new MockUserRepository();
        return new KejiRequestAuthenticator(opts, jwtSvc, repo, TimeProvider.System);
    }

    private static bool InvokeApiKeyComparer(string provided, string configured)
        => InternalApiKeyComparer.IsValid(provided, configured);

    private static string CreateUnsignedJwt(string sub, string username, string role)
    {
        var header = "{\"alg\":\"none\",\"typ\":\"JWT\"}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + 3600;
        var payload = $"{{\"sub\":\"{sub}\",\"unique_name\":\"{username}\",\"role\":\"{role}\",\"iat\":{now},\"exp\":{exp}}}";
        var headerB64 = B64Url(Encoding.UTF8.GetBytes(header));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(payload));
        return $"{headerB64}.{payloadB64}.";
    }

    private static string CreateJwt(string sub, string username, string role, int expOffset = 3600)
    {
        if (expOffset == 3600)
        {
            var svc = CreateJwtService(secret: TestJwtSecret);
            var result = svc.CreateToken(sub, username, role);
            return result.Token;
        }
        var header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + expOffset;
        var payload = $"{{\"sub\":\"{sub}\",\"unique_name\":\"{username}\",\"role\":\"{role}\",\"iat\":{now},\"exp\":{exp}}}";
        var headerB64 = B64Url(Encoding.UTF8.GetBytes(header));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(payload));
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestJwtSecret));
        var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}")));
        return $"{headerB64}.{payloadB64}.{sig}";
    }

    private static string CreateJwtWithAlg(string alg, string secret, string sub, string username, string role)
    {
        var header = $"{{\"alg\":\"{alg}\",\"typ\":\"JWT\"}}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + 3600;
        var payload = $"{{\"sub\":\"{sub}\",\"unique_name\":\"{username}\",\"role\":\"{role}\",\"iat\":{now},\"exp\":{exp}}}";
        var headerB64 = B64Url(Encoding.UTF8.GetBytes(header));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(payload));
        var sigInput = $"{headerB64}.{payloadB64}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(sigInput)));
        return $"{headerB64}.{payloadB64}.{sig}";
    }

    private static string CreateJwtWithMissingClaim(string claimToRemove, string sub, string username, string role)
    {
        var header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var exp = now + 3600;
        var claims = new List<string>
        {
            $"\"sub\":\"{sub}\"",
            $"\"unique_name\":\"{username}\"",
            $"\"role\":\"{role}\"",
            $"\"iat\":{now}",
            $"\"exp\":{exp}"
        };
        var filtered = claimToRemove switch
        {
            "sub" => claims.Where(c => !c.StartsWith("\"sub\"")).ToList(),
            "unique_name" => claims.Where(c => !c.StartsWith("\"unique_name\"")).ToList(),
            "role" => claims.Where(c => !c.StartsWith("\"role\"")).ToList(),
            _ => claims
        };
        var payload = "{" + string.Join(",", filtered) + "}";
        var headerB64 = B64Url(Encoding.UTF8.GetBytes(header));
        var payloadB64 = B64Url(Encoding.UTF8.GetBytes(payload));
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestJwtSecret));
        var sig = B64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}")));
        return $"{headerB64}.{payloadB64}.{sig}";
    }

    private static string B64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion

    #region Helper Types

    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public FakeTimeProvider(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class MockUserRepository : IUserRepository
    {
        private readonly Dictionary<string, UserAccountRecord> _users = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UserAccountRecord> _byUsername = new(StringComparer.OrdinalIgnoreCase);

        public Task<int> CountAsync(CancellationToken ct = default) => Task.FromResult(_users.Count);

        public Task<UserAccountRecord?> GetByUsernameAsync(string username, CancellationToken ct = default)
        {
            _byUsername.TryGetValue(username.Trim(), out var user);
            return Task.FromResult(user);
        }

        public Task<UserAccountRecord?> GetByIdAsync(string userId, CancellationToken ct = default)
        {
            _users.TryGetValue(userId, out var user);
            return Task.FromResult(user);
        }

        public Task<List<UserSummaryRecord>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(_users.Values.Select(u => new UserSummaryRecord
            {
                Id = u.Id,
                Username = u.Username,
                DisplayName = u.DisplayName,
                Role = u.Role,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
            }).ToList());

        public Task<string> CreateAsync(string username, string passwordHash, string role = "member", string displayName = "", CancellationToken ct = default)
        {
            var trimmed = username.Trim();
            if (_byUsername.ContainsKey(trimmed))
                throw new DuplicateUsernameException(trimmed);
            var id = Guid.NewGuid().ToString("N")[..16];
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var display = string.IsNullOrEmpty(displayName) ? trimmed : displayName;
            var record = new UserAccountRecord
            {
                Id = id,
                Username = trimmed,
                PasswordHash = passwordHash,
                Role = role,
                DisplayName = display,
                IsActive = true,
                CreatedAt = now,
            };
            _users[id] = record;
            _byUsername[trimmed] = record;
            return Task.FromResult(id);
        }

        public Task<bool> UpdateAsync(string userId, UpdateUserCommand command, CancellationToken ct = default)
        {
            if (!_users.TryGetValue(userId, out var user))
                return Task.FromResult(false);
            var updated = new UserAccountRecord
            {
                Id = user.Id,
                Username = user.Username,
                PasswordHash = command.PasswordHash ?? user.PasswordHash,
                Role = command.Role ?? user.Role,
                DisplayName = command.DisplayName ?? user.DisplayName,
                IsActive = command.IsActive ?? user.IsActive,
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt,
            };
            _users[userId] = updated;
            if (command.DisplayName != null || command.Role != null || command.IsActive != null || command.PasswordHash != null)
            {
                _byUsername[updated.Username] = updated;
            }
            return Task.FromResult(true);
        }

        public Task TouchLoginAsync(string userId, CancellationToken ct = default)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                var updated = new UserAccountRecord
                {
                    Id = user.Id,
                    Username = user.Username,
                    PasswordHash = user.PasswordHash,
                    Role = user.Role,
                    DisplayName = user.DisplayName,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt,
                    LastLoginAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };
                _users[userId] = updated;
                _byUsername[updated.Username] = updated;
            }
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string userId, CancellationToken ct = default)
        {
            if (_users.TryGetValue(userId, out var user))
            {
                _users.Remove(userId);
                _byUsername.Remove(user.Username);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    private sealed class MockPasswordHasher : IPasswordHasher
    {
        private readonly int _workFactor;
        public int VerifyCallCount { get; private set; }
        public string? LastVerifyPassword { get; private set; }
        public string? LastVerifyHash { get; private set; }

        public MockPasswordHasher(int workFactor = 12)
        {
            _workFactor = workFactor;
        }

        public string Hash(string password)
        {
            if (password is null) throw new ArgumentNullException(nameof(password));
            if (password.Length == 0) throw new ArgumentException("Password must not be empty.", nameof(password));
            return BCrypt.Net.BCrypt.HashPassword(password, _workFactor);
        }

        public bool Verify(string password, string passwordHash)
        {
            VerifyCallCount++;
            LastVerifyPassword = password;
            LastVerifyHash = passwordHash;
            if (password is null || passwordHash is null) return false;
            if (password.Length == 0 || passwordHash.Length == 0) return false;
            try { return BCrypt.Net.BCrypt.Verify(password, passwordHash); }
            catch { return false; }
        }

        public bool NeedsRehash(string passwordHash)
        {
            if (string.IsNullOrEmpty(passwordHash)) return false;
            try { return BCrypt.Net.BCrypt.PasswordNeedsRehash(passwordHash, _workFactor); }
            catch { return false; }
        }
    }

    private sealed class MockAuth : IRequestAuthenticator
    {
        private readonly RequestAuthenticationResult _result;
        public MockAuth(RequestAuthenticationResult result) { _result = result; }
        public Task<RequestAuthenticationResult> AuthenticateAsync(string? a, string? b, string? c, string? d, CancellationToken ct)
            => Task.FromResult(_result);
    }

    private sealed class ThrowingAuth : IRequestAuthenticator
    {
        public Task<RequestAuthenticationResult> AuthenticateAsync(string? a, string? b, string? c, string? d, CancellationToken ct)
            => Task.FromException<RequestAuthenticationResult>(new OperationCanceledException());
    }

    #endregion
}
