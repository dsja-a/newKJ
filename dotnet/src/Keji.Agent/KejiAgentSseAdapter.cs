using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Keji.Agent;

public sealed class KejiAgentSseAdapter
{
    public async IAsyncEnumerable<string> AdaptAsync(
        IAsyncEnumerable<KejiAgentEvent> events,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(events);
        await foreach (var item in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            Validate(item);
            var eventName = item.Type switch
            {
                KejiAgentEventType.RunStarted => "agent_run_started",
                KejiAgentEventType.IterationStarted => "agent_iteration_started",
                KejiAgentEventType.AssistantDelta => "agent_delta",
                KejiAgentEventType.ToolStarted => "agent_tool_started",
                KejiAgentEventType.ToolCompleted => "agent_tool_completed",
                KejiAgentEventType.Usage => "agent_usage",
                KejiAgentEventType.RunCompleted => "agent_done",
                KejiAgentEventType.Error => "agent_error",
                _ => throw new ArgumentException("Agent event type is invalid.", nameof(events)),
            };
            var payload = JsonSerializer.Serialize(new
            {
                protocol_version = 1,
                run_id = item.RunId,
                sequence = item.Sequence,
                timestamp_utc = item.TimestampUtc,
                type = eventName,
                iteration = item.Iteration == 0 ? (int?)null : item.Iteration,
                content_delta = item.ContentDelta,
                tool_call_id = item.ToolCallId,
                tool_name = item.ToolName,
                tool_succeeded = item.ToolSucceeded,
                tool_error_code = item.ToolErrorCode,
                usage = item.Usage,
                stop_reason = item.StopReason == KejiAgentStopReason.None ? null : item.StopReason.ToString(),
                error_code = item.ErrorCode == KejiAgentErrorCode.None ? null : item.ErrorCode.ToString(),
                transcript = item.Transcript,
            });
            yield return $"id: {item.RunId}-{item.Sequence:x}\nevent: {eventName}\ndata: {payload}\n\n";
        }
    }

    private static void Validate(KejiAgentEvent item)
    {
        if (item.Sequence < 1 || item.RunId.Length != 32 ||
            !item.RunId.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            item.TimestampUtc.Offset != TimeSpan.Zero)
            throw new ArgumentException("Agent event is invalid.", nameof(item));
    }
}
