using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IMessageRepository
{
    Task<long> AddOwnedAsync(string conversationId, string ownerUserId, string role, string content, CancellationToken cancellationToken = default);
    Task<List<MessageRecord>> ListOwnedMessagesAsync(string conversationId, string ownerUserId, int limit = 100, CancellationToken cancellationToken = default);
}
