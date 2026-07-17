using System.Text;

namespace Keji.Agent;

internal sealed record KejiAgentEffectiveRunContext(
    string EffectiveRunId,
    string SafeConversationId,
    string SafeProviderName,
    string SafeModelName,
    bool RequestIsValid)
{
    private const int MaxConversationIdLength = 128;
    private const int MaxProviderNameLength = 32;
    private const int MaxModelLength = 256;
    private const int MaxUserMessageBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static KejiAgentEffectiveRunContext Create(KejiAgentRunRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var runIdIsValid = IsRunId(request.RunId);
        var conversationIsValid = IsSafeIdentifier(request.ConversationId, MaxConversationIdLength);
        var providerIsValid = IsSafeIdentifier(request.ProviderName, MaxProviderNameLength);
        var modelIsValid = IsSafeText(request.Model, MaxModelLength, allowWhitespace: false);
        var messageIsValid = IsSafeMessage(request.UserMessage);
        var controlsAreValid = request.Temperature is null or >= 0 and <= 2 &&
                               request.MaxTokens is null or >= 1 and <= 131072;

        return new KejiAgentEffectiveRunContext(
            runIdIsValid ? request.RunId : Guid.NewGuid().ToString("N"),
            conversationIsValid ? request.ConversationId : string.Empty,
            providerIsValid ? request.ProviderName : string.Empty,
            modelIsValid ? request.Model : string.Empty,
            runIdIsValid && conversationIsValid && providerIsValid && modelIsValid &&
            messageIsValid && controlsAreValid);
    }

    private static bool IsRunId(string? value) => value is { Length: 32 } &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsSafeIdentifier(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length <= maxLength && IsStrictUtf8(value) &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private static bool IsSafeText(string? value, int maxLength, bool allowWhitespace) =>
        value is not null && value.Length is > 0 && value.Length <= maxLength &&
        (allowWhitespace || !string.IsNullOrWhiteSpace(value)) &&
        !value.Any(char.IsControl) && IsStrictUtf8(value);

    private static bool IsSafeMessage(string? value)
    {
        if (value is null || string.IsNullOrWhiteSpace(value) || value.IndexOf('\0') >= 0)
            return false;
        try
        {
            return StrictUtf8.GetByteCount(value) <= MaxUserMessageBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsStrictUtf8(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }
}
