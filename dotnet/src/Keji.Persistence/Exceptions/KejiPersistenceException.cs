namespace Keji.Persistence;

public class KejiPersistenceException : Exception
{
    public int ErrorCode { get; }

    public KejiPersistenceException(string message) : base(message) { ErrorCode = 0; }
    public KejiPersistenceException(string message, int errorCode) : base(message) { ErrorCode = errorCode; }
}

public class ConversationNotFoundException : KejiPersistenceException
{
    public ConversationNotFoundException(string conversationId)
        : base("Conversation not found.")
    {
    }
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


