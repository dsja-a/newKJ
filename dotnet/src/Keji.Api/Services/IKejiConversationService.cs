using Keji.Persistence.Models;

namespace Keji.Api.Services;

public interface IKejiConversationService
{
    Task<ConversationRecord> CreateConversationAsync(string convId, string title = "新对话", CancellationToken cancellationToken = default);
    Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureConversationAsync(string convId, string title = "新对话", CancellationToken cancellationToken = default);
    Task<ConversationRecord?> GetConversationAsync(string convId, CancellationToken cancellationToken = default);
    Task<List<ConversationRecord>> ListConversationsAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<bool> RenameConversationAsync(string convId, string title, CancellationToken cancellationToken = default);
    Task<bool> DeleteConversationAsync(string convId, CancellationToken cancellationToken = default);
    Task<int> CountConversationsAsync(CancellationToken cancellationToken = default);
    Task<long> AddMessageAsync(string conversationId, string role, string content, CancellationToken cancellationToken = default);
    Task<List<MessageRecord>> ListMessagesAsync(string conversationId, int limit = 100, CancellationToken cancellationToken = default);
}
