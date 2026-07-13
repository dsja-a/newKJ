using Keji.Persistence;
using Keji.Persistence.Repositories;
using Keji.Security.Auth;
using Keji.Security.Authentication;
using Keji.Security.Models;
using Keji.Security.Options;
using Keji.Security.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Keji.Integration.Tests.Authorization;

public sealed class AuthorizationIntegrationFixture : IAsyncLifetime
{
    public const string ApiKey = "task-006-test-api-key-that-is-at-least-32-bytes!!";

    private const string JwtSecret = "task-006-test-jwt-secret-that-is-at-least-32-bytes!!";
    private const string AdminPassword = "task-006-test-admin-password!!";
    private const string EnvJwt = "TEST_TASK006_JWT_SECRET";
    private const string EnvApiKey = "TEST_TASK006_API_KEY";
    private const string EnvAdminPassword = "TEST_TASK006_ADMIN_PASSWORD";

    private readonly Dictionary<string, string?> _savedEnvironment = new(StringComparer.Ordinal);
    private readonly List<WebApplicationFactory<Program>> _factories = new();
    private readonly List<HttpClient> _clients = new();
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), $"keji_task006_authorization_{Guid.NewGuid():N}");

    public WebApplicationFactory<Program> DefaultFactory { get; private set; } = null!;
    public HttpClient DefaultClient { get; private set; } = null!;
    public string AdminToken { get; private set; } = string.Empty;
    public string MemberToken { get; private set; } = string.Empty;
    public string ReadonlyToken { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempRoot);
        SaveAndSetEnvironment(EnvJwt, JwtSecret);
        SaveAndSetEnvironment(EnvApiKey, ApiKey);
        SaveAndSetEnvironment(EnvAdminPassword, AdminPassword);

        (DefaultFactory, DefaultClient) = CreateHost("default", DefaultConfig());

        using var scope = DefaultFactory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var tokens = scope.ServiceProvider.GetRequiredService<IAccessTokenService>();

        var admin = await users.GetByUsernameAsync("admin");
        Assert.NotNull(admin);

        var memberId = await users.CreateAsync(
            "task006-member", "unused-test-password-hash", "member", "TASK-006 Member");
        var readonlyId = await users.CreateAsync(
            "task006-readonly", "unused-test-password-hash", "readonly", "TASK-006 Readonly");

        AdminToken = tokens.CreateToken(admin.Id, admin.Username, admin.Role).Token;
        MemberToken = tokens.CreateToken(memberId, "task006-member", "member").Token;
        ReadonlyToken = tokens.CreateToken(readonlyId, "task006-readonly", "readonly").Token;
    }

    public Task DisposeAsync()
    {
        foreach (var client in _clients)
            client.Dispose();

        foreach (var factory in _factories)
            factory.Dispose();

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        foreach (var (name, value) in _savedEnvironment)
            Environment.SetEnvironmentVariable(name, value);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);

        return Task.CompletedTask;
    }

    public (WebApplicationFactory<Program> Factory, HttpClient Client) CreateEnabledFalseHost()
        => CreateHost("enabled-false", EnabledFalseConfig());

    public (WebApplicationFactory<Program> Factory, HttpClient Client) CreateNullCurrentUserHost()
        => CreateHost("null-current-user", DefaultConfig(), replaceCurrentUserWithNull: true);

    public (WebApplicationFactory<Program> Factory, HttpClient Client) CreateLocalhostHost()
        => CreateHost("localhost", LocalhostConfig(), forceLoopbackRemoteIp: true);

    public static HttpRequestMessage BearerRequest(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private (WebApplicationFactory<Program> Factory, HttpClient Client) CreateHost(
        string name,
        string config,
        bool replaceCurrentUserWithNull = false,
        bool forceLoopbackRemoteIp = false)
    {
        var projectRoot = Path.Combine(_tempRoot, $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "config.yaml"), config);

        var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Keji:ProjectRoot", projectRoot);
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<KejiPersistenceOptions>();
                    services.AddSingleton(new KejiPersistenceOptions
                    {
                        ProjectRoot = projectRoot,
                        DatabasePath = Path.Combine(projectRoot, "test.db"),
                        BusyTimeoutMilliseconds = 5000,
                        EnableWal = true,
                        EnableForeignKeys = true,
                        CreateDirectoryIfMissing = true,
                    });

                    services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);

                    if (replaceCurrentUserWithNull)
                    {
                        services.RemoveAll<ICurrentUserAccessor>();
                        services.AddScoped<ICurrentUserAccessor, NullCurrentUserAccessor>();
                    }

                    if (forceLoopbackRemoteIp)
                    {
                        services.RemoveAll<IRequestAuthenticator>();
                        services.AddSingleton<IRequestAuthenticator>(provider =>
                        {
                            var inner = new KejiRequestAuthenticator(
                                provider.GetRequiredService<KejiSecurityOptions>(),
                                provider.GetRequiredService<IAccessTokenService>(),
                                provider.GetRequiredService<IUserRepository>(),
                                provider.GetRequiredService<TimeProvider>());
                            return new LoopbackRequestAuthenticator(inner);
                        });
                    }
                });
            });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        _factories.Add(factory);
        _clients.Add(client);
        return (factory, client);
    }

    private void SaveAndSetEnvironment(string name, string value)
    {
        _savedEnvironment.Add(name, Environment.GetEnvironmentVariable(name));
        Environment.SetEnvironmentVariable(name, value);
    }

    private static string DefaultConfig() => """
        database:
          path: test.db
        security:
          auth_mode: both
          jwt_secret: ${TEST_TASK006_JWT_SECRET}
          api_key: ${TEST_TASK006_API_KEY}
          bootstrap_admin:
            username: admin
            password: ${TEST_TASK006_ADMIN_PASSWORD}
            display_name: TASK-006 Admin
        """;

    private static string LocalhostConfig() => """
        database:
          path: test.db
        security:
          auth_mode: both
          jwt_secret: ${TEST_TASK006_JWT_SECRET}
          api_key: ${TEST_TASK006_API_KEY}
          allow_localhost_without_auth: true
          bootstrap_admin:
            username: admin
            password: ${TEST_TASK006_ADMIN_PASSWORD}
            display_name: TASK-006 Admin
        """;

    private static string EnabledFalseConfig() => """
        database:
          path: test.db
        security:
          enabled: false
        """;

    private sealed class NullCurrentUserAccessor : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser => null;
    }

    private sealed class LoopbackRequestAuthenticator : IRequestAuthenticator
    {
        private readonly IRequestAuthenticator _inner;

        public LoopbackRequestAuthenticator(IRequestAuthenticator inner)
        {
            _inner = inner;
        }

        public Task<RequestAuthenticationResult> AuthenticateAsync(
            string? authorizationHeader,
            string? xApiKeyHeader,
            string? queryApiKey,
            string? remoteIp,
            CancellationToken cancellationToken = default)
        {
            return _inner.AuthenticateAsync(
                authorizationHeader,
                xApiKeyHeader,
                queryApiKey,
                "127.0.0.1",
                cancellationToken);
        }
    }
}
