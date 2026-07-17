using System.Text;
using Keji.Persistence.Models;
using Keji.Providers;

namespace Keji.Agent;

public sealed class KejiAgentContextBuilder
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private readonly AgentLoopOptions _options;

    public KejiAgentContextBuilder(AgentLoopOptions options) =>
        _options = options ?? throw new ArgumentNullException(nameof(options));

    public bool TryBuild(IReadOnlyList<MessageRecord> history, string userMessage, out List<ChatMessage> context)
    {
        ArgumentNullException.ThrowIfNull(history);
        context = new List<ChatMessage>(history.Count + 2)
        {
            new() { Role = KejiChatRole.System, Content = _options.SystemPrompt },
        };
        if (history.Count + 2 > _options.MaxContextMessages)
            return false;
        int bytes;
        try { bytes = checked(StrictUtf8.GetByteCount(_options.SystemPrompt) + StrictUtf8.GetByteCount(userMessage)); }
        catch (EncoderFallbackException) { return false; }
        if (bytes > _options.MaxContextBytes)
            return false;
        foreach (var record in history)
        {
            var role = record.Role switch
            {
                "user" => KejiChatRole.User,
                "assistant" => KejiChatRole.Assistant,
                _ => KejiChatRole.Invalid,
            };
            if (role == KejiChatRole.Invalid || record.Content is null)
                return false;
            try { bytes = checked(bytes + StrictUtf8.GetByteCount(record.Content)); }
            catch (EncoderFallbackException) { return false; }
            if (bytes > _options.MaxContextBytes)
                return false;
            context.Add(new ChatMessage { Role = role, Content = record.Content });
        }
        context.Add(new ChatMessage { Role = KejiChatRole.User, Content = userMessage });
        return true;
    }
}
