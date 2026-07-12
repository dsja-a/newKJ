namespace Keji.Persistence.Models;

public class MessageRecord
{
    public long Id { get; init; }
    public string ConversationId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public double CreatedAt { get; init; }
}
