using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IConversationRepository
{
    Task<ConversationRecord> CreateAsync(string convId, string title = "新对话", string? ownerUserId = null, CancellationToken cancellationToken = default);
    Task<(ConversationRecord? Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(string convId, string ownerUserId, string title = "新对话", CancellationToken cancellationToken = default);
    Task<ConversationRecord?> GetAsync(string convId, CancellationToken cancellationToken = default);
    Task<List<ConversationRecord>> ListAsync(int limit = 50, string? ownerUserId = null, CancellationToken cancellationToken = default);
    Task<bool> RenameAsync(string convId, string title, double timestamp, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string convId, CancellationToken cancellationToken = default);
}
