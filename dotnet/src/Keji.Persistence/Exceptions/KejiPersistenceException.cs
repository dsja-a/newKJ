namespace Keji.Persistence;

public class KejiPersistenceException : Exception
{
    public KejiPersistenceException(string message) : base(message) { }
    public KejiPersistenceException(string message, Exception inner) : base(message, inner) { }
}

public class DuplicateUsernameException : KejiPersistenceException
{
    public string Username { get; }

    public DuplicateUsernameException(string username)
        : base($"Username '{username}' already exists.")
    {
        Username = username;
    }
}

public class ConversationOwnershipException : KejiPersistenceException
{
    public string ConversationId { get; }
    public string UserId { get; }

    public ConversationOwnershipException(string conversationId, string userId)
        : base($"Conversation '{conversationId}' is owned by another user.")
    {
        ConversationId = conversationId;
        UserId = userId;
    }
}
