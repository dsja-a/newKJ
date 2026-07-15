using System.Text.Json;

namespace Keji.Streaming;

public static class KejiSseFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static string FormatEvent(KejiSseEvent evt)
    {
        var eventName = evt.EventType switch
        {
            KejiSseEventType.Thinking => "thinking",
            KejiSseEventType.ThinkToken => "think_token",
            KejiSseEventType.Answering => "answering",
            KejiSseEventType.Answer => "answer",
            KejiSseEventType.ToolCall => "tool_call",
            KejiSseEventType.Usage => "usage",
            KejiSseEventType.Error => "error",
            KejiSseEventType.Done => "done",
            _ => "unknown"
        };

        var phaseName = evt.Phase switch
        {
            KejiSsePhase.Thinking => "thinking",
            KejiSsePhase.Answering => "answering",
            KejiSsePhase.Done => "done",
            KejiSsePhase.Error => "error",
            _ => "unknown"
        };

        var dataObj = new Dictionary<string, object?>
        {
            ["phase"] = phaseName
        };

        if (evt.Delta is not null)
            dataObj["delta"] = evt.Delta;

        if (evt.ToolName is not null)
            dataObj["tool"] = evt.ToolName;

        if (evt.Usage is not null)
            dataObj["usage"] = new
            {
                promptTokens = evt.Usage.PromptTokens,
                completionTokens = evt.Usage.CompletionTokens,
                totalTokens = evt.Usage.TotalTokens
            };

        if (evt.ErrorMessage is not null)
            dataObj["error"] = evt.ErrorMessage;

        var json = JsonSerializer.Serialize(dataObj, JsonOptions);

        return $"event: {eventName}\ndata: {json}\n\n";
    }

    public static string FormatThinkingPhaseStart()
    {
        return "event: thinking\ndata: {\"phase\":\"thinking\"}\n\n";
    }
}
