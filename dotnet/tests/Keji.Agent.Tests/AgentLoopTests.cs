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

        Assert.Equal(AgentRunStatus.ToolFailed, result.Status);
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
    public async Task RunStreamAsync_EmitsOrderedRunTranscriptAndUtcTimes()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("hello stream", new TokenUsage { PromptTokens = 3, CompletionTokens = 2 }));

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));

        Assert.Equal(Enumerable.Range(1, events.Count).Select(static value => (long)value), events.Select(static item => item.Sequence));
        Assert.Single(events.Select(static item => item.RunId).Distinct());
        Assert.All(events, static item => Assert.Equal(TimeSpan.Zero, item.TimestampUtc.Offset));
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.AssistantDelta && item.ContentDelta == "hello stream");
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

        Assert.Equal(KejiAgentErrorCode.LimitExceeded, error.ErrorCode);
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
        var fixture = Fixture(new AgentLoopOptions(runTimeout: TimeSpan.FromSeconds(1)), ChatCompletionResponse.Succeeded("unused"));
        fixture.Provider.StreamHandler = (_, ct) => NeverCompletingProvider(ct);

        var error = Assert.Single(await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest())), static item => item.Type == KejiAgentEventType.Error);

        Assert.Equal(KejiAgentErrorCode.RunTimedOut, error.ErrorCode);
        Assert.Equal(KejiAgentStopReason.TimedOut, error.StopReason);
    }

    [Fact]
    public async Task RunStreamAsync_WritesSafeAgentAudit()
    {
        var fixture = Fixture(ChatCompletionResponse.Succeeded("audited"));

        await CollectAsync(fixture.Loop.RunStreamAsync(StreamRequest()));

        var audit = Assert.Single(fixture.Audit.Calls);
        Assert.Equal("agent_run", audit.Action);
        Assert.Equal(KejiAuditOutcome.Success, audit.Outcome);
        Assert.Equal("completed", audit.Metadata["result_code"]);
        Assert.DoesNotContain("audited", JsonSerializer.Serialize(audit), StringComparison.Ordinal);
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

    private static TestFixture Fixture(AgentLoopOptions options, IKejiAgentSessionGate gate, params ChatCompletionResponse[] responses)
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
        var loop = new AgentLoop(user, conversations, messages, registry, tools, pipeline, options, audit, gate);
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

        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory category, string action, KejiAuditOutcome outcome,
            KejiAuditSeverity severity, string targetType, string? targetId = null,
            IReadOnlyDictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new AuditCall(action, outcome, metadata ?? new Dictionary<string, string>()));
            return Task.FromResult(KejiAuditResult.Written);
        }
    }

    private sealed record AuditCall(string Action, KejiAuditOutcome Outcome, IReadOnlyDictionary<string, string> Metadata);

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
