using Keji.Security.Models;

namespace Keji.Security.Services;

public interface IKejiLoginService
{
    Task<LoginResult> LoginAsync(string username, string password, CancellationToken cancellationToken = default);
}
