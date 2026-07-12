using Keji.Persistence.Models;

namespace Keji.Persistence.Repositories;

public interface IMessageRepository
{
    Task<long> AddAsync(string conversationId, string role, string content, double timestamp, CancellationToken cancellationToken = default);
    Task<List<MessageRecord>> ListByConversationAsync(string conversationId, int limit = 100, CancellationToken cancellationToken = default);
}
