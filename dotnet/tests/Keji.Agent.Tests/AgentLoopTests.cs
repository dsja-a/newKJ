using System.Collections.Immutable;
using System.Text.Json;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Persistence.Models;
using Keji.Persistence.Repositories;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Security.Authorization;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Agent.Tests;

public sealed class AgentLoopTests
{
    [Fact]
    public async Task RunAsync_CompletesAndPersistsOnlyUserAndFinalAssistant()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("final answer"));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.True(result.Success, result.Status.ToString());
        Assert.Equal("final answer", result.Content);
        Assert.Equal(1, result.Iterations);
        Assert.Equal(new[] { "user", "assistant" }, fixture.Messages.Writes.Select(static item => item.Role));
        Assert.Equal(new[] { "hello", "final answer" }, fixture.Messages.Writes.Select(static item => item.Content));
    }

    [Fact]
    public async Task RunAsync_RejectsUnsupportedHistoryWithoutSilentlyDroppingIt()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("ok"));
        fixture.Messages.History.Add(new MessageRecord { Role = "user", Content = "old user" });
        fixture.Messages.History.Add(new MessageRecord { Role = "tool", Content = "must not replay" });
        fixture.Messages.History.Add(new MessageRecord { Role = "assistant", Content = "old assistant" });

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.LimitExceeded, result.Status);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task RunAsync_UnauthenticatedHasNoSideEffects()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.User.CurrentUser = null;

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.Unauthenticated, result.Status);
        Assert.Empty(fixture.Messages.Writes);
        Assert.Empty(fixture.Provider.Requests);
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task RunAsync_UnownedConversationHasNoSideEffects()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Conversations.Owned = false;

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ConversationNotFound, result.Status);
        Assert.Empty(fixture.Messages.Writes);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task RunAsync_UnknownProviderFailsBeforePersistence()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));

        var result = await fixture.Loop.RunAsync(Request(provider: "missing"));

        Assert.Equal(AgentRunStatus.ProviderNotFound, result.Status);
        Assert.Empty(fixture.Messages.Writes);
    }

    [Theory]
    [InlineData("", "openai", "hello")]
    [InlineData("bad/id", "openai", "hello")]
    [InlineData("conv", "bad/provider", "hello")]
    [InlineData("conv", "openai", "")]
    public async Task RunAsync_InvalidRequestFailsClosed(string conversationId, string provider, string message)
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var result = await fixture.Loop.RunAsync(new AgentRunRequest
        {
            ConversationId = conversationId,
            ProviderName = provider,
            UserMessage = message,
        });
        Assert.Equal(AgentRunStatus.InvalidRequest, result.Status);
        Assert.Empty(fixture.Messages.Writes);
    }

    [Fact]
    public async Task RunAsync_AdvertisesOnlyExecutableToolsWithObjectSchema()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("ok"));

        await fixture.Loop.RunAsync(Request());

        var tool = Assert.Single(Assert.Single(fixture.Provider.Requests).Tools);
        Assert.Equal("calculator", tool.Name);
        Assert.DoesNotContain("contract_only", Assert.Single(fixture.Provider.Requests).Tools.Select(static item => item.Name));
        using var schema = JsonDocument.Parse(tool.InputSchemaJson!);
        Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public async Task RunAsync_ExecutesToolSeriallyThroughPipelineThenCompletes()
    {
        var fixture = Fixture(
            ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1+1\"}")),
            ChatCompletionResponse.Succeeded("two"));
        fixture.Pipeline.Handler = async (_, inputs, ct) =>
        {
            Assert.Equal("1+1", inputs!["expression"]);
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            return ToolExecutionResult.Successful("{\"value\":2}", TimeSpan.Zero);
        };

        var result = await fixture.Loop.RunAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(2, result.Iterations);
        Assert.Equal(1, result.ToolCalls);
        Assert.Equal(new[] { "calculator" }, fixture.Pipeline.Calls);
        Assert.Equal(2, fixture.Provider.Requests.Count);
        var second = fixture.Provider.Requests[1].Messages;
        Assert.Equal(KejiChatRole.Assistant, second[^2].Role);
        Assert.Equal(KejiChatRole.Tool, second[^1].Role);
        Assert.Equal("{\"value\":2}", second[^1].Content);
        Assert.Equal(new[] { "user", "assistant" }, fixture.Messages.Writes.Select(static item => item.Role));
    }

    [Fact]
    public async Task RunAsync_MultipleToolsNeverOverlap()
    {
        var fixture = Fixture(
            ToolResponse(
                ToolCall("call_1", "calculator", "{\"expression\":\"1+1\"}"),
                ToolCall("call_2", "calculator", "{\"expression\":\"2+2\"}")),
            ChatCompletionResponse.Succeeded("done"));
        var active = 0;
        var maxActive = 0;
        fixture.Pipeline.Handler = async (_, _, _) =>
        {
            active++;
            maxActive = Math.Max(maxActive, active);
            await Task.Yield();
            active--;
            return ToolExecutionResult.Successful("{}", TimeSpan.Zero);
        };

        var result = await fixture.Loop.RunAsync(Request());

        Assert.True(result.Success);
        Assert.Equal(1, maxActive);
        Assert.Equal(2, fixture.Pipeline.Calls.Count);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("contract_only")]
    public async Task RunAsync_UnknownOrNonExecutableToolIsRejectedWithoutPipeline(string toolName)
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", toolName, "{}")));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ToolFailed, result.Status);
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task RunAsync_NestedToolArgumentsAreRejected()
    {
        var fixture = Fixture(ToolResponse(
            ToolCall("call_1", "calculator", "{\"expression\":{\"nested\":true}}")));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ToolFailed, result.Status);
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task RunAsync_DuplicateToolCallIdsAreRejected()
    {
        var fixture = Fixture(ToolResponse(
            ToolCall("duplicate", "calculator", "{}"),
            ToolCall("duplicate", "calculator", "{}")));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ToolFailed, result.Status);
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task RunAsync_ToolFailureDoesNotExposeRawErrorToModelOrCaller()
    {
        const string secret = "secret-tool-error-stack";
        var fixture = Fixture(
            ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1/0\"}")),
            ChatCompletionResponse.Succeeded("safe"));
        fixture.Pipeline.Handler = (_, _, _) => Task.FromResult(
            ToolExecutionResult.Failed(secret, "EXECUTION_FAILED"));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.True(result.Success);
        Assert.DoesNotContain(secret, result.Content);
        Assert.DoesNotContain(secret, fixture.Provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
        Assert.Contains("EXECUTION_FAILED", fixture.Provider.Requests[1].Messages[^1].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_OversizedToolResultStopsBeforeNextProviderCall()
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1+1\"}")));
        fixture.Pipeline.Handler = (_, _, _) => Task.FromResult(
            ToolExecutionResult.Successful(new string('x', 65 * 1024), TimeSpan.Zero));

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.LimitExceeded, result.Status);
        Assert.Single(fixture.Provider.Requests);
    }

    [Fact]
    public async Task RunAsync_IterationLimitStopsFiniteLoop()
    {
        var fixture = Fixture(
            options: new AgentLoopOptions(maxIterations: 2),
            responses:
            [
                ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1\"}")),
                ToolResponse(ToolCall("call_2", "calculator", "{\"expression\":\"2\"}")),
            ]);

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.LimitExceeded, result.Status);
        Assert.Equal(2, result.Iterations);
        Assert.Equal(2, result.ToolCalls);
        Assert.Equal(2, fixture.Provider.Requests.Count);
    }

    [Fact]
    public async Task RunAsync_OwnershipIsRecheckedBeforeToolExecution()
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1\"}")));
        fixture.Conversations.RevokeAfterSuccessfulChecks = 3;

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ConversationNotFound, result.Status);
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task RunAsync_ProviderExceptionIsSanitized()
    {
        const string secret = "provider-secret-stack";
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.Handler = (_, _) => throw new InvalidOperationException(secret);

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ProviderFailed, result.Status);
        Assert.DoesNotContain(secret, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_ToolPipelineExceptionIsSanitized()
    {
        const string secret = "tool-secret-stack";
        var fixture = Fixture(ToolResponse(
            ToolCall("call_1", "calculator", "{\"expression\":\"1\"}")));
        fixture.Pipeline.Handler = (_, _, _) => throw new InvalidOperationException(secret);

        var result = await fixture.Loop.RunAsync(Request());

        Assert.Equal(AgentRunStatus.ProviderFailed, result.Status);
        Assert.DoesNotContain(secret, result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_CancellationPropagatesWithoutAssistantWrite()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.Handler = (_, ct) => Task.FromCanceled<ChatCompletionResponse>(ct);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => fixture.Loop.RunAsync(Request(), cts.Token));
        Assert.DoesNotContain(fixture.Messages.Writes, static item => item.Role == "assistant");
    }

    [Fact]
    public void OptionsRejectUnboundedValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxIterations: int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxToolCalls: int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxContextMessages: int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxContextBytes: int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxToolResultBytes: int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AgentLoopOptions(maxTotalToolResultBytes: int.MaxValue));
    }

    [Fact]
    public void DependencyInjectionRegistersScopedLoopAndSingletonOptions()
    {
        var services = new ServiceCollection();

        services.AddKejiAgentLoop();

        var loop = Assert.Single(services, static item => item.ServiceType == typeof(IAgentLoop));
        Assert.Equal(ServiceLifetime.Scoped, loop.Lifetime);
        var options = Assert.Single(services, static item => item.ServiceType == typeof(AgentLoopOptions));
        Assert.Equal(ServiceLifetime.Singleton, options.Lifetime);
    }

    [Fact]
    public void Options_RejectUnpairedSurrogateSystemPrompt()
    {
        var invalidUtf16 = new string((char)0xd800, 1);
        Assert.Throws<ArgumentException>(() => new AgentLoopOptions(systemPrompt: invalidUtf16));
    }

    [Fact]
    public async Task RunStreamAsync_EmitsOrderedRunTranscriptAndUtcTimes()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("hello stream", new TokenUsage { PromptTokens = 3, CompletionTokens = 2 }));

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));

        Assert.Equal(Enumerable.Range(1, events.Count).Select(static value => (long)value), events.Select(static item => item.Sequence));
        Assert.Single(events.Select(static item => item.RunId).Distinct());
        Assert.All(events, static item => Assert.Equal(TimeSpan.Zero, item.TimestampUtc.Offset));
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.AnswerDelta && item.ContentDelta == "hello stream");
        var done = Assert.Single(events, static item => item.Type == KejiAgentEventType.RunCompleted);
        Assert.Equal("hello stream", done.Transcript!.AssistantContent);
        Assert.True(done.Transcript.CompletedAtUtc >= done.Transcript.StartedAtUtc);
    }

    [Fact]
    public async Task RunStreamAsync_AccumulatesUsageAcrossLengthRecovery()
    {
        var fixture = Fixture(
            ChatCompletionResponse.Succeeded("part ", new TokenUsage { PromptTokens = 5, CompletionTokens = 2, CachedTokens = 1 }, finishReason: KejiFinishReason.Length),
            ChatCompletionResponse.Succeeded("two", new TokenUsage { PromptTokens = 7, CompletionTokens = 3, CachedTokens = 2 }, finishReason: KejiFinishReason.Stop));

        var done = Assert.Single(await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.RunCompleted);

        Assert.Equal("part two", done.Transcript!.AssistantContent);
        Assert.Equal(new KejiAgentUsage(12, 5, 3), done.Transcript.Usage);
        Assert.Equal(2, done.Transcript.Iterations);
    }

    [Fact]
    public async Task RunStreamAsync_RecoversFromEmptyAnswerOnce()
    {
        var fixture = Fixture(
            ChatCompletionResponse.Succeeded("", finishReason: KejiFinishReason.Stop),
            ChatCompletionResponse.Succeeded("recovered", finishReason: KejiFinishReason.Stop));

        var done = Assert.Single(await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.RunCompleted);

        Assert.Equal("recovered", done.Transcript!.AssistantContent);
        Assert.Equal(2, fixture.Provider.Requests.Count);
    }

    [Fact]
    public async Task RunStreamAsync_ReportsLengthWhenRecoveryIsExhausted()
    {
        var fixture = Fixture(new AgentLoopOptions(maxRecoveryAttempts: 0),
            ChatCompletionResponse.Succeeded("partial", finishReason: KejiFinishReason.Length));

        var error = Assert.Single(await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.Error);

        Assert.Equal(KejiAgentErrorCode.IterationLimit, error.ErrorCode);
        Assert.DoesNotContain(fixture.Messages.Writes, static item => item.Role == "assistant");
    }

    [Theory]
    [InlineData(KejiProviderErrorCode.Timeout, KejiAgentErrorCode.ProviderTimeout)]
    [InlineData(KejiProviderErrorCode.AuthFailed, KejiAgentErrorCode.ProviderRejected)]
    [InlineData(KejiProviderErrorCode.QuotaExceeded, KejiAgentErrorCode.ProviderRejected)]
    [InlineData(KejiProviderErrorCode.ServiceUnavailable, KejiAgentErrorCode.ProviderUnavailable)]
    [InlineData(KejiProviderErrorCode.StreamProtocolError, KejiAgentErrorCode.ProviderProtocolError)]
    public async Task RunStreamAsync_MapsProviderErrorsWithoutRawMessage(KejiProviderErrorCode providerCode, KejiAgentErrorCode expected)
    {
        const string secret = "provider raw secret";
        var fixture = Fixture(ChatCompletionResponse.Failed(providerCode, secret));

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        var error = Assert.Single(events, static item => item.Type == KejiAgentEventType.Error);

        Assert.Equal(expected, error.ErrorCode);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunStreamAsync_UsesStreamProviderWithoutCompleteAsync()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.Handler = (_, _) => throw new InvalidOperationException("CompleteAsync must not run");
        fixture.Provider.StreamHandler = (_, ct) => ProviderEvents(ct,
            ChatCompletionStreamEvent.Token("stream only"),
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false),
            ChatCompletionStreamEvent.Done());

        var done = Assert.Single(await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.RunCompleted);

        Assert.Equal("stream only", done.Transcript!.AssistantContent);
    }

    [Fact]
    public async Task RunStreamAsync_RejectsConcurrentSameSessionAndReleasesGate()
    {
        var gate = new KejiAgentSessionGate();
        var first = Fixture(new AgentLoopOptions(), gate, ChatCompletionResponse.Succeeded("unused"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.Provider.StreamHandler = (_, ct) => DelayedProviderEvents(release.Task, ct);
        await using var enumerator = first.Loop.RunStreamAsync(StreamRequest()).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        var second = Fixture(new AgentLoopOptions(), gate, ChatCompletionResponse.Succeeded("second"));

        var busy = Assert.Single(await CollectAsync(second.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.Error);
        Assert.Equal(KejiAgentErrorCode.SessionBusy, busy.ErrorCode);

        release.SetResult();
        while (await enumerator.MoveNextAsync()) { }
        var third = Fixture(new AgentLoopOptions(), gate, ChatCompletionResponse.Succeeded("third"));
        Assert.Contains(await CollectAsync(third.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.RunCompleted);
    }

    [Fact]
    public async Task RunStreamAsync_RunTimeoutProducesTypedTimeout()
    {
        var time = new ManualTimeProvider();
        var fixture = FixtureWithTimeProvider(new AgentLoopOptions(runTimeout: TimeSpan.FromSeconds(1)), time,
            ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => NeverCompletingProvider(ct);

        var pending = CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        time.Advance(TimeSpan.FromSeconds(2));
        var error = Assert.Single(await pending, static item => item.Type == KejiAgentEventType.Error);

        Assert.Equal(KejiAgentErrorCode.RunTimedOut, error.ErrorCode);
        Assert.Equal(KejiAgentStopReason.TimedOut, error.StopReason);
    }

    [Fact]
    public async Task RunStreamAsync_WritesSafeAgentAudit()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("audited"));

        await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));

        var audit = Assert.Single(fixture.Audit.Calls, static call => call.Action == "agent_run_completed");
        Assert.Equal("agent_run_completed", audit.Action);
        Assert.Equal(KejiAuditOutcome.Success, audit.Outcome);
        Assert.Equal(StreamRequest().RunId, audit.Metadata["run_id"]);
        Assert.DoesNotContain("audited", JsonSerializer.Serialize(audit), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureTerminal_IsErrorImmediatelyFollowedByRunCompleted()
    {
        var events = await CollectAsync(Fixture(ChatCompletionResponse.Failed(KejiProviderErrorCode.ServerError, "raw")).Loop.RunStreamAsync(StreamRequest()));
        var errorIndex = events.FindIndex(static item => item.Type == KejiAgentEventType.Error);
        Assert.True(errorIndex >= 0);
        Assert.Equal(KejiAgentEventType.RunCompleted, events[errorIndex + 1].Type);
        Assert.Equal(errorIndex + 2, events.Count);
    }

    [Fact]
    public async Task FailureTerminal_ContainsExactlyOneErrorAndOneCompletion()
    {
        var events = await CollectAsync(Fixture(ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "raw")).Loop.RunStreamAsync(StreamRequest()));
        Assert.Single(events, static item => item.Type == KejiAgentEventType.Error);
        Assert.Single(events, static item => item.Type == KejiAgentEventType.RunCompleted);
    }

    [Fact]
    public async Task InvalidRequest_StillEmitsFailureTerminal()
    {
        var request = StreamRequest();
        var invalid = new KejiAgentRunRequest { RunId = request.RunId, ConversationId = "bad space", ProviderName = "openai", Model = "model", UserMessage = "hello" };
        var events = await CollectAsync(Fixture(ChatCompletionResponse.Succeeded("unused")).Loop.RunStreamAsync(invalid));
        AssertFailureTerminal(events, KejiAgentErrorCode.InvalidRequest);
    }

    [Fact]
    public async Task InvalidRunId_UsesFreshSafeRunIdAcrossEventsTranscriptAuditAndSse()
    {
        const string unsafeRunId = "INVALID-RUN-ID/secret";
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var request = new KejiAgentRunRequest
        {
            RunId = unsafeRunId, ConversationId = "conv_1", ProviderName = "openai",
            Model = "model", UserMessage = "hello",
        };

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(request));

        AssertFailureTerminal(events, KejiAgentErrorCode.InvalidRequest);
        var effectiveRunId = Assert.Single(events.Select(static item => item.RunId).Distinct());
        Assert.Matches("^[0-9a-f]{32}$", effectiveRunId);
        Assert.Equal(effectiveRunId, events[^1].Transcript!.RunId);
        Assert.All(fixture.Audit.Calls, call => Assert.Equal(effectiveRunId, call.Metadata["run_id"]));
        Assert.DoesNotContain(unsafeRunId, JsonSerializer.Serialize(events), StringComparison.Ordinal);
        Assert.DoesNotContain(unsafeRunId, JsonSerializer.Serialize(fixture.Audit.Calls), StringComparison.Ordinal);

        var sseFixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(sseFixture.Loop.RunStreamAsync(request)));
        Assert.Contains("event: error\n", frames[^2], StringComparison.Ordinal);
        Assert.Contains("event: done\n", frames[^1], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("conversation", "bad conversation/raw", "openai", "model", "hello")]
    [InlineData("provider", "conv_1", "bad/provider/raw", "model", "hello")]
    [InlineData("model", "conv_1", "openai", "bad\u0000model", "hello")]
    public async Task InvalidTextFields_AreInvalidRequestAndNeverReachTerminalOrAudit(
        string marker, string conversationId, string provider, string model, string message)
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var request = new KejiAgentRunRequest
        {
            RunId = StreamRequest().RunId, ConversationId = conversationId, ProviderName = provider,
            Model = model, UserMessage = message,
        };

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(request));

        AssertFailureTerminal(events, KejiAgentErrorCode.InvalidRequest);
        var done = events[^1];
        Assert.Empty(done.Transcript!.Messages);
        var auditValues = fixture.Audit.Calls.SelectMany(static call => call.Metadata.Values).ToArray();
        switch (marker)
        {
            case "conversation":
                Assert.Empty(done.Transcript.ConversationId);
                Assert.DoesNotContain(conversationId, auditValues);
                break;
            case "provider":
                Assert.DoesNotContain(provider, auditValues);
                break;
            case "model":
            case "model_surrogate":
                Assert.DoesNotContain(model, auditValues);
                break;
            case "message":
                Assert.DoesNotContain(message, auditValues);
                break;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnpairedSurrogate_IsInvalidRequestNotContextLimit(bool putInModel)
    {
        var invalidUtf16 = new string((char)0xd800, 1);
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var request = new KejiAgentRunRequest
        {
            RunId = StreamRequest().RunId, ConversationId = "conv_1", ProviderName = "openai",
            Model = putInModel ? invalidUtf16 : "model",
            UserMessage = putInModel ? "hello" : invalidUtf16,
        };

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(request));

        AssertFailureTerminal(events, KejiAgentErrorCode.InvalidRequest);
        Assert.Empty(events[^1].Transcript!.Messages);
        Assert.All(fixture.Audit.Calls.SelectMany(static call => call.Metadata.Values),
            value => Assert.DoesNotContain(invalidUtf16, value, StringComparison.Ordinal));
    }

    [Fact]
    public async Task BoundedChannel_DoesNotDropFailureTerminalWhenNearlyFull()
    {
        var fixture = Fixture(new AgentLoopOptions(eventBufferCapacity: 1), ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => ManyThenError(ct);
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderUnavailable);
    }

    [Fact]
    public async Task Cancellation_PropagatesWithoutErrorOrCompletion()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => NeverCompletingProvider(ct);
        using var cts = new CancellationTokenSource();
        var seen = new List<KejiAgentEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in fixture.Loop.RunStreamAsync(StreamRequest(), cts.Token))
            {
                seen.Add(item);
                cts.Cancel();
            }
        });
        Assert.DoesNotContain(seen, static item => item.Type is KejiAgentEventType.Error or KejiAgentEventType.RunCompleted);
    }

    [Fact]
    public async Task ProviderStream_RejectsDuplicateDone()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done(), ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsTokenAfterDone()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done(), ChatCompletionStreamEvent.Token("late"));
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsUsageAfterDone()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done(), ChatCompletionStreamEvent.UsageEvent(new TokenUsage()));
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsDuplicateUsage()
    {
        var usage = ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1 });
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), usage, usage, ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Theory]
    [InlineData("token")]
    [InlineData("reasoning")]
    [InlineData("tool_begin")]
    [InlineData("choice")]
    [InlineData("error")]
    public async Task ProviderStream_ChoiceFinishedAllowsOnlyOptionalUsageThenDone(string lateKind)
    {
        var late = lateKind switch
        {
            "token" => ChatCompletionStreamEvent.Token("late"),
            "reasoning" => ChatCompletionStreamEvent.ReasoningToken("late"),
            "tool_begin" => ChatCompletionStreamEvent.ToolCallBegin("call_1", "calculator"),
            "choice" => ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false),
            _ => ChatCompletionStreamEvent.Error(KejiProviderErrorCode.ServerError, "raw"),
        };
        var events = await ProtocolEvents(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false),
            late,
            ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_AfterUsageAllowsOnlyDone()
    {
        var events = await ProtocolEvents(
            ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false),
            ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1 }),
            ChatCompletionStreamEvent.Token("late"),
            ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsMissingChoiceFinished()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.Token("answer"), ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsMissingDone()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.Token("answer"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false));
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task ProviderStream_RejectsUnfinishedToolCall()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ToolCallBegin("call_1", "calculator"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.ToolCalls, true), ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task Reasoning_EmitsOneStartAndEveryDelta()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.ReasoningToken("r1"), ChatCompletionStreamEvent.ReasoningToken("r2"), ChatCompletionStreamEvent.Token("answer"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done());
        Assert.Single(events, static item => item.Type == KejiAgentEventType.ThinkingStarted);
        Assert.Equal(new[] { "r1", "r2" }, events.Where(static item => item.Type == KejiAgentEventType.ThinkingDelta).Select(static item => item.ContentDelta));
    }

    [Fact]
    public async Task Reasoning_IsAbsentFromTranscriptAndPersistence()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => ProviderEvents(ct, ChatCompletionStreamEvent.ReasoningToken("private reasoning"), ChatCompletionStreamEvent.Token("public"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done());
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        var transcript = events.Single(static item => item.Type == KejiAgentEventType.RunCompleted).Transcript!;
        Assert.DoesNotContain("private reasoning", JsonSerializer.Serialize(transcript), StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.Messages.Writes, static item => item.Content.Contains("private reasoning", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnsweringStarted_IsEmittedOnlyOnceAcrossTokens()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.Token("a"), ChatCompletionStreamEvent.Token("b"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.Done());
        Assert.Single(events, static item => item.Type == KejiAgentEventType.AnsweringStarted);
        Assert.Equal(2, events.Count(static item => item.Type == KejiAgentEventType.AnswerDelta));
    }

    [Fact]
    public async Task Usage_IsEmittedExactlyOnceAtFinalRunBoundary()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("answer", new TokenUsage { PromptTokens = 4, CompletionTokens = 2, CachedTokens = 1 }));
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        var usageIndex = events.FindIndex(static item => item.Type == KejiAgentEventType.Usage);
        Assert.Single(events, static item => item.Type == KejiAgentEventType.Usage);
        Assert.Equal(KejiAgentEventType.RunCompleted, events[usageIndex + 1].Type);
    }

    [Fact]
    public async Task Usage_RejectsCachedTokensGreaterThanPromptTokens()
    {
        var events = await ProtocolEvents(ChatCompletionStreamEvent.Token("answer"), ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false), ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 1, CachedTokens = 2 }), ChatCompletionStreamEvent.Done());
        AssertFailureTerminal(events, KejiAgentErrorCode.ProviderProtocolError);
    }

    [Fact]
    public async Task RunId_RejectsUppercaseAndPreservesValidRequestValue()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        var invalid = new KejiAgentRunRequest { RunId = "0123456789ABCDEF0123456789ABCDEF", ConversationId = "conv_1", ProviderName = "openai", Model = "model", UserMessage = "hello" };
        AssertFailureTerminal(await CollectAsync(fixture.Loop.RunStreamAsync(invalid)), KejiAgentErrorCode.InvalidRequest);
        var valid = await CollectAsync(Fixture(ChatCompletionResponse.Succeeded("answer")).Loop.RunStreamAsync(StreamRequest()));
        Assert.All(valid, static item => Assert.Equal("0123456789abcdef0123456789abcdef", item.RunId));
    }

    [Fact]
    public async Task Sse_UsesIndependentUniqueHexEventIdsAndTask012Names()
    {
        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(Fixture(ChatCompletionResponse.Succeeded("answer")).Loop.RunStreamAsync(StreamRequest())));
        var ids = frames.Select(static frame => frame.Split('\n')[0][4..]).ToArray();
        Assert.All(ids, static id => Assert.Matches("^[0-9a-f]{32}$", id));
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.DoesNotContain(StreamRequest().RunId, ids);
        Assert.Contains(frames, static frame => frame.Contains("event: answer\n", StringComparison.Ordinal));
        Assert.Contains(frames, static frame => frame.Contains("event: done\n", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sse_StripsTranscriptConversationAndSensitiveBodies()
    {
        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(Fixture(ChatCompletionResponse.Succeeded("safe answer")).Loop.RunStreamAsync(StreamRequest())));
        var wire = string.Concat(frames);
        Assert.DoesNotContain("transcript", wire, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conv_1", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Context_BeginsWithConfiguredServerSystemPrompt()
    {
        var options = new AgentLoopOptions(systemPrompt: "server-owned-system");
        var fixture = Fixture(options, ChatCompletionResponse.Succeeded("answer"));
        await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        var first = Assert.Single(fixture.Provider.Requests).Messages[0];
        Assert.Equal(KejiChatRole.System, first.Role);
        Assert.Equal("server-owned-system", first.Content);
    }

    [Fact]
    public async Task ContextLimit_DoesNotCallProviderOrWriteUserMessage()
    {
        var fixture = Fixture(new AgentLoopOptions(maxContextMessages: 2), ChatCompletionResponse.Succeeded("unused"));
        fixture.Messages.History.Add(new MessageRecord { Role = "user", Content = "old" });
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        AssertFailureTerminal(events, KejiAgentErrorCode.ContextLimit);
        Assert.Empty(fixture.Provider.Requests);
        Assert.Empty(fixture.Messages.Writes);
    }

    [Fact]
    public async Task SessionGate_IsReleasedAfterProtocolFailure()
    {
        var gate = new KejiAgentSessionGate();
        var failed = Fixture(new AgentLoopOptions(), gate, ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "x"));
        AssertFailureTerminal(await CollectAsync(failed.Loop.RunStreamAsync(StreamRequest())), KejiAgentErrorCode.ProviderProtocolError);
        var next = Fixture(new AgentLoopOptions(), gate, ChatCompletionResponse.Succeeded("answer"));
        Assert.Contains(await CollectAsync(next.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.RunCompleted && item.StopReason == KejiAgentStopReason.Completed);
    }

    [Fact]
    public async Task Audit_RecordsStartedToolStartedToolCompletedAndCompleted()
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"1\"}")), ChatCompletionResponse.Succeeded("answer"));
        await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        Assert.Equal(new[] { "agent_run_started", "agent_tool_started", "agent_tool_completed", "agent_run_completed" }, fixture.Audit.Calls.Select(static call => call.Action));
    }

    [Fact]
    public async Task AuditFailure_DoesNotChangeSuccessfulResult()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("answer"));
        fixture.Audit.Throw = true;
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.RunCompleted && item.StopReason == KejiAgentStopReason.Completed);
    }

    [Fact]
    public async Task ToolEvents_ExposeIndexAndDurationWithoutArgumentsOrResult()
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", "calculator", "{\"expression\":\"secret-arg\"}")), ChatCompletionResponse.Succeeded("answer"));
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
        var started = Assert.Single(events, static item => item.Type == KejiAgentEventType.ToolStarted);
        var completed = Assert.Single(events, static item => item.Type == KejiAgentEventType.ToolCompleted);
        Assert.Equal(0, started.ToolCallIndex);
        Assert.True(completed.ToolDurationMs >= 0);
        Assert.DoesNotContain("secret-arg", JsonSerializer.Serialize(new[] { started, completed }), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("contract_only")]
    public async Task RegistryToolRejection_EmitsCompleteStartedCompletedErrorDoneSequence(string toolName)
    {
        var fixture = Fixture(ToolResponse(ToolCall("call_1", toolName, "{}")));
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));

        var terminal = events.Where(static item => item.Type is KejiAgentEventType.ToolStarted or
            KejiAgentEventType.ToolCompleted or KejiAgentEventType.Error or KejiAgentEventType.RunCompleted)
            .Select(static item => item.Type);
        Assert.Equal(new[]
        {
            KejiAgentEventType.ToolStarted, KejiAgentEventType.ToolCompleted,
            KejiAgentEventType.Error, KejiAgentEventType.RunCompleted,
        }, terminal);
        var completed = Assert.Single(events, static item => item.Type == KejiAgentEventType.ToolCompleted);
        Assert.False(completed.ToolSucceeded);
        Assert.Equal("TOOL_REJECTED", completed.ToolErrorCode);
        Assert.Empty(fixture.Pipeline.Calls);
        Assert.Equal(new[] { "agent_tool_started", "agent_tool_completed" },
            fixture.Audit.Calls.Where(static call => call.Action.StartsWith("agent_tool_", StringComparison.Ordinal))
                .Select(static call => call.Action));
    }

    private async Task<List<KejiAgentEvent>> ProtocolEvents(params ChatCompletionStreamEvent[] providerEvents)
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => ProviderEvents(ct, providerEvents);
        return await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));
    }

    private static void AssertFailureTerminal(IReadOnlyList<KejiAgentEvent> events, KejiAgentErrorCode code)
    {
        var error = Assert.Single(events, static item => item.Type == KejiAgentEventType.Error);
        Assert.Equal(code, error.ErrorCode);
        var done = Assert.Single(events, static item => item.Type == KejiAgentEventType.RunCompleted);
        Assert.NotEqual(KejiAgentStopReason.Invalid, done.StopReason);
        Assert.NotEqual(KejiAgentStopReason.Completed, done.StopReason);
        Assert.Equal(events.Count - 2, events.ToList().IndexOf(error));
        Assert.Same(done, events[^1]);
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> ManyThenError(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        for (var i = 0; i < 40; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ChatCompletionStreamEvent.ReasoningToken("x");
        }
        yield return ChatCompletionStreamEvent.Error(KejiProviderErrorCode.ServiceUnavailable, "raw");
        await Task.CompletedTask;
    }

    private static AgentRunRequest Request(string provider = "openai") => new()
    {
        ConversationId = "conv_1",
        ProviderName = provider,
        Model = "model",
        UserMessage = "hello",
    };

    private static KejiAgentRunRequest StreamRequest() => new()
    {
        RunId = "0123456789abcdef0123456789abcdef",
        ConversationId = "conv_1",
        ProviderName = "openai",
        Model = "model",
        UserMessage = "hello",
    };

    private static ChatCompletionResponse ToolResponse(params ChatToolCall[] calls) =>
        ChatCompletionResponse.Succeeded(
            string.Empty,
            toolCalls: calls.ToImmutableArray(),
            finishReason: KejiFinishReason.ToolCalls);

    private static ChatToolCall ToolCall(string id, string name, string arguments)
    {
        using var document = JsonDocument.Parse(arguments);
        return new ChatToolCall
        {
            Id = id,
            FunctionName = name,
            FunctionArguments = document.RootElement.Clone(),
        };
    }

    private static TestFixture Fixture(params ChatCompletionResponse[] responses) =>
        Fixture(new AgentLoopOptions(), responses);

    private static TestFixture Fixture(AgentLoopOptions options, params ChatCompletionResponse[] responses) =>
        Fixture(options, new KejiAgentSessionGate(), responses);

    private static TestFixture FixtureWithTimeProvider(
        AgentLoopOptions options,
        TimeProvider timeProvider,
        params ChatCompletionResponse[] responses) =>
        Fixture(options, new KejiAgentSessionGate(), timeProvider, responses);

    private static TestFixture Fixture(AgentLoopOptions options, IKejiAgentSessionGate gate, params ChatCompletionResponse[] responses)
        => Fixture(options, gate, TimeProvider.System, responses);

    private static TestFixture Fixture(AgentLoopOptions options, IKejiAgentSessionGate gate, TimeProvider timeProvider, params ChatCompletionResponse[] responses)
    {
        var user = new MutableUserAccessor
        {
            CurrentUser = new CurrentUser("user_1", "user", "member", "User", KejiAuthenticationKind.Jwt),
        };
        var conversations = new FakeConversationRepository();
        var messages = new FakeMessageRepository();
        var provider = new ScriptedProvider(responses);
        var registry = new ModelProviderRegistry(new[]
        {
            new KeyValuePair<string, IModelProvider>("openai", provider),
        });
        var tools = ToolRegistry();
        var pipeline = new FakeToolPipeline();
        var audit = new FakeAuditService();
        var loop = new AgentLoop(user, conversations, messages, registry, tools, pipeline, options, audit, gate,
            new KejiAgentContextBuilder(options), timeProvider);
        return new TestFixture(loop, user, conversations, messages, provider, pipeline, audit);
    }

    private static IKejiToolRegistry ToolRegistry()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(Tool("calculator", KejiToolAvailability.Executable));
        builder.Register(Tool("contract_only", KejiToolAvailability.ContractOnly));
        return builder.Build();
    }

    private static KejiToolDefinition Tool(string name, KejiToolAvailability availability) => new(
        KejiToolName.Create(name),
        1,
        "Test tool",
        KejiToolCategory.Utility,
        KejiToolRiskLevel.ReadOnly,
        KejiToolExecutionTarget.ToolWorker,
        KejiPermission.ToolExecuteRead,
        new KejiToolInputSchema(new[]
        {
            new KejiToolParameterDefinition(
                "expression", KejiToolParameterType.String, false, "Expression", maxLength: 128),
        }),
        availability);

    private sealed record TestFixture(
        AgentLoop Loop,
        MutableUserAccessor User,
        FakeConversationRepository Conversations,
        FakeMessageRepository Messages,
        ScriptedProvider Provider,
        FakeToolPipeline Pipeline,
        FakeAuditService Audit);

    private sealed class MutableUserAccessor : ICurrentUserAccessor
    {
        public CurrentUser? CurrentUser { get; set; }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public bool Owned { get; set; } = true;
        public int Checks { get; private set; }
        public int? RevokeAfterSuccessfulChecks { get; set; }

        public Task<ConversationRecord?> GetOwnedAsync(string convId, string ownerUserId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Checks++;
            var owned = Owned && (!RevokeAfterSuccessfulChecks.HasValue || Checks <= RevokeAfterSuccessfulChecks.Value);
            return Task.FromResult<ConversationRecord?>(owned
                ? new ConversationRecord { Id = convId, OwnerUserId = ownerUserId }
                : null);
        }

        public Task<ConversationRecord> CreateOwnedAsync(string convId, string ownerUserId, string title = "New", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(string convId, string ownerUserId, string title = "New", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<List<ConversationRecord>> ListOwnedAsync(string ownerUserId, int limit = 50, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RenameOwnedAsync(string convId, string ownerUserId, string title, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteOwnedAsync(string convId, string ownerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountByOwnerAsync(string ownerUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeMessageRepository : IMessageRepository
    {
        public List<MessageRecord> History { get; } = new();
        public List<(string Role, string Content)> Writes { get; } = new();

        public Task<long> AddOwnedAsync(string conversationId, string ownerUserId, string role, string content, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add((role, content));
            return Task.FromResult((long)Writes.Count);
        }

        public Task<List<MessageRecord>> ListOwnedMessagesAsync(string conversationId, string ownerUserId, int limit = 100, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(History.TakeLast(limit).ToList());
        }
    }

    private sealed class ScriptedProvider : IModelProvider
    {
        private readonly Queue<ChatCompletionResponse> _responses;
        public string ProviderName => "openai";
        public List<ChatCompletionRequest> Requests { get; } = new();
        public Func<ChatCompletionRequest, CancellationToken, Task<ChatCompletionResponse>>? Handler { get; set; }
        public Func<ChatCompletionRequest, CancellationToken, IAsyncEnumerable<ChatCompletionStreamEvent>>? StreamHandler { get; set; }

        public ScriptedProvider(IEnumerable<ChatCompletionResponse> responses) =>
            _responses = new Queue<ChatCompletionResponse>(responses);

        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            if (Handler is not null)
                return Handler(request, ct);
            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            if (StreamHandler is not null)
            {
                Requests.Add(request);
                await foreach (var item in StreamHandler(request, ct).WithCancellation(ct))
                    yield return item;
                yield break;
            }
            var response = await CompleteAsync(request, ct);
            if (!response.Success)
            {
                yield return ChatCompletionStreamEvent.Error(response.ErrorCode, response.ErrorMessage ?? "failed");
                yield return ChatCompletionStreamEvent.Done();
                yield break;
            }
            if (!string.IsNullOrEmpty(response.Content))
                yield return ChatCompletionStreamEvent.Token(response.Content);
            for (var index = 0; !response.ToolCalls.IsDefault && index < response.ToolCalls.Length; index++)
            {
                var call = response.ToolCalls[index];
                yield return ChatCompletionStreamEvent.ToolCallBegin(call.Id, call.FunctionName, index);
                yield return ChatCompletionStreamEvent.ToolCallDelta(call.FunctionArguments.GetRawText(), index, toolCallId: call.Id);
                yield return ChatCompletionStreamEvent.ToolCallEnd(call.Id, index);
            }
            yield return ChatCompletionStreamEvent.ChoiceFinished(
                response.FinishReason == KejiFinishReason.Invalid ? KejiFinishReason.Stop : response.FinishReason,
                response.HasToolCalls);
            if (response.Usage is not null)
                yield return ChatCompletionStreamEvent.UsageEvent(response.Usage);
            yield return ChatCompletionStreamEvent.Done();
        }
    }

    private sealed class FakeAuditService : IKejiAuditService
    {
        public List<AuditCall> Calls { get; } = new();
        public bool Throw { get; set; }

        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action, KejiAuditOutcome outcome,
            KejiAuditSeverity severity, string targetType, string? targetId = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("audit failure");
            Calls.Add(new AuditCall(action, outcome, metadata ?? new Dictionary<string, string>()));
            return Task.FromResult(KejiAuditResult.Written);
        }
    }

    private sealed record AuditCall(string Action, KejiAuditOutcome Outcome, IReadOnlyDictionary<string, string> Metadata);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = new();
        private DateTimeOffset _utcNow = DateTimeOffset.UnixEpoch;
        private long _timestamp;

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan value)
        {
            _utcNow += value;
            _timestamp += (long)(value.TotalSeconds * TimestampFrequency);
            foreach (var timer in _timers.ToArray()) timer.FireIfDue(value);
        }

        private sealed class ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period) : ITimer
        {
            private TimeSpan _remaining = dueTime;
            private TimeSpan _period = period;
            private bool _disposed;

            public bool Change(TimeSpan due, TimeSpan repeat)
            { _remaining = due; _period = repeat; return !_disposed; }

            public void FireIfDue(TimeSpan elapsed)
            {
                if (_disposed || _remaining == Timeout.InfiniteTimeSpan) return;
                _remaining -= elapsed;
                if (_remaining > TimeSpan.Zero) return;
                callback(state);
                _remaining = _period;
            }

            public void Dispose() { _disposed = true; owner._timers.Remove(this); }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> ProviderEvents(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken,
        params ChatCompletionStreamEvent[] events)
    {
        foreach (var item in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> DelayedProviderEvents(
        Task release,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await release.WaitAsync(cancellationToken);
        yield return ChatCompletionStreamEvent.Token("done");
        yield return ChatCompletionStreamEvent.ChoiceFinished(KejiFinishReason.Stop, false);
        yield return ChatCompletionStreamEvent.Done();
    }

    private static async IAsyncEnumerable<ChatCompletionStreamEvent> NeverCompletingProvider(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source) result.Add(item);
        return result;
    }

    private sealed class FakeToolPipeline : IToolExecutionPipeline
    {
        public List<string> Calls { get; } = new();
        public Func<string, IReadOnlyDictionary<string, object?>?, CancellationToken, Task<ToolExecutionResult>> Handler { get; set; } =
            static (_, _, _) => Task.FromResult(ToolExecutionResult.Successful("{}", TimeSpan.Zero));

        public Task<ToolExecutionResult> ExecuteAsync(string toolName, IReadOnlyDictionary<string, object?>? inputs, CancellationToken cancellationToken = default)
        {
            Calls.Add(toolName);
            return Handler(toolName, inputs, cancellationToken);
        }
    }
}
