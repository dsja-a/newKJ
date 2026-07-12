using Keji.Persistence;
using Keji.Security.Options;
using Keji.Security.Services;

namespace Keji.Api.HostedServices;

public class KejiStartupInitializer : IHostedService
{
    private readonly IKejiDatabaseInitializer _dbInitializer;
    private readonly IBootstrapAdminService _bootstrapAdmin;
    private readonly KejiSecurityOptions _securityOptions;

    public KejiStartupInitializer(
        IKejiDatabaseInitializer dbInitializer,
        IBootstrapAdminService bootstrapAdmin,
        KejiSecurityOptions securityOptions)
    {
        _dbInitializer = dbInitializer;
        _bootstrapAdmin = bootstrapAdmin;
        _securityOptions = securityOptions;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _dbInitializer.InitializeAsync(cancellationToken);

        if (_securityOptions.Enabled)
        {
            await _bootstrapAdmin.InitializeAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
