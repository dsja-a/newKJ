using System.Text.Json;

namespace Keji.Agent.Tests;

public sealed class KejiAgentContractTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(1, 2, 3)]
    [InlineData(10, 20, 5)]
    [InlineData(100, 200, 50)]
    [InlineData(1000, 2000, 500)]
    [InlineData(7, 11, 13)]
    [InlineData(99, 1, 2)]
    [InlineData(123, 456, 78)]
    [InlineData(4096, 8192, 1024)]
    [InlineData(31, 37, 41)]
    [InlineData(2, 3, 5)]
    [InlineData(13, 17, 19)]
    [InlineData(9999, 8888, 7777)]
    [InlineData(64, 128, 32)]
    [InlineData(256, 512, 128)]
    [InlineData(3, 0, 2)]
    [InlineData(0, 3, 2)]
    public void Usage_AddAccumulatesAllNonNegativeCounters(long prompt, long completion, long cached)
    {
        var usage = new KejiAgentUsage(5, 7, 3).Add(prompt, completion, cached);

        Assert.Equal(5 + prompt, usage.PromptTokens);
        Assert.Equal(7 + completion, usage.CompletionTokens);
        Assert.Equal(3 + cached, usage.CachedTokens);
        Assert.Equal(12 + prompt + completion, usage.TotalTokens);
    }

    [Theory]
    [InlineData(KejiAgentEventType.RunStarted, 1)]
    [InlineData(KejiAgentEventType.IterationStarted, 2)]
    [InlineData(KejiAgentEventType.AssistantDelta, 3)]
    [InlineData(KejiAgentEventType.ToolStarted, 4)]
    [InlineData(KejiAgentEventType.ToolCompleted, 5)]
    [InlineData(KejiAgentEventType.Usage, 6)]
    [InlineData(KejiAgentEventType.RunCompleted, 7)]
    [InlineData(KejiAgentEventType.Error, 8)]
    public void EventTypeWireValuesAreStable(KejiAgentEventType value, int expected) =>
        Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(KejiAgentStopReason.None, 0)]
    [InlineData(KejiAgentStopReason.Completed, 1)]
    [InlineData(KejiAgentStopReason.Length, 2)]
    [InlineData(KejiAgentStopReason.ContentFiltered, 3)]
    [InlineData(KejiAgentStopReason.IterationLimit, 4)]
    [InlineData(KejiAgentStopReason.ToolCallLimit, 5)]
    [InlineData(KejiAgentStopReason.ContextLimit, 6)]
    [InlineData(KejiAgentStopReason.Cancelled, 7)]
    [InlineData(KejiAgentStopReason.TimedOut, 8)]
    [InlineData(KejiAgentStopReason.Failed, 9)]
    public void StopReasonWireValuesAreStable(KejiAgentStopReason value, int expected) =>
        Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(KejiAgentErrorCode.None, 0)]
    [InlineData(KejiAgentErrorCode.InvalidRequest, 1)]
    [InlineData(KejiAgentErrorCode.Unauthenticated, 2)]
    [InlineData(KejiAgentErrorCode.ConversationNotFound, 3)]
    [InlineData(KejiAgentErrorCode.ProviderNotFound, 4)]
    [InlineData(KejiAgentErrorCode.ProviderTimeout, 5)]
    [InlineData(KejiAgentErrorCode.ProviderRejected, 6)]
    [InlineData(KejiAgentErrorCode.ProviderUnavailable, 7)]
    [InlineData(KejiAgentErrorCode.ProviderProtocolError, 8)]
    [InlineData(KejiAgentErrorCode.ToolRejected, 9)]
    [InlineData(KejiAgentErrorCode.ToolFailed, 10)]
    [InlineData(KejiAgentErrorCode.LimitExceeded, 11)]
    [InlineData(KejiAgentErrorCode.SessionBusy, 12)]
    [InlineData(KejiAgentErrorCode.PersistenceFailed, 13)]
    [InlineData(KejiAgentErrorCode.AuditFailed, 14)]
    [InlineData(KejiAgentErrorCode.InternalFailure, 15)]
    [InlineData(KejiAgentErrorCode.ContextLimit, 16)]
    [InlineData(KejiAgentErrorCode.ToolCallLimit, 17)]
    [InlineData(KejiAgentErrorCode.RunTimedOut, 18)]
    public void ErrorCodeWireValuesAreStable(KejiAgentErrorCode value, int expected) =>
        Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(96)]
    [InlineData(128)]
    [InlineData(160)]
    [InlineData(192)]
    [InlineData(224)]
    [InlineData(256)]
    public void OptionsAcceptOnlyBoundedEventBufferCapacities(int capacity)
    {
        var options = new AgentLoopOptions(eventBufferCapacity: capacity);
        Assert.Equal(capacity, options.EventBufferCapacity);
    }

    [Theory]
    [InlineData(KejiAgentEventType.RunStarted, "agent_run_started")]
    [InlineData(KejiAgentEventType.IterationStarted, "agent_iteration_started")]
    [InlineData(KejiAgentEventType.AssistantDelta, "agent_delta")]
    [InlineData(KejiAgentEventType.ToolStarted, "agent_tool_started")]
    [InlineData(KejiAgentEventType.ToolCompleted, "agent_tool_completed")]
    [InlineData(KejiAgentEventType.Usage, "agent_usage")]
    [InlineData(KejiAgentEventType.RunCompleted, "agent_done")]
    [InlineData(KejiAgentEventType.Error, "agent_error")]
    public async Task SseAdapterEmitsVersionedBoundedFrame(KejiAgentEventType type, string eventName)
    {
        var frame = Assert.Single(await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(OneEvent(type))));

        Assert.Contains($"event: {eventName}\n", frame, StringComparison.Ordinal);
        Assert.Contains("\"protocol_version\":1", frame, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", frame, StringComparison.Ordinal);
        var data = frame.Split('\n').Single(static line => line.StartsWith("data: ", StringComparison.Ordinal))[6..];
        using var document = JsonDocument.Parse(data);
        Assert.Equal(1, document.RootElement.GetProperty("sequence").GetInt64());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("g123456789abcdef0123456789abcdef")]
    [InlineData("-123456789abcdef0123456789abcdef")]
    [InlineData(" 123456789abcdef0123456789abcdef")]
    [InlineData("0123456789abcdef0123456789abcde ")]
    [InlineData("0000000000000000000000000000000z")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("0123456789abcdef\n123456789abcde")]
    [InlineData("0123456789abcdef\t123456789abcde")]
    [InlineData("😀😀😀😀😀😀😀😀")]
    public async Task SseAdapterRejectsInvalidRunId(string runId)
    {
        var adapter = new KejiAgentSseAdapter();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await CollectAsync(adapter.AdaptAsync(OneInvalidEvent(runId))));
    }

    [Theory]
    [InlineData(nameof(KejiAgentRunRequest.ConversationId), typeof(string))]
    [InlineData(nameof(KejiAgentRunRequest.ProviderName), typeof(string))]
    [InlineData(nameof(KejiAgentRunRequest.Model), typeof(string))]
    [InlineData(nameof(KejiAgentRunRequest.UserMessage), typeof(string))]
    [InlineData(nameof(KejiAgentRunRequest.Temperature), typeof(double?))]
    [InlineData(nameof(KejiAgentRunRequest.MaxTokens), typeof(int?))]
    public void RunRequestContractIsStronglyTyped(string propertyName, Type expectedType) =>
        Assert.Equal(expectedType, typeof(KejiAgentRunRequest).GetProperty(propertyName)!.PropertyType);

    private static async IAsyncEnumerable<KejiAgentEvent> OneEvent(KejiAgentEventType type)
    {
        yield return new KejiAgentEvent
        {
            RunId = "0123456789abcdef0123456789abcdef",
            Sequence = 1,
            Type = type,
            TimestampUtc = DateTimeOffset.UnixEpoch,
        };
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<KejiAgentEvent> OneInvalidEvent(string runId)
    {
        yield return new KejiAgentEvent
        {
            RunId = runId,
            Sequence = 1,
            Type = KejiAgentEventType.RunStarted,
            TimestampUtc = DateTimeOffset.UnixEpoch,
        };
        await Task.CompletedTask;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source) result.Add(item);
        return result;
    }
}
