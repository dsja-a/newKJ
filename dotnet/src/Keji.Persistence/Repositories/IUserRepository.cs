using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IUserRepository
{
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<UserAccountRecord?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<UserAccountRecord?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<List<UserSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);
    Task<string> CreateAsync(string username, string passwordHash, string role = "member", string displayName = "", CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(string userId, UpdateUserCommand command, CancellationToken cancellationToken = default);
    Task TouchLoginAsync(string userId, double timestamp, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string userId, CancellationToken cancellationToken = default);
}
