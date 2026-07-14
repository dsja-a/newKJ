using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IMessageRepository
{
    Task<long> AddAsync(string conversationId, string role, string content, string? ownerUserId = null, CancellationToken cancellationToken = default);
    Task<List<MessageRecord>> ListByConversationAsync(string conversationId, int limit = 100, string? ownerUserId = null, CancellationToken cancellationToken = default);
}
