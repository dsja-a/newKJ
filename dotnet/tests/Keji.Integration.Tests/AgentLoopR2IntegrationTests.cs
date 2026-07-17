using System.Collections.Immutable;
using System.Text.Json;
using Keji.Agent;
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

namespace Keji.Integration.Tests;

public sealed class AgentLoopR2IntegrationTests
{
    [Fact]
    public async Task DirectStreamingAnswer_ReachesTask012SseDone()
    {
        var fixture = Fixture(Answer("hello"));
        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(fixture.Loop.RunStreamAsync(Request())));
        Assert.Contains(frames, static frame => frame.Contains("event: answer\n", StringComparison.Ordinal));
        Assert.Contains("event: done\n", frames[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReasoningThenAnswer_StreamsBothPhases()
    {
        var fixture = Fixture(new[] { ChatCompletionStreamEvent.ReasoningToken("think"), ChatCompletionStreamEvent.Token("answer"), Finish(), ChatCompletionStreamEvent.Done() });
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.ThinkingDelta);
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.AnswerDelta);
    }

    [Fact]
    public async Task CalculatorTool_ExecutesThenProviderAnswers()
    {
        var fixture = Fixture(ToolRound("call_1", "calculator", 0), Answer("42"));
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(new[] { "calculator" }, fixture.Pipeline.Calls);
        Assert.Equal("42", events[^1].Transcript!.FinalContent);
    }

    [Fact]
    public async Task GetTimeTool_ExecutesThenProviderAnswers()
    {
        var fixture = Fixture(ToolRound("call_1", "get_time", 0), Answer("now"));
        await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(new[] { "get_time" }, fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task TwoTools_ExecuteInToolCallIndexOrder()
    {
        var round = ToolRound(("call_2", "get_time", 1), ("call_1", "calculator", 0));
        var fixture = Fixture(round, Answer("done"));
        await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(new[] { "calculator", "get_time" }, fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task ContractOnlyTool_IsRejectedBeforePipeline()
    {
        var fixture = Fixture(ToolRound("call_1", "contract_only", 0));
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Empty(fixture.Pipeline.Calls);
        Assert.Equal(KejiAgentErrorCode.ToolRejected, events[^2].ErrorCode);
    }

    [Fact]
    public async Task UnauthorizedToolFailure_TerminatesSafelyAndNeverLeaksRawError()
    {
        var fixture = Fixture(ToolRound("call_1", "calculator", 0), Answer("denied handled"));
        fixture.Pipeline.Result = ToolExecutionResult.Failed("raw authorization secret", "UNAUTHORIZED");
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.DoesNotContain("raw authorization secret", JsonSerializer.Serialize(events), StringComparison.Ordinal);
        Assert.Contains(events, static item => item.Type == KejiAgentEventType.ToolCompleted && item.ToolErrorCode == "UNAUTHORIZED");
        Assert.Equal(KejiAgentErrorCode.ToolRejected, events[^2].ErrorCode);
    }

    [Fact]
    public async Task ProviderError_MapsToErrorThenDoneSse()
    {
        var fixture = Fixture(new[] { ChatCompletionStreamEvent.Error(KejiProviderErrorCode.ServerError, "secret") });
        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(fixture.Loop.RunStreamAsync(Request())));
        Assert.Contains("event: error\n", frames[^2], StringComparison.Ordinal);
        Assert.Contains("event: done\n", frames[^1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_ProducesNoErrorOrDone()
    {
        var fixture = Fixture(Array.Empty<ChatCompletionStreamEvent>());
        fixture.Provider.Block = true;
        using var cts = new CancellationTokenSource();
        var events = new List<KejiAgentEvent>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in fixture.Loop.RunStreamAsync(Request(), cts.Token))
            {
                events.Add(item);
                cts.Cancel();
            }
        });
        Assert.DoesNotContain(events, static item => item.Type is KejiAgentEventType.Error or KejiAgentEventType.RunCompleted);
    }

    [Fact]
    public async Task SameSessionConcurrentRun_ReturnsBusyTerminal()
    {
        var gate = new KejiAgentSessionGate();
        var first = Fixture(gate, Array.Empty<ChatCompletionStreamEvent>());
        first.Provider.Block = true;
        await using var enumerator = first.Loop.RunStreamAsync(Request()).GetAsyncEnumerator();
        Assert.True(await enumerator.MoveNextAsync());
        var second = Fixture(gate, Answer("second"));
        var events = await CollectAsync(second.Loop.RunStreamAsync(Request()));
        Assert.Equal(KejiAgentErrorCode.SessionBusy, events[^2].ErrorCode);
        first.Provider.Release.SetResult();
        while (await enumerator.MoveNextAsync()) { }
    }

    [Fact]
    public async Task CompletedSessionGate_CanBeReused()
    {
        var gate = new KejiAgentSessionGate();
        await CollectAsync(Fixture(gate, Answer("one")).Loop.RunStreamAsync(Request()));
        var events = await CollectAsync(Fixture(gate, Answer("two")).Loop.RunStreamAsync(Request()));
        Assert.Equal(KejiAgentStopReason.Completed, events[^1].StopReason);
    }

    [Fact]
    public async Task MultipleProviderRounds_EmitOneFinalUsage()
    {
        var first = InsertBeforeDone(ToolRound("call_1", "calculator", 0), ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 2, CompletionTokens = 1 }));
        var second = InsertBeforeDone(Answer("answer"), ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 3, CompletionTokens = 2 }));
        var events = await CollectAsync(Fixture(first, second).Loop.RunStreamAsync(Request()));
        var usage = Assert.Single(events, static item => item.Type == KejiAgentEventType.Usage).Usage!;
        Assert.Equal(5, usage.PromptTokens);
        Assert.Equal(3, usage.CompletionTokens);
    }

    [Fact]
    public async Task AgentSequence_IsContinuousAcrossToolsAndRounds()
    {
        var events = await CollectAsync(Fixture(ToolRound("call_1", "calculator", 0), Answer("answer")).Loop.RunStreamAsync(Request()));
        Assert.Equal(Enumerable.Range(1, events.Count).Select(static value => (long)value), events.Select(static item => item.Sequence));
    }

    [Fact]
    public async Task Sse_DoesNotLeakArgumentsResultsOrProviderSecret()
    {
        var fixture = Fixture(ToolRound("call_1", "calculator", 0, "{\"expression\":\"sensitive-argument\"}"), Answer("safe"));
        fixture.Pipeline.Result = ToolExecutionResult.Successful("sensitive-result", TimeSpan.Zero);
        var wire = string.Concat(await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(fixture.Loop.RunStreamAsync(Request()))));
        Assert.DoesNotContain("sensitive-argument", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-result", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossUserConversation_IsRejectedBeforeProvider()
    {
        var fixture = Fixture(Answer("unused"));
        fixture.Conversations.Owned = false;
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(KejiAgentErrorCode.ConversationNotFound, events[^2].ErrorCode);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task ContextLimit_DoesNotPersistOrCallProvider()
    {
        var fixture = Fixture(new AgentLoopOptions(maxContextMessages: 2), Answer("unused"));
        fixture.Messages.History.Add(new MessageRecord { Role = "user", Content = "old" });
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(KejiAgentErrorCode.ContextLimit, events[^2].ErrorCode);
        Assert.Empty(fixture.Messages.Writes);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task AuditFailure_DoesNotPreventSuccessfulCompletion()
    {
        var fixture = Fixture(Answer("answer"));
        fixture.Audit.Throw = true;
        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));
        Assert.Equal(KejiAgentStopReason.Completed, events[^1].StopReason);
    }

    [Fact]
    public async Task InvalidRunId_StillProducesSafeErrorThenDoneSse()
    {
        var request = new KejiAgentRunRequest
        {
            RunId = "invalid/run-id", ConversationId = "conv_1", ProviderName = "openai",
            Model = "model", UserMessage = "hello",
        };
        var fixture = Fixture(Answer("unused"));

        var frames = await CollectAsync(new KejiAgentSseAdapter().AdaptAsync(fixture.Loop.RunStreamAsync(request)));

        Assert.Contains("event: error\n", frames[^2], StringComparison.Ordinal);
        Assert.Contains("event: done\n", frames[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("invalid/run-id", string.Concat(frames), StringComparison.Ordinal);
        Assert.Empty(fixture.Provider.Requests);
    }

    [Fact]
    public async Task ChoiceFinishedThenLateContent_IsProviderProtocolError()
    {
        var fixture = Fixture(new[]
        {
            Finish(), ChatCompletionStreamEvent.Token("late"), ChatCompletionStreamEvent.Done(),
        });

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.Equal(KejiAgentErrorCode.ProviderProtocolError, events[^2].ErrorCode);
        Assert.Equal(KejiAgentEventType.RunCompleted, events[^1].Type);
    }

    [Fact]
    public async Task ContractOnly_RejectionHasToolCompletedButNoToolStarted()
    {
        var fixture = Fixture(ToolRound("call_1", "contract_only", 0));

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.Equal(new[]
        {
            KejiAgentEventType.ToolCompleted, KejiAgentEventType.Error, KejiAgentEventType.RunCompleted,
        }, events.Where(static item => item.Type is KejiAgentEventType.ToolStarted or
                KejiAgentEventType.ToolCompleted or KejiAgentEventType.Error or KejiAgentEventType.RunCompleted)
            .Select(static item => item.Type));
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task InvalidArguments_RejectionHasToolCompletedErrorDone()
    {
        var fixture = Fixture(ToolRound("call_1", "calculator", 0, "{\"nested\":{\"value\":1}}"));

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.DoesNotContain(events, static item => item.Type == KejiAgentEventType.ToolStarted);
        Assert.Equal(new[]
        {
            KejiAgentEventType.ToolCompleted, KejiAgentEventType.Error, KejiAgentEventType.RunCompleted,
        }, events.Where(static item => item.Type is KejiAgentEventType.ToolCompleted or
                KejiAgentEventType.Error or KejiAgentEventType.RunCompleted)
            .Select(static item => item.Type));
        Assert.Empty(fixture.Pipeline.Calls);
    }

    [Fact]
    public async Task OwnershipRevokedBeforeTool_HasNoToolEventsAndNoPipeline()
    {
        var fixture = Fixture(ToolRound("call_1", "calculator", 0));
        fixture.Conversations.RevokeAfterSuccessfulChecks = 3;

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.DoesNotContain(events, static item => item.Type is KejiAgentEventType.ToolStarted or KejiAgentEventType.ToolCompleted);
        Assert.Empty(fixture.Pipeline.Calls);
        Assert.Equal(KejiAgentErrorCode.ConversationNotFound, events[^2].ErrorCode);
    }

    [Fact]
    public async Task ProviderErrorAfterChoiceFinished_ProducesMappedErrorAndDone()
    {
        var fixture = Fixture(new[]
        {
            Finish(), ChatCompletionStreamEvent.Error(KejiProviderErrorCode.AuthFailed, "raw-secret"),
        });

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.Equal(KejiAgentErrorCode.ProviderRejected, events[^2].ErrorCode);
        Assert.Equal(KejiAgentEventType.RunCompleted, events[^1].Type);
        Assert.DoesNotContain("raw-secret", JsonSerializer.Serialize(events), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderErrorAfterUsage_ProducesUsageMappedErrorDone()
    {
        var fixture = Fixture(new[]
        {
            Finish(), ChatCompletionStreamEvent.UsageEvent(new TokenUsage { PromptTokens = 2, CompletionTokens = 1 }),
            ChatCompletionStreamEvent.Error(KejiProviderErrorCode.ServiceUnavailable, "raw-secret"),
        });

        var events = await CollectAsync(fixture.Loop.RunStreamAsync(Request()));

        Assert.Equal(new[]
        {
            KejiAgentEventType.Usage, KejiAgentEventType.Error, KejiAgentEventType.RunCompleted,
        }, events.TakeLast(3).Select(static item => item.Type));
        Assert.Equal(KejiAgentErrorCode.ProviderUnavailable, events[^2].ErrorCode);
    }

    private static KejiAgentRunRequest Request() => new()
    {
        RunId = Guid.NewGuid().ToString("N"), ConversationId = "conv_1", ProviderName = "openai",
        Model = "model", UserMessage = "hello",
    };

    private static ChatCompletionStreamEvent Finish(bool tools = false) =>
        ChatCompletionStreamEvent.ChoiceFinished(tools ? KejiFinishReason.ToolCalls : KejiFinishReason.Stop, tools);

    private static ChatCompletionStreamEvent[] Answer(string answer) =>
        [ChatCompletionStreamEvent.Token(answer), Finish(), ChatCompletionStreamEvent.Done()];

    private static ChatCompletionStreamEvent[] ToolRound(string id, string name, int index, string arguments = "{}") =>
        ToolRound((id, name, index, arguments));

    private static ChatCompletionStreamEvent[] ToolRound(params (string Id, string Name, int Index)[] calls) =>
        ToolRound(calls.Select(static value => (value.Id, value.Name, value.Index, "{}")).ToArray());

    private static ChatCompletionStreamEvent[] ToolRound(params (string Id, string Name, int Index, string Arguments)[] calls)
    {
        var events = new List<ChatCompletionStreamEvent>();
        foreach (var call in calls)
        {
            events.Add(ChatCompletionStreamEvent.ToolCallBegin(call.Id, call.Name, call.Index));
            events.Add(ChatCompletionStreamEvent.ToolCallDelta(call.Arguments, call.Index, toolCallId: call.Id));
            events.Add(ChatCompletionStreamEvent.ToolCallEnd(call.Id, call.Index));
        }
        events.Add(Finish(true));
        events.Add(ChatCompletionStreamEvent.Done());
        return events.ToArray();
    }

    private static ChatCompletionStreamEvent[] InsertBeforeDone(ChatCompletionStreamEvent[] events, ChatCompletionStreamEvent item) =>
        events[..^1].Append(item).Append(events[^1]).ToArray();

    private static FixtureState Fixture(params ChatCompletionStreamEvent[][] rounds) => Fixture(new AgentLoopOptions(), new KejiAgentSessionGate(), rounds);
    private static FixtureState Fixture(AgentLoopOptions options, params ChatCompletionStreamEvent[][] rounds) => Fixture(options, new KejiAgentSessionGate(), rounds);
    private static FixtureState Fixture(IKejiAgentSessionGate gate, params ChatCompletionStreamEvent[][] rounds) => Fixture(new AgentLoopOptions(), gate, rounds);

    private static FixtureState Fixture(AgentLoopOptions options, IKejiAgentSessionGate gate, params ChatCompletionStreamEvent[][] rounds)
    {
        var user = new UserAccessor { CurrentUser = new CurrentUser("user_1", "user", "member", "User", KejiAuthenticationKind.Jwt) };
        var conversations = new ConversationRepo();
        var messages = new MessageRepo();
        var provider = new StreamingProvider(rounds);
        var providers = new ModelProviderRegistry([new KeyValuePair<string, IModelProvider>("openai", provider)]);
        var tools = Tools();
        var pipeline = new Pipeline();
        var audit = new Audit();
        var loop = new AgentLoop(user, conversations, messages, providers, tools, pipeline, options, audit, gate);
        return new FixtureState(loop, conversations, messages, provider, pipeline, audit);
    }

    private static IKejiToolRegistry Tools()
    {
        var builder = new KejiToolRegistryBuilder();
        builder.Register(Tool("calculator", KejiToolAvailability.Executable));
        builder.Register(Tool("get_time", KejiToolAvailability.Executable));
        builder.Register(Tool("contract_only", KejiToolAvailability.ContractOnly));
        return builder.Build();
    }

    private static KejiToolDefinition Tool(string name, KejiToolAvailability availability) => new(
        KejiToolName.Create(name), 1, "integration", KejiToolCategory.Utility, KejiToolRiskLevel.ReadOnly,
        KejiToolExecutionTarget.ToolWorker, KejiPermission.ToolExecuteRead,
        new KejiToolInputSchema(Array.Empty<KejiToolParameterDefinition>()), availability);

    private sealed record FixtureState(AgentLoop Loop, ConversationRepo Conversations, MessageRepo Messages,
        StreamingProvider Provider, Pipeline Pipeline, Audit Audit);

    private sealed class UserAccessor : ICurrentUserAccessor { public CurrentUser? CurrentUser { get; set; } }

    private sealed class ConversationRepo : IConversationRepository
    {
        public bool Owned { get; set; } = true;
        public int Checks { get; private set; }
        public int? RevokeAfterSuccessfulChecks { get; set; }
        public Task<ConversationRecord?> GetOwnedAsync(string id, string user, CancellationToken ct = default) =>
            Task.FromResult<ConversationRecord?>(Owned &&
                (!RevokeAfterSuccessfulChecks.HasValue || ++Checks <= RevokeAfterSuccessfulChecks.Value)
                ? new ConversationRecord { Id = id, OwnerUserId = user }
                : null);
        public Task<ConversationRecord> CreateOwnedAsync(string a, string b, string c = "New", CancellationToken d = default) => throw new NotSupportedException();
        public Task<(ConversationRecord Record, ConversationOwnershipResult Result)> EnsureOwnedAsync(string a, string b, string c = "New", CancellationToken d = default) => throw new NotSupportedException();
        public Task<List<ConversationRecord>> ListOwnedAsync(string a, int b = 50, CancellationToken c = default) => throw new NotSupportedException();
        public Task<bool> RenameOwnedAsync(string a, string b, string c, CancellationToken d = default) => throw new NotSupportedException();
        public Task<bool> DeleteOwnedAsync(string a, string b, CancellationToken c = default) => throw new NotSupportedException();
        public Task<int> CountByOwnerAsync(string a, CancellationToken b = default) => throw new NotSupportedException();
    }

    private sealed class MessageRepo : IMessageRepository
    {
        public List<MessageRecord> History { get; } = new();
        public List<(string Role, string Content)> Writes { get; } = new();
        public Task<long> AddOwnedAsync(string a, string b, string role, string content, CancellationToken c = default)
        { Writes.Add((role, content)); return Task.FromResult((long)Writes.Count); }
        public Task<List<MessageRecord>> ListOwnedMessagesAsync(string a, string b, int limit = 100, CancellationToken c = default) =>
            Task.FromResult(History.TakeLast(limit).ToList());
    }

    private sealed class StreamingProvider(IEnumerable<ChatCompletionStreamEvent[]> rounds) : IModelProvider
    {
        private readonly Queue<ChatCompletionStreamEvent[]> _rounds = new(rounds);
        public string ProviderName => "openai";
        public List<ChatCompletionRequest> Requests { get; } = new();
        public bool Block { get; set; }
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default) => throw new InvalidOperationException();
        public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(ChatCompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);
            if (Block) await Release.Task.WaitAsync(ct);
            foreach (var item in _rounds.Dequeue()) { ct.ThrowIfCancellationRequested(); yield return item; }
        }
    }

    private sealed class Pipeline : IToolExecutionPipeline
    {
        public List<string> Calls { get; } = new();
        public ToolExecutionResult Result { get; set; } = ToolExecutionResult.Successful("{}", TimeSpan.Zero);
        public Task<ToolExecutionResult> ExecuteAsync(string name, IReadOnlyDictionary<string, object?>? inputs, CancellationToken ct = default)
        { Calls.Add(name); return Task.FromResult(Result); }
    }

    private sealed class Audit : IKejiAuditService
    {
        public bool Throw { get; set; }
        public Task<KejiAuditResult> WriteAsync(KejiAuditCategory a, string b, KejiAuditOutcome c, KejiAuditSeverity d,
            string e, string? f = null, IReadOnlyDictionary<string, string>? g = null, CancellationToken h = default)
        { if (Throw) throw new InvalidOperationException(); return Task.FromResult(KejiAuditResult.Written); }
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    { var result = new List<T>(); await foreach (var item in source) result.Add(item); return result; }
}
