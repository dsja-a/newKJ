using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IConversationRepository
{
    Task<ConversationRecord> CreateOwnedAsync(string convId, string ownerUserId, string title = "新对话", CancellationToken cancellationToken = default);
    Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(string convId, string ownerUserId, string title = "新对话", CancellationToken cancellationToken = default);
    Task<ConversationRecord?> GetOwnedAsync(string convId, string ownerUserId, CancellationToken cancellationToken = default);
    Task<List<ConversationRecord>> ListOwnedAsync(string ownerUserId, int limit = 50, CancellationToken cancellationToken = default);
    Task<bool> RenameOwnedAsync(string convId, string ownerUserId, string title, CancellationToken cancellationToken = default);
    Task<bool> DeleteOwnedAsync(string convId, string ownerUserId, CancellationToken cancellationToken = default);
    Task<int> CountByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default);
}
