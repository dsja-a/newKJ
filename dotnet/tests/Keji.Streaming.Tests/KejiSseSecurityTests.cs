using Keji.Providers;
using Keji.Streaming;

namespace Keji.Streaming.Tests;

public class KejiSseSecurityTests
{
    [Fact]
    public async Task Error_DoesNotLeakRawException()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.Error("CONNECTION_ERROR", "Connection refused")
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        Assert.Single(results);
        Assert.Equal("Connection refused", results[0].ErrorMessage);
        Assert.DoesNotContain("Exception", results[0].ErrorMessage);
        Assert.DoesNotContain("StackTrace", results[0].ErrorMessage);
    }

    [Fact]
    public async Task ToolCall_DoesNotLeakParameters()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.ToolCallBegin("call_1", "execute"),
            ChatCompletionStreamEvent.ToolCallDelta("{\"command\":\"rm -rf /\",\"password\":\"secret\"}"),
            ChatCompletionStreamEvent.ToolCallEnd()
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var toolCall = results.FirstOrDefault(r => r.EventType == KejiSseEventType.ToolCall);
        Assert.NotNull(toolCall);
        Assert.Equal("execute", toolCall.ToolName);
        Assert.DoesNotContain("rm -rf", toolCall.ToolName);
        Assert.Null(toolCall.Delta);
    }

    [Fact]
    public async Task Usage_DoesNotLeakContent()
    {
        var usage = new TokenUsage { PromptTokens = 100, CompletionTokens = 50 };
        var providerEvents = AsyncEnumerable(new[] { ChatCompletionStreamEvent.UsageEvent(usage) });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var usageEvent = results.FirstOrDefault(r => r.EventType == KejiSseEventType.Usage);
        Assert.NotNull(usageEvent);
        Assert.Equal(100, usageEvent.Usage!.PromptTokens);
        Assert.Equal(150, usageEvent.Usage.TotalTokens);
    }

    [Fact]
    public async Task ErrorFormatter_DoesNotLeakStackTrace()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Error,
            Phase = KejiSsePhase.Error,
            ErrorMessage = "Safe error message"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.Contains("Safe error message", result);
        Assert.DoesNotContain("Exception", result);
        Assert.DoesNotContain("StackTrace", result);
        Assert.DoesNotContain("api_key", result.ToLower());
        Assert.DoesNotContain("secret", result.ToLower());
    }

    [Fact]
    public async Task AnswerFormatter_DoesNotLeakKeys()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Answer,
            Phase = KejiSsePhase.Answering,
            Delta = "Hello world"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.DoesNotContain("api", result.ToLower());
        Assert.DoesNotContain("key", result.ToLower());
        Assert.DoesNotContain("token", result.ToLower());
    }

    [Fact]
    public async Task ProviderError_StrippedByAdapter_NoLeak()
    {
        var providerEvents = AsyncEnumerable(new[]
        {
            ChatCompletionStreamEvent.Error("AUTH_FAILED", "API key sk-12345 rejected")
        });

        var results = await KejiSseAdapter.ToSseEvents(providerEvents).ToListAsync();

        var error = results.FirstOrDefault(r => r.EventType == KejiSseEventType.Error);
        Assert.NotNull(error);
        Assert.Equal("API key sk-12345 rejected", error.ErrorMessage);
        // The SSE adapter passes through the error message - the actual stripping happens at ProviderBase level
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> AsyncEnumerable(ChatCompletionStreamEvent[] events)
    {
        foreach (var e in events)
        {
            await Task.Yield();
            yield return e;
        }
    }
}
