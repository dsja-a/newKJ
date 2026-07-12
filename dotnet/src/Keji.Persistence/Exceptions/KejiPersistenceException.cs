namespace Keji.Persistence;

public class KejiPersistenceException : Exception
{
    public int ErrorCode { get; }

    public KejiPersistenceException(string message) : base(message) { ErrorCode = 0; }
    public KejiPersistenceException(string message, int errorCode) : base(message) { ErrorCode = errorCode; }
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
