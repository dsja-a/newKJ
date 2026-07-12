namespace Keji.Persistence.Models;

public class ConversationRecord
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = "新对话";
    public double CreatedAt { get; init; }
    public double UpdatedAt { get; init; }
    public int MessageCount { get; init; }
    public string? OwnerUserId { get; init; }
}

public enum ConversationOwnershipResult
{
    Created,
    AlreadyOwned,
    ClaimedUnowned,
    OwnedByAnotherUser,
}
