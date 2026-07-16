using System.Buffers;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Keji.Auditing.Abstractions;
using Keji.Auditing.Models;
using Keji.Persistence.Repositories;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.Agent;

public sealed class AgentLoop : IAgentLoop, IKejiAgentLoop
{
    private const int MaxConversationIdLength = 128;
    private const int MaxProviderNameLength = 32;
    private const int MaxModelLength = 256;
    private const int MaxUserMessageBytes = 64 * 1024;
    private const int MaxAssistantBytes = 256 * 1024;
    private const int MaxToolArgumentsBytes = 64 * 1024;
    private const int MaxToolArgumentProperties = 64;

    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly IModelProviderRegistry _providers;
    private readonly IKejiToolRegistry _tools;
    private readonly IToolExecutionPipeline _toolPipeline;
    private readonly AgentLoopOptions _options;
    private readonly IKejiAuditService? _audit;
    private readonly IKejiAgentSessionGate _sessionGate;

    public AgentLoop(
        ICurrentUserAccessor userAccessor,
        IConversationRepository conversations,
        IMessageRepository messages,
        IModelProviderRegistry providers,
        IKejiToolRegistry tools,
        IToolExecutionPipeline toolPipeline,
        AgentLoopOptions options,
        IKejiAuditService? audit = null,
        IKejiAgentSessionGate? sessionGate = null)
    {
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _toolPipeline = toolPipeline ?? throw new ArgumentNullException(nameof(toolPipeline));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit;
        _sessionGate = sessionGate ?? new KejiAgentSessionGate();
    }

    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        KejiAgentEvent? terminal = null;
        var iterations = 0;
        var toolCalls = 0;
        await foreach (var item in RunStreamAsync(ToKejiRequest(request), cancellationToken).ConfigureAwait(false))
        {
            iterations = Math.Max(iterations, item.Iteration);
            if (item.Type == KejiAgentEventType.ToolCompleted)
                toolCalls++;
            if (item.Type is KejiAgentEventType.RunCompleted or KejiAgentEventType.Error)
                terminal = item;
        }
        if (terminal?.Type == KejiAgentEventType.RunCompleted && terminal.Transcript is { } transcript)
            return AgentRunResult.Completed(transcript.AssistantContent, transcript.Iterations, transcript.ToolCalls);
        return AgentRunResult.Failed(MapLegacyStatus(terminal?.ErrorCode ?? KejiAgentErrorCode.InternalFailure), iterations, toolCalls);
    }

    public IAsyncEnumerable<KejiAgentEvent> RunStreamAsync(
        KejiAgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var channel = Channel.CreateBounded<KejiAgentEvent>(new BoundedChannelOptions(_options.EventBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(_options.RunTimeout);
        var producer = ProduceAsync(request, channel.Writer, cancellationToken, linked.Token);
        return ReadEventsAsync(channel.Reader, producer, linked, cancellationToken);
    }

    private async Task ProduceAsync(
        KejiAgentRunRequest request,
        ChannelWriter<KejiAgentEvent> writer,
        CancellationToken callerToken,
        CancellationToken runToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        var started = DateTimeOffset.UtcNow;
        var state = new RunState();
        long sequence = 0;
        IDisposable? lease = null;
        async ValueTask EmitAsync(KejiAgentEvent item) =>
            await writer.WriteAsync(item with
            {
                RunId = runId,
                Sequence = ++sequence,
                TimestampUtc = DateTimeOffset.UtcNow,
            }, runToken).ConfigureAwait(false);

        try
        {
            if (!TryValidateRequest(request))
                throw new AgentFailureException(KejiAgentErrorCode.InvalidRequest);

            var user = _userAccessor.CurrentUser;
            if (!IsValidUser(user))
                throw new AgentFailureException(KejiAgentErrorCode.Unauthenticated);
            lease = _sessionGate.TryEnter(user!.Id, request.ConversationId);
            if (lease is null)
                throw new AgentFailureException(KejiAgentErrorCode.SessionBusy);
            await EmitAsync(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.RunStarted, TimestampUtc = started });
            await RunCoreStreamAsync(request, user.Id, runId, started, state, EmitAsync, runToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            writer.TryComplete(new OperationCanceledException(callerToken));
            return;
        }
        catch (OperationCanceledException)
        {
            await TryEmitTerminalErrorAsync(KejiAgentErrorCode.RunTimedOut, KejiAgentStopReason.TimedOut).ConfigureAwait(false);
        }
        catch (AgentFailureException exception)
        {
            await TryEmitTerminalErrorAsync(exception.Code, exception.StopReason ?? StopReasonFor(exception.Code)).ConfigureAwait(false);
        }
        catch (Persistence.KejiPersistenceException)
        {
            await TryEmitTerminalErrorAsync(KejiAgentErrorCode.PersistenceFailed, KejiAgentStopReason.Failed).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await TryEmitTerminalErrorAsync(KejiAgentErrorCode.InternalFailure, KejiAgentStopReason.Failed).ConfigureAwait(false);
        }
        finally
        {
            lease?.Dispose();
            writer.TryComplete();
        }

        async Task TryEmitTerminalErrorAsync(KejiAgentErrorCode code, KejiAgentStopReason reason)
        {
            try
            {
                var completedAt = DateTimeOffset.UtcNow;
                var transcript = new KejiAgentTranscript(runId, request.ConversationId, started, completedAt,
                    reason, state.Iterations, state.ToolCalls, state.Content.ToString(), state.Usage);
                writer.TryWrite(new KejiAgentEvent
                {
                    RunId = runId,
                    Sequence = ++sequence,
                    Type = KejiAgentEventType.Error,
                    TimestampUtc = completedAt,
                    Iteration = state.Iterations,
                    ErrorCode = code,
                    StopReason = reason,
                    Usage = state.Usage,
                    Transcript = transcript,
                });
                await WriteAuditAsync(request.ConversationId, runId, false, code, CancellationToken.None).ConfigureAwait(false);
            }
            catch (ChannelClosedException) { }
        }
    }

    private async Task RunCoreStreamAsync(
        KejiAgentRunRequest request,
        string userId,
        string runId,
        DateTimeOffset started,
        RunState state,
        Func<KejiAgentEvent, ValueTask> emit,
        CancellationToken cancellationToken)
    {
        if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
            throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);

        var provider = _providers.GetProvider(request.ProviderName);
        if (provider is null)
            throw new AgentFailureException(KejiAgentErrorCode.ProviderNotFound);

        var history = await _messages.ListOwnedMessagesAsync(
            request.ConversationId,
            userId,
            _options.MaxContextMessages + 1,
            cancellationToken).ConfigureAwait(false);
        if (!TryBuildContext(history, request.UserMessage, out var context))
            throw new AgentFailureException(KejiAgentErrorCode.ContextLimit);

        if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
            throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
        await _messages.AddOwnedAsync(
            request.ConversationId,
            userId,
            "user",
            request.UserMessage,
            cancellationToken).ConfigureAwait(false);

        var advertisedTools = BuildAdvertisedTools();
        var toolCalls = 0;
        var totalToolResultBytes = 0;
        var iterations = 0;
        var recoveryAttempts = 0;
        var finalContent = new StringBuilder();
        var usage = new KejiAgentUsage();

        while (++iterations <= _options.MaxIterations)
        {
            state.Iterations = iterations;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
            await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.IterationStarted, TimestampUtc = default, Iteration = iterations });
            var iterationContent = new StringBuilder();
            var streamedCalls = new SortedDictionary<int, StreamedToolCall>();
            var finishReason = KejiFinishReason.Invalid;
            var hasToolCalls = false;
            var sawDone = false;

            var providerRequest = new ChatCompletionRequest
            {
                Model = request.Model,
                Messages = context.ToImmutableArray(),
                Tools = advertisedTools,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
            };
            try
            {
                await foreach (var item in provider.StreamAsync(providerRequest, cancellationToken)
                                   .WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    switch (item.Type)
                    {
                        case KejiProviderStreamEventKind.ReasoningToken:
                            break;
                        case KejiProviderStreamEventKind.Token:
                            if (item.ChoiceIndex != 0 || string.IsNullOrEmpty(item.Content) || item.Content.Contains('\0') ||
                                Encoding.UTF8.GetByteCount(iterationContent.ToString()) + Encoding.UTF8.GetByteCount(item.Content) > MaxAssistantBytes)
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            iterationContent.Append(item.Content);
                            finalContent.Append(item.Content);
                            state.Content.Append(item.Content);
                            await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.AssistantDelta, TimestampUtc = default, Iteration = iterations, ContentDelta = item.Content });
                            break;
                        case KejiProviderStreamEventKind.ToolCallBegin:
                            if (item.ChoiceIndex != 0 || streamedCalls.ContainsKey(item.ToolCallIndex) ||
                                !IsSafeIdentifier(item.ToolCallId ?? string.Empty, 256) ||
                                !IsSafeIdentifier(item.ToolName ?? string.Empty, 64))
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            streamedCalls.Add(item.ToolCallIndex, new StreamedToolCall(item.ToolCallId!, item.ToolName!));
                            await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.ToolStarted, TimestampUtc = default, Iteration = iterations, ToolCallId = item.ToolCallId, ToolName = item.ToolName });
                            break;
                        case KejiProviderStreamEventKind.ToolCallDelta:
                            if (!streamedCalls.TryGetValue(item.ToolCallIndex, out var streamed) || item.ChoiceIndex != 0 || item.ToolArguments is null)
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            streamed.Append(item.ToolArguments, MaxToolArgumentsBytes);
                            break;
                        case KejiProviderStreamEventKind.ToolCallEnd:
                            if (!streamedCalls.TryGetValue(item.ToolCallIndex, out var ended) || ended.Ended ||
                                item.ToolCallId is not null && !string.Equals(item.ToolCallId, ended.Id, StringComparison.Ordinal))
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            ended.Ended = true;
                            break;
                        case KejiProviderStreamEventKind.ChoiceFinished:
                            if (item.ChoiceIndex != 0 || finishReason != KejiFinishReason.Invalid)
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            finishReason = item.FinishReason;
                            hasToolCalls = item.HasToolCalls;
                            break;
                        case KejiProviderStreamEventKind.Usage:
                            if (item.Usage is null || item.Usage.PromptTokens < 0 || item.Usage.CompletionTokens < 0 || item.Usage.CachedTokens < 0)
                                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                            usage = usage.Add(item.Usage.PromptTokens, item.Usage.CompletionTokens, item.Usage.CachedTokens);
                            state.Usage = usage;
                            await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.Usage, TimestampUtc = default, Iteration = iterations, Usage = usage });
                            break;
                        case KejiProviderStreamEventKind.Error:
                            throw new AgentFailureException(MapProviderError(item.ErrorCode));
                        case KejiProviderStreamEventKind.Done:
                            sawDone = true;
                            break;
                        default:
                            throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                    }
                }
            }
            catch (AgentFailureException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new AgentFailureException(KejiAgentErrorCode.ProviderUnavailable); }

            if (!sawDone || finishReason == KejiFinishReason.Invalid)
                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);

            var responseToolCalls = BuildToolCalls(streamedCalls);
            if (!hasToolCalls)
            {
                if (!responseToolCalls.IsDefaultOrEmpty)
                    throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                if ((finishReason == KejiFinishReason.Length || string.IsNullOrWhiteSpace(iterationContent.ToString())) &&
                    recoveryAttempts++ < _options.MaxRecoveryAttempts)
                {
                    if (iterationContent.Length > 0)
                        context.Add(new ChatMessage { Role = KejiChatRole.Assistant, Content = iterationContent.ToString() });
                    context.Add(new ChatMessage { Role = KejiChatRole.User, Content = "Continue the answer without repeating prior content." });
                    if (!TryEnforceContextBounds(context))
                        throw new AgentFailureException(KejiAgentErrorCode.ContextLimit);
                    continue;
                }
                if (finishReason == KejiFinishReason.Length)
                    throw new AgentFailureException(KejiAgentErrorCode.LimitExceeded, KejiAgentStopReason.Length);
                if (finishReason == KejiFinishReason.ContentFilter)
                    throw new AgentFailureException(KejiAgentErrorCode.ProviderRejected);
                if (!TryValidateAssistantContent(finalContent.ToString(), out var content))
                    throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
                if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                    throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
                await _messages.AddOwnedAsync(request.ConversationId, userId, "assistant", content, cancellationToken).ConfigureAwait(false);
                var completedAt = DateTimeOffset.UtcNow;
                var transcript = new KejiAgentTranscript(runId, request.ConversationId, started, completedAt,
                    KejiAgentStopReason.Completed, iterations, toolCalls, content, usage);
                await WriteAuditAsync(request.ConversationId, runId, true, KejiAgentErrorCode.None, cancellationToken).ConfigureAwait(false);
                await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.RunCompleted, TimestampUtc = completedAt, Iteration = iterations, StopReason = KejiAgentStopReason.Completed, Usage = usage, Transcript = transcript });
                return;
            }

            if (finishReason is not (KejiFinishReason.ToolCalls or KejiFinishReason.Stop) ||
                responseToolCalls.IsDefaultOrEmpty || responseToolCalls.Length > _options.MaxToolCalls - toolCalls)
                throw new AgentFailureException(KejiAgentErrorCode.ToolCallLimit);
            if (!TryValidateToolCalls(responseToolCalls))
                throw new AgentFailureException(KejiAgentErrorCode.ToolRejected);

            context.Add(new ChatMessage
            {
                Role = KejiChatRole.Assistant,
                Content = iterationContent.Length == 0 ? null : iterationContent.ToString(),
                ToolCalls = responseToolCalls,
            });

            foreach (var toolCall in responseToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                    throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
                if (!TryConvertArguments(toolCall.FunctionArguments, out var inputs))
                    throw new AgentFailureException(KejiAgentErrorCode.ToolRejected);

                ToolExecutionResult result;
                try { result = await _toolPipeline.ExecuteAsync(toolCall.FunctionName, inputs, cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { throw new AgentFailureException(KejiAgentErrorCode.ToolFailed); }
                toolCalls++;
                state.ToolCalls = toolCalls;

                if (!TryFormatToolResult(result, out var toolContent, out var resultBytes) ||
                    resultBytes > _options.MaxTotalToolResultBytes - totalToolResultBytes)
                    throw new AgentFailureException(KejiAgentErrorCode.LimitExceeded);
                totalToolResultBytes += resultBytes;
                context.Add(new ChatMessage
                {
                    Role = KejiChatRole.Tool,
                    ToolCallId = toolCall.Id,
                    Name = toolCall.FunctionName,
                    Content = toolContent,
                });
                await emit(new KejiAgentEvent { RunId = runId, Sequence = 0, Type = KejiAgentEventType.ToolCompleted, TimestampUtc = default, Iteration = iterations, ToolCallId = toolCall.Id, ToolName = toolCall.FunctionName, ToolSucceeded = result.Success, ToolErrorCode = result.Success ? null : SafeToolErrorCode(result.ErrorCode) });
            }

            if (!TryEnforceContextBounds(context))
                throw new AgentFailureException(KejiAgentErrorCode.ContextLimit);
        }
        throw new AgentFailureException(KejiAgentErrorCode.LimitExceeded, KejiAgentStopReason.IterationLimit);
    }

    private static async IAsyncEnumerable<KejiAgentEvent> ReadEventsAsync(
        ChannelReader<KejiAgentEvent> reader,
        Task producer,
        CancellationTokenSource linked,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            linked.Cancel();
            try { await producer.ConfigureAwait(false); } catch (OperationCanceledException) { }
            linked.Dispose();
        }
    }

    private async Task WriteAuditAsync(
        string conversationId,
        string runId,
        bool success,
        KejiAgentErrorCode errorCode,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
            return;
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["run_id"] = runId,
            ["result_code"] = success ? "completed" : errorCode.ToString(),
        };
        try
        {
            await _audit.WriteAsync(
                KejiAuditCategory.DataAccess,
                "agent_run",
                success ? KejiAuditOutcome.Success : KejiAuditOutcome.Failure,
                success ? KejiAuditSeverity.Information : KejiAuditSeverity.Warning,
                "conversation",
                conversationId,
                metadata,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { }
    }

    private static ImmutableArray<ChatToolCall> BuildToolCalls(SortedDictionary<int, StreamedToolCall> streamed)
    {
        if (streamed.Count == 0)
            return ImmutableArray<ChatToolCall>.Empty;
        var result = ImmutableArray.CreateBuilder<ChatToolCall>(streamed.Count);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (index, call) in streamed)
        {
            if (index < 0 || !call.Ended || !ids.Add(call.Id))
                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
            try
            {
                using var document = JsonDocument.Parse(call.Arguments.ToString(), new JsonDocumentOptions { MaxDepth = 8 });
                result.Add(new ChatToolCall
                {
                    Id = call.Id,
                    FunctionName = call.Name,
                    FunctionArguments = document.RootElement.Clone(),
                });
            }
            catch (JsonException)
            {
                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
            }
        }
        return result.ToImmutable();
    }

    private static string SafeToolErrorCode(string? value) =>
        IsSafeIdentifier(value ?? string.Empty, 64) ? value! : "TOOL_FAILED";

    private static KejiAgentErrorCode MapProviderError(KejiProviderErrorCode code) => code switch
    {
        KejiProviderErrorCode.Timeout or KejiProviderErrorCode.GatewayTimeout => KejiAgentErrorCode.ProviderTimeout,
        KejiProviderErrorCode.AuthFailed or KejiProviderErrorCode.QuotaExceeded or
            KejiProviderErrorCode.InvalidRequest or KejiProviderErrorCode.RequestTooLarge => KejiAgentErrorCode.ProviderRejected,
        KejiProviderErrorCode.ServiceUnavailable or KejiProviderErrorCode.GatewayError or
            KejiProviderErrorCode.ServerError or KejiProviderErrorCode.ConnectionError or
            KejiProviderErrorCode.RateLimited => KejiAgentErrorCode.ProviderUnavailable,
        _ => KejiAgentErrorCode.ProviderProtocolError,
    };

    private static KejiAgentStopReason StopReasonFor(KejiAgentErrorCode code) => code switch
    {
        KejiAgentErrorCode.ProviderTimeout or KejiAgentErrorCode.RunTimedOut => KejiAgentStopReason.TimedOut,
        KejiAgentErrorCode.LimitExceeded or KejiAgentErrorCode.ContextLimit => KejiAgentStopReason.ContextLimit,
        KejiAgentErrorCode.ToolCallLimit => KejiAgentStopReason.ToolCallLimit,
        _ => KejiAgentStopReason.Failed,
    };

    private static AgentRunStatus MapLegacyStatus(KejiAgentErrorCode code) => code switch
    {
        KejiAgentErrorCode.InvalidRequest => AgentRunStatus.InvalidRequest,
        KejiAgentErrorCode.Unauthenticated => AgentRunStatus.Unauthenticated,
        KejiAgentErrorCode.ConversationNotFound => AgentRunStatus.ConversationNotFound,
        KejiAgentErrorCode.ProviderNotFound => AgentRunStatus.ProviderNotFound,
        KejiAgentErrorCode.ToolRejected or KejiAgentErrorCode.ToolFailed or
            KejiAgentErrorCode.ProviderProtocolError => AgentRunStatus.ToolFailed,
        KejiAgentErrorCode.LimitExceeded or KejiAgentErrorCode.ContextLimit or
            KejiAgentErrorCode.ToolCallLimit => AgentRunStatus.LimitExceeded,
        _ => AgentRunStatus.ProviderFailed,
    };

    private static KejiAgentRunRequest ToKejiRequest(AgentRunRequest request) => new()
    {
        ConversationId = request.ConversationId,
        ProviderName = request.ProviderName,
        Model = request.Model,
        UserMessage = request.UserMessage,
        Temperature = request.Temperature,
        MaxTokens = request.MaxTokens,
    };

    private sealed class StreamedToolCall(string id, string name)
    {
        public string Id { get; } = id;
        public string Name { get; } = name;
        public StringBuilder Arguments { get; } = new();
        public bool Ended { get; set; }

        public void Append(string value, int maxBytes)
        {
            if (Encoding.UTF8.GetByteCount(Arguments.ToString()) + Encoding.UTF8.GetByteCount(value) > maxBytes)
                throw new AgentFailureException(KejiAgentErrorCode.LimitExceeded);
            Arguments.Append(value);
        }
    }

    private sealed class AgentFailureException(
        KejiAgentErrorCode code,
        KejiAgentStopReason? stopReason = null) : Exception
    {
        public KejiAgentErrorCode Code { get; } = code;
        public KejiAgentStopReason? StopReason { get; } = stopReason;
    }

    private sealed class RunState
    {
        public int Iterations { get; set; }
        public int ToolCalls { get; set; }
        public StringBuilder Content { get; } = new();
        public KejiAgentUsage Usage { get; set; } = new();
    }

    private async Task<bool> IsStillOwnedAsync(string conversationId, string userId, CancellationToken ct)
    {
        var current = _userAccessor.CurrentUser;
        if (!IsValidUser(current) || !string.Equals(current!.Id, userId, StringComparison.Ordinal))
            return false;
        return await _conversations.GetOwnedAsync(conversationId, userId, ct).ConfigureAwait(false) is not null;
    }

    private static bool IsValidUser(CurrentUser? user) =>
        user is not null && !string.IsNullOrWhiteSpace(user.Id) && user.Id.Length <= 128 &&
        !user.Id.Any(char.IsControl);

    private static bool TryValidateRequest(KejiAgentRunRequest request)
    {
        if (!IsSafeIdentifier(request.ConversationId, MaxConversationIdLength) ||
            !IsSafeIdentifier(request.ProviderName, MaxProviderNameLength) ||
            request.Model.Length > MaxModelLength || request.Model.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(request.UserMessage) ||
            Encoding.UTF8.GetByteCount(request.UserMessage) > MaxUserMessageBytes ||
            request.UserMessage.Any(c => c == '\0') ||
            request.Temperature is < 0 or > 2 ||
            request.MaxTokens is < 1 or > 131072)
        {
            return false;
        }
        return true;
    }

    private static bool IsSafeIdentifier(string value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        value.All(static c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');

    private bool TryBuildContext(
        IReadOnlyList<Persistence.Models.MessageRecord> history,
        string userMessage,
        out List<ChatMessage> context)
    {
        context = new List<ChatMessage>(_options.MaxContextMessages + 1);
        var bytes = Encoding.UTF8.GetByteCount(userMessage);
        if (bytes > _options.MaxContextBytes)
            return false;

        if (history.Count > _options.MaxContextMessages - 1)
            return false;
        foreach (var record in history)
        {
            var role = record.Role switch
            {
                "user" => KejiChatRole.User,
                "assistant" => KejiChatRole.Assistant,
                _ => KejiChatRole.Invalid,
            };
            if (role == KejiChatRole.Invalid || record.Content is null)
                return false;
            var added = Encoding.UTF8.GetByteCount(record.Content);
            if (added > _options.MaxContextBytes - bytes)
                return false;
            bytes += added;
            context.Add(new ChatMessage { Role = role, Content = record.Content });
        }
        context.Add(new ChatMessage { Role = KejiChatRole.User, Content = userMessage });
        return true;
    }

    private ImmutableArray<ChatTool> BuildAdvertisedTools()
    {
        var builder = ImmutableArray.CreateBuilder<ChatTool>();
        foreach (var definition in _tools.GetAll()
                     .Where(static tool => tool.Availability == KejiToolAvailability.Executable)
                     .OrderBy(static tool => tool.Name.Value, StringComparer.Ordinal))
        {
            builder.Add(new ChatTool
            {
                Name = definition.Name.Value,
                Description = definition.Description,
                InputSchemaJson = BuildInputSchema(definition.InputSchema),
            });
        }
        return builder.ToImmutable();
    }

    private static string BuildInputSchema(KejiToolInputSchema schema)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WritePropertyName("properties");
        writer.WriteStartObject();
        foreach (var parameter in schema.Parameters)
        {
            writer.WritePropertyName(parameter.Name);
            writer.WriteStartObject();
            writer.WriteString("type", ParameterType(parameter.Type));
            if (parameter.Type is KejiToolParameterType.StringArray or KejiToolParameterType.IntegerArray)
            {
                writer.WritePropertyName("items");
                writer.WriteStartObject();
                writer.WriteString("type", parameter.Type == KejiToolParameterType.StringArray ? "string" : "integer");
                writer.WriteEndObject();
            }
            writer.WriteString("description", parameter.Description);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.WritePropertyName("required");
        writer.WriteStartArray();
        foreach (var parameter in schema.Parameters.Where(static item => item.Required))
            writer.WriteStringValue(parameter.Name);
        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string ParameterType(KejiToolParameterType type) => type switch
    {
        KejiToolParameterType.String => "string",
        KejiToolParameterType.Integer => "integer",
        KejiToolParameterType.Number => "number",
        KejiToolParameterType.Boolean => "boolean",
        KejiToolParameterType.StringArray or KejiToolParameterType.IntegerArray => "array",
        _ => throw new InvalidOperationException("Unsupported tool parameter type"),
    };

    private bool TryValidateToolCalls(ImmutableArray<ChatToolCall> calls)
    {
        if (calls.IsDefaultOrEmpty)
            return false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
        {
            if (!IsSafeIdentifier(call.Id, 256) || !ids.Add(call.Id) ||
                !KejiToolName.TryCreate(call.FunctionName, out var name) ||
                call.FunctionArguments.ValueKind != JsonValueKind.Object ||
                Encoding.UTF8.GetByteCount(call.FunctionArguments.GetRawText()) > MaxToolArgumentsBytes)
            {
                return false;
            }
            var resolution = _tools.Resolve(name);
            if (resolution.Definition?.Availability != KejiToolAvailability.Executable)
                return false;
        }
        return true;
    }

    private static bool TryConvertArguments(
        JsonElement arguments,
        out IReadOnlyDictionary<string, object?> inputs)
    {
        var converted = new Dictionary<string, object?>(StringComparer.Ordinal);
        var count = 0;
        foreach (var property in arguments.EnumerateObject())
        {
            if (++count > MaxToolArgumentProperties || converted.ContainsKey(property.Name) ||
                !TryConvertValue(property.Value, out var value))
            {
                inputs = converted;
                return false;
            }
            converted.Add(property.Name, value);
        }
        inputs = converted;
        return true;
    }

    private static bool TryConvertValue(JsonElement element, out object? value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                value = element.GetString();
                return value is string text && Encoding.UTF8.GetByteCount(text) <= MaxToolArgumentsBytes;
            case JsonValueKind.Number when element.TryGetInt64(out var integer):
                value = integer;
                return true;
            case JsonValueKind.Number when element.TryGetDouble(out var number) && double.IsFinite(number):
                value = number;
                return true;
            case JsonValueKind.True:
            case JsonValueKind.False:
                value = element.GetBoolean();
                return true;
            case JsonValueKind.Array:
                return TryConvertArray(element, out value);
            default:
                value = null;
                return false;
        }
    }

    private static bool TryConvertArray(JsonElement element, out object? value)
    {
        if (element.GetArrayLength() > 256)
        {
            value = null;
            return false;
        }
        var strings = new List<string>();
        var integers = new List<long>();
        JsonValueKind? kind = null;
        foreach (var item in element.EnumerateArray())
        {
            kind ??= item.ValueKind;
            if (item.ValueKind != kind)
            {
                value = null;
                return false;
            }
            if (kind == JsonValueKind.String && item.GetString() is { } text &&
                Encoding.UTF8.GetByteCount(text) <= MaxToolArgumentsBytes)
                strings.Add(text);
            else if (kind == JsonValueKind.Number && item.TryGetInt64(out var integer))
                integers.Add(integer);
            else
            {
                value = null;
                return false;
            }
        }
        value = kind == JsonValueKind.Number ? integers.ToArray() : strings.ToArray();
        return true;
    }

    private bool TryFormatToolResult(ToolExecutionResult result, out string content, out int bytes)
    {
        if (!result.Success)
        {
            var safeCode = IsSafeIdentifier(result.ErrorCode ?? string.Empty, 64)
                ? result.ErrorCode
                : "TOOL_FAILED";
            content = $"{{\"success\":false,\"error_code\":\"{safeCode}\"}}";
        }
        else if (result.Value is string text)
        {
            content = text;
        }
        else if (result.Value is null)
        {
            content = "null";
        }
        else
        {
            content = JsonSerializer.Serialize(result.Value);
        }
        bytes = Encoding.UTF8.GetByteCount(content);
        return bytes <= _options.MaxToolResultBytes && !content.Contains('\0');
    }

    private static bool TryValidateAssistantContent(string? value, out string content)
    {
        content = value ?? string.Empty;
        return !string.IsNullOrWhiteSpace(content) && !content.Contains('\0') &&
               Encoding.UTF8.GetByteCount(content) <= MaxAssistantBytes;
    }

    private bool TryEnforceContextBounds(IReadOnlyList<ChatMessage> context)
    {
        if (context.Count > _options.MaxContextMessages)
            return false;
        var bytes = 0;
        foreach (var message in context)
        {
            bytes += Encoding.UTF8.GetByteCount(message.Content ?? string.Empty);
            foreach (var call in message.ToolCalls.IsDefault ? [] : message.ToolCalls)
                bytes += Encoding.UTF8.GetByteCount(call.FunctionArguments.GetRawText());
            if (bytes > _options.MaxContextBytes)
                return false;
        }
        return true;
    }
}
