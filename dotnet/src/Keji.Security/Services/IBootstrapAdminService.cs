using Keji.Security.Models;

namespace Keji.Security.Services;

public interface IBootstrapAdminService
{
    Task<BootstrapAdminResult> InitializeAsync(CancellationToken cancellationToken = default);
}
