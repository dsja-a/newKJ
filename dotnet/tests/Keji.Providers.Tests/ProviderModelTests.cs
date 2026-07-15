using Keji.Providers;

namespace Keji.Providers.Tests;

public class ProviderModelTests
{
    [Fact]
    public void ChatCompletionResponse_Succeeded_SetsSuccess()
    {
        var resp = ChatCompletionResponse.Succeeded("hello");
        Assert.True(resp.Success);
        Assert.Equal("hello", resp.Content);
    }

    [Fact]
    public void ChatCompletionResponse_Failed_SetsError()
    {
        var resp = ChatCompletionResponse.Failed("ERR", "msg");
        Assert.False(resp.Success);
        Assert.Equal("ERR", resp.ErrorCode);
        Assert.Equal("msg", resp.ErrorMessage);
    }

    [Fact]
    public void ChatCompletionResponse_Succeeded_WithUsage()
    {
        var usage = new TokenUsage { PromptTokens = 10, CompletionTokens = 20 };
        var resp = ChatCompletionResponse.Succeeded("ok", usage);
        Assert.Equal(30, resp.Usage!.TotalTokens);
    }

    [Fact]
    public void ChatCompletionResponse_Succeeded_WithReasoning()
    {
        var resp = ChatCompletionResponse.Succeeded("answer", reasoningContent: "thinking");
        Assert.Equal("thinking", resp.ReasoningContent);
    }

    [Fact]
    public void ChatCompletionResponse_Succeeded_WithToolCalls()
    {
        var calls = new List<ChatToolCall> { new() { FunctionName = "read_file" } };
        var resp = ChatCompletionResponse.Succeeded("", toolCalls: calls);
        Assert.Single(resp.ToolCalls!);
    }

    [Fact]
    public void ChatCompletionResponse_Succeeded_WithModel()
    {
        var resp = ChatCompletionResponse.Succeeded("ok", model: "gpt-4o");
        Assert.Equal("gpt-4o", resp.Model);
    }

    [Fact]
    public void ChatCompletionStreamEvent_ReasoningToken_SetsType()
    {
        var evt = ChatCompletionStreamEvent.ReasoningToken("think");
        Assert.Equal(ChatCompletionStreamEventType.ReasoningToken, evt.Type);
        Assert.Equal("think", evt.Content);
    }

    [Fact]
    public void ChatCompletionStreamEvent_Token_SetsType()
    {
        var evt = ChatCompletionStreamEvent.Token("hello");
        Assert.Equal(ChatCompletionStreamEventType.Token, evt.Type);
        Assert.Equal("hello", evt.Content);
    }

    [Fact]
    public void ChatCompletionStreamEvent_ToolCallBegin_SetsIdAndName()
    {
        var evt = ChatCompletionStreamEvent.ToolCallBegin("call_1", "read_file");
        Assert.Equal("call_1", evt.ToolCallId);
        Assert.Equal("read_file", evt.ToolName);
    }

    [Fact]
    public void ChatCompletionStreamEvent_ToolCallDelta_SetsArgs()
    {
        var evt = ChatCompletionStreamEvent.ToolCallDelta("{\"path\":\"/tmp\"}");
        Assert.Equal("{\"path\":\"/tmp\"}", evt.ToolArguments);
    }

    [Fact]
    public void ChatCompletionStreamEvent_ToolCallEnd_SetsType()
    {
        var evt = ChatCompletionStreamEvent.ToolCallEnd();
        Assert.Equal(ChatCompletionStreamEventType.ToolCallEnd, evt.Type);
    }

    [Fact]
    public void ChatCompletionStreamEvent_Usage_SetsUsage()
    {
        var usage = new TokenUsage { PromptTokens = 5, CompletionTokens = 10 };
        var evt = ChatCompletionStreamEvent.UsageEvent(usage);
        Assert.Equal(15, evt.Usage!.TotalTokens);
    }

    [Fact]
    public void ChatCompletionStreamEvent_Error_SetsCodeAndMessage()
    {
        var evt = ChatCompletionStreamEvent.Error("ERR", "error msg");
        Assert.Equal("ERR", evt.ErrorCode);
        Assert.Equal("error msg", evt.ErrorMessage);
    }

    [Fact]
    public void ChatCompletionStreamEvent_Done_SetsType()
    {
        var evt = ChatCompletionStreamEvent.Done();
        Assert.Equal(ChatCompletionStreamEventType.Done, evt.Type);
    }

    [Fact]
    public void TokenUsage_TotalTokens_Calculated()
    {
        var usage = new TokenUsage { PromptTokens = 50, CompletionTokens = 30 };
        Assert.Equal(80, usage.TotalTokens);
    }

    [Fact]
    public void TokenUsage_ZeroTokens()
    {
        var usage = new TokenUsage();
        Assert.Equal(0, usage.TotalTokens);
    }

    [Fact]
    public void ChatMessage_DefaultValues()
    {
        var msg = new ChatMessage();
        Assert.Equal("", msg.Role);
        Assert.Equal("", msg.Content);
        Assert.Null(msg.ToolCalls);
    }

    [Fact]
    public void ChatTool_DefaultValues()
    {
        var tool = new ChatTool();
        Assert.Equal("", tool.Name);
        Assert.Null(tool.InputSchemaJson);
    }

    [Fact]
    public void ChatToolCall_DefaultValues()
    {
        var tc = new ChatToolCall();
        Assert.Equal("function", tc.Type);
        Assert.Equal("", tc.FunctionName);
    }

    [Fact]
    public void ChatCompletionRequest_DefaultHasNoTools()
    {
        var req = new ChatCompletionRequest();
        Assert.False(req.HasTools);
    }

    [Fact]
    public void ChatCompletionRequest_WithTools_HasToolsTrue()
    {
        var req = new ChatCompletionRequest { Tools = new[] { new ChatTool { Name = "test" } } };
        Assert.True(req.HasTools);
    }

    [Fact]
    public void ModelProviderConfig_Create_ThrowsOnEmptyProviderType()
    {
        Assert.Throws<ArgumentException>(() => ModelProviderConfig.Create("", "", "http://localhost", "model"));
    }

    [Fact]
    public void ModelProviderConfig_Create_ThrowsOnEmptyModel()
    {
        Assert.Throws<ArgumentException>(() => ModelProviderConfig.Create("openai", "", "http://localhost", ""));
    }

    [Fact]
    public void ModelProviderConfig_WithTimeout_ThrowsOnZero()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithTimeout(TimeSpan.Zero));
    }

    [Fact]
    public void ModelProviderConfig_WithTimeout_ThrowsOnOver120()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithTimeout(TimeSpan.FromSeconds(121)));
    }

    [Fact]
    public void ModelProviderConfig_WithTimeout_Valid()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        var result = cfg.WithTimeout(TimeSpan.FromSeconds(30));
        Assert.Equal(30, result.Timeout.TotalSeconds);
    }

    [Fact]
    public void ModelProviderConfig_WithMaxRetries_Negative_Throws()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithMaxRetries(-1));
    }

    [Fact]
    public void ModelProviderConfig_WithMaxRetries_Over5_Throws()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithMaxRetries(6));
    }

    [Fact]
    public void ModelProviderConfig_WithMaxRetries_Valid()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        var result = cfg.WithMaxRetries(3);
        Assert.Equal(3, result.MaxRetries);
    }

    [Fact]
    public void ModelProviderConfig_WithMaxTokens_Zero_Throws()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithMaxTokens(0));
    }

    [Fact]
    public void ModelProviderConfig_WithMaxTokens_Over131072_Throws()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Throws<ArgumentOutOfRangeException>(() => cfg.WithMaxTokens(200000));
    }

    [Fact]
    public void ModelProviderConfig_WithMaxTokens_Valid()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        var result = cfg.WithMaxTokens(8192);
        Assert.Equal(8192, result.MaxTokens);
    }

    [Fact]
    public void ModelProviderConfig_EndpointNormalized_TrailingSlash()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost:11434", "model");
        Assert.EndsWith("/", cfg.Endpoint);
    }

    [Fact]
    public void ModelProviderConfig_DefaultTimeoutIs30Seconds()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Equal(30, cfg.Timeout.TotalSeconds);
    }

    [Fact]
    public void ModelProviderConfig_DefaultMaxRetriesIs2()
    {
        var cfg = ModelProviderConfig.Create("openai", "key", "http://localhost", "model");
        Assert.Equal(2, cfg.MaxRetries);
    }
}
