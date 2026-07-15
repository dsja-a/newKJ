using Keji.Streaming;

namespace Keji.Streaming.Tests;

public class KejiSseFormatterTests
{
    [Fact]
    public void ThinkToken_FormatsCorrectly()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.ThinkToken,
            Phase = KejiSsePhase.Thinking,
            Delta = "thinking text"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: think_token", result);
        Assert.Contains("\"phase\":\"thinking\"", result);
        Assert.Contains("\"delta\":\"thinking text\"", result);
        Assert.EndsWith("\n\n", result);
    }

    [Fact]
    public void Answer_FormatsCorrectly()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Answer,
            Phase = KejiSsePhase.Answering,
            Delta = "answer text"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: answer", result);
        Assert.Contains("\"phase\":\"answering\"", result);
        Assert.Contains("\"delta\":\"answer text\"", result);
    }

    [Fact]
    public void Answering_FormatsCorrectly()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Answering,
            Phase = KejiSsePhase.Answering
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: answering", result);
        Assert.Contains("\"phase\":\"answering\"", result);
        Assert.DoesNotContain("\"delta\"", result);
    }

    [Fact]
    public void ToolCall_FormatsWithToolName()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.ToolCall,
            Phase = KejiSsePhase.Answering,
            ToolName = "read_file"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: tool_call", result);
        Assert.Contains("\"tool\":\"read_file\"", result);
        Assert.DoesNotContain("\"arguments\"", result);
    }

    [Fact]
    public void ToolCall_DoesNotIncludeArgs()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.ToolCall,
            Phase = KejiSsePhase.Answering,
            ToolName = "search",
            Delta = "should not appear"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.Contains("\"tool\":\"search\"", result);
        Assert.DoesNotContain("arguments", result.ToLower());
    }

    [Fact]
    public void Usage_FormatsWithTokenCounts()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Usage,
            Phase = KejiSsePhase.Done,
            Usage = new Keji.Providers.TokenUsage { PromptTokens = 10, CompletionTokens = 20 }
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: usage", result);
        Assert.Contains("\"phase\":\"done\"", result);
        Assert.Contains("\"usage\"", result);
    }

    [Fact]
    public void Error_FormatsWithErrorMessage()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Error,
            Phase = KejiSsePhase.Error,
            ErrorMessage = "Something went wrong"
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: error", result);
        Assert.Contains("\"phase\":\"error\"", result);
        Assert.Contains("\"error\":\"Something went wrong\"", result);
    }

    [Fact]
    public void Done_FormatsCorrectly()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Done,
            Phase = KejiSsePhase.Done
        };

        var result = KejiSseFormatter.FormatEvent(evt);

        Assert.StartsWith("event: done", result);
        Assert.Contains("\"phase\":\"done\"", result);
    }

    [Fact]
    public void Thinking_FormatsCorrectly()
    {
        var result = KejiSseFormatter.FormatThinkingPhaseStart();

        Assert.StartsWith("event: thinking", result);
        Assert.Contains("\"phase\":\"thinking\"", result);
    }

    [Fact]
    public void FormattedEvent_HasTwoNewlines()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Done,
            Phase = KejiSsePhase.Done
        };

        var result = KejiSseFormatter.FormatEvent(evt);
        Assert.EndsWith("\n\n", result);
    }

    [Fact]
    public void FormattedEvent_HasEventAndDataLines()
    {
        var evt = new KejiSseEvent
        {
            EventType = KejiSseEventType.Answer,
            Phase = KejiSsePhase.Answering,
            Delta = "test"
        };

        var result = KejiSseFormatter.FormatEvent(evt);
        var lines = result.Split('\n');
        Assert.StartsWith("event: ", lines[0]);
        Assert.StartsWith("data: ", lines[1]);
    }

    [Fact]
    public void MultipleAnswers_AllFormatted()
    {
        var evts = new[]
        {
            new KejiSseEvent { EventType = KejiSseEventType.Answer, Phase = KejiSsePhase.Answering, Delta = "Hello" },
            new KejiSseEvent { EventType = KejiSseEventType.Answer, Phase = KejiSsePhase.Answering, Delta = " World" },
            new KejiSseEvent { EventType = KejiSseEventType.Done, Phase = KejiSsePhase.Done }
        };

        var result = string.Join("", evts.Select(KejiSseFormatter.FormatEvent));
        var parts = result.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, parts.Length);
    }

    [Fact]
    public void PhaseField_AlwaysPresent()
    {
        foreach (var eventType in Enum.GetValues<KejiSseEventType>())
        {
            var evt = new KejiSseEvent
            {
                EventType = eventType,
                Phase = eventType switch
                {
                    KejiSseEventType.Thinking or KejiSseEventType.ThinkToken => KejiSsePhase.Thinking,
                    KejiSseEventType.Answering or KejiSseEventType.Answer or KejiSseEventType.ToolCall => KejiSsePhase.Answering,
                    KejiSseEventType.Usage or KejiSseEventType.Done => KejiSsePhase.Done,
                    KejiSseEventType.Error => KejiSsePhase.Error,
                    _ => KejiSsePhase.Done
                }
            };

            var result = KejiSseFormatter.FormatEvent(evt);
            Assert.Contains("\"phase\"", result);
        }
    }
}
