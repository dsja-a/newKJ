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
    private const int MaxReasoningBytes = 4 * 1024 * 1024;
    private const int MaxToolArgumentProperties = 64;

    private readonly ICurrentUserAccessor _userAccessor;
    private readonly IConversationRepository _conversations;
    private readonly IMessageRepository _messages;
    private readonly IModelProviderRegistry _providers;
    private readonly IKejiToolRegistry _tools;
    private readonly IToolExecutionPipeline _toolPipeline;
    private readonly AgentLoopOptions _options;
    private readonly IKejiAuditService _audit;
    private readonly IKejiAgentSessionGate _sessionGate;
    private readonly KejiAgentContextBuilder _contextBuilder;
    private readonly TimeProvider _timeProvider;

    public AgentLoop(
        ICurrentUserAccessor userAccessor,
        IConversationRepository conversations,
        IMessageRepository messages,
        IModelProviderRegistry providers,
        IKejiToolRegistry tools,
        IToolExecutionPipeline toolPipeline,
        AgentLoopOptions options,
        IKejiAuditService audit,
        IKejiAgentSessionGate sessionGate,
        KejiAgentContextBuilder? contextBuilder = null,
        TimeProvider? timeProvider = null)
    {
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _toolPipeline = toolPipeline ?? throw new ArgumentNullException(nameof(toolPipeline));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _sessionGate = sessionGate ?? throw new ArgumentNullException(nameof(sessionGate));
        _contextBuilder = contextBuilder ?? new KejiAgentContextBuilder(options);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        KejiAgentEvent? error = null;
        KejiAgentEvent? completed = null;
        await foreach (var item in RunStreamAsync(ToKejiRequest(request), cancellationToken).ConfigureAwait(false))
        {
            if (item.Type == KejiAgentEventType.Error) error = item;
            if (item.Type == KejiAgentEventType.RunCompleted) completed = item;
        }
        if (error is null && completed?.Transcript is { } transcript)
            return AgentRunResult.Completed(transcript.FinalContent, transcript.Iterations, transcript.ToolCallCount);
        var summary = completed?.Transcript;
        return AgentRunResult.Failed(MapLegacyStatus(error?.ErrorCode ?? KejiAgentErrorCode.InternalFailure),
            summary?.Iterations ?? 0, summary?.ToolCallCount ?? 0);
    }

    public IAsyncEnumerable<KejiAgentEvent> RunStreamAsync(KejiAgentRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var channel = Channel.CreateBounded<KejiAgentEvent>(new BoundedChannelOptions(_options.EventBufferCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true,
        });
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stoppedCts = new CancellationTokenSource();
        var terminalCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stoppedCts.Token);
        var timer = _timeProvider.CreateTimer(static state => ((CancellationTokenSource)state!).Cancel(), runCts,
            _options.RunTimeout, Timeout.InfiniteTimeSpan);
        var producer = ProduceAsync(request, channel.Writer, cancellationToken, runCts.Token, terminalCts.Token);
        return ReadEventsAsync(channel.Reader, producer, runCts, stoppedCts, terminalCts, timer, cancellationToken);
    }

    private async Task ProduceAsync(KejiAgentRunRequest request, ChannelWriter<KejiAgentEvent> writer,
        CancellationToken callerToken, CancellationToken runToken, CancellationToken terminalToken)
    {
        var runId = request.RunId;
        var started = _timeProvider.GetUtcNow();
        var state = new RunState();
        long sequence = 0;
        IDisposable? lease = null;
        string userId = string.Empty;

        async ValueTask EmitAsync(KejiAgentEvent item, CancellationToken token) =>
            await writer.WriteAsync(item with
            {
                RunId = runId, Sequence = checked(++sequence), TimestampUtc = _timeProvider.GetUtcNow(),
            }, token).ConfigureAwait(false);

        try
        {
            if (!TryValidateRequest(request)) throw new AgentFailureException(KejiAgentErrorCode.InvalidRequest);
            var user = _userAccessor.CurrentUser;
            if (!IsValidUser(user)) throw new AgentFailureException(KejiAgentErrorCode.Unauthenticated);
            userId = user!.Id;
            lease = _sessionGate.TryEnter(userId, request.ConversationId);
            if (lease is null) throw new AgentFailureException(KejiAgentErrorCode.SessionBusy, KejiAgentStopReason.SessionBusy);
            await AuditAsync("agent_run_started", request, userId, state, null, null, runToken).ConfigureAwait(false);
            await EmitAsync(Event(KejiAgentEventType.RunStarted), runToken).ConfigureAwait(false);
            await RunCoreStreamAsync(request, userId, started, state, item => EmitAsync(item, runToken), runToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            await AuditAsync("agent_run_cancelled", request, userId, state, null, null, CancellationToken.None).ConfigureAwait(false);
            writer.TryComplete(new OperationCanceledException(callerToken));
            return;
        }
        catch (OperationCanceledException)
        {
            await EmitFailureTerminalAsync(KejiAgentErrorCode.RunTimedOut, KejiAgentStopReason.TimedOut).ConfigureAwait(false);
        }
        catch (AgentFailureException exception)
        {
            await EmitFailureTerminalAsync(exception.Code, exception.StopReason ?? StopReasonFor(exception.Code)).ConfigureAwait(false);
        }
        catch (Persistence.KejiPersistenceException)
        {
            await EmitFailureTerminalAsync(KejiAgentErrorCode.PersistenceFailed, KejiAgentStopReason.Failed).ConfigureAwait(false);
        }
        catch (Exception)
        {
            await EmitFailureTerminalAsync(KejiAgentErrorCode.InternalFailure, KejiAgentStopReason.Failed).ConfigureAwait(false);
        }
        finally
        {
            lease?.Dispose();
            writer.TryComplete();
        }

        KejiAgentEvent Event(KejiAgentEventType type) => new()
        { RunId = runId, Sequence = 0, Type = type, TimestampUtc = default };

        async Task EmitFailureTerminalAsync(KejiAgentErrorCode code, KejiAgentStopReason reason)
        {
            if (state.HasUsage && !state.UsageEmitted)
            {
                await EmitAsync(Event(KejiAgentEventType.Usage) with { Usage = state.Usage, Iteration = state.Iterations }, terminalToken).ConfigureAwait(false);
                state.UsageEmitted = true;
            }
            var completedAt = _timeProvider.GetUtcNow();
            var transcript = BuildTranscript(request, started, completedAt, reason, state);
            await EmitAsync(Event(KejiAgentEventType.Error) with
            {
                ErrorCode = code, StopReason = reason, Iteration = state.Iterations,
                SafeErrorMessage = SafeError(code),
            }, terminalToken).ConfigureAwait(false);
            await AuditAsync("agent_run_failed", request, userId, state, code, null, terminalToken).ConfigureAwait(false);
            await EmitAsync(Event(KejiAgentEventType.RunCompleted) with
            {
                StopReason = reason, Iteration = state.Iterations, Usage = state.Usage, Transcript = transcript,
            }, terminalToken).ConfigureAwait(false);
            await AuditAsync("agent_run_completed", request, userId, state, code, null, terminalToken).ConfigureAwait(false);
        }
    }

    private async Task RunCoreStreamAsync(KejiAgentRunRequest request, string userId, DateTimeOffset started,
        RunState state, Func<KejiAgentEvent, ValueTask> emit, CancellationToken cancellationToken)
    {
        if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
            throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
        var provider = _providers.GetProvider(request.ProviderName) ??
            throw new AgentFailureException(KejiAgentErrorCode.ProviderNotFound);
        var history = await _messages.ListOwnedMessagesAsync(request.ConversationId, userId,
            _options.MaxContextMessages + 1, cancellationToken).ConfigureAwait(false);
        if (!_contextBuilder.TryBuild(history, request.UserMessage, out var context))
            throw new AgentFailureException(KejiAgentErrorCode.ContextLimit, KejiAgentStopReason.ContextLimit);
        state.Messages.AddRange(history.Select(static record => new KejiAgentTranscriptMessage(
            record.Role == "user" ? KejiAgentTranscriptRole.User : KejiAgentTranscriptRole.Assistant, record.Content)));
        state.Messages.Add(new KejiAgentTranscriptMessage(KejiAgentTranscriptRole.User, request.UserMessage));
        if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
            throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
        await _messages.AddOwnedAsync(request.ConversationId, userId, "user", request.UserMessage, cancellationToken).ConfigureAwait(false);

        var advertisedTools = BuildAdvertisedTools();
        var recoveryAttempts = 0;
        var failedToolContinuations = 0;
        var totalToolArgumentBytes = 0;
        var totalToolResultBytes = 0;

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            state.Iterations = iteration;
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
            var iterationContent = new StringBuilder();
            var iterationContentBytes = 0;
            var calls = new SortedDictionary<int, StreamedToolCall>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var finishReason = KejiFinishReason.Invalid;
            var declaredTools = false;
            var sawUsage = false;
            var sawDone = false;
            var eventCount = 0;

            try
            {
                var providerRequest = new ChatCompletionRequest
                {
                    Model = request.Model, Messages = context.ToImmutableArray(), Tools = advertisedTools,
                    Temperature = request.Temperature, MaxTokens = request.MaxTokens,
                };
                await foreach (var item in provider.StreamAsync(providerRequest, cancellationToken)
                                   .WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    if (checked(++eventCount) > _options.MaxProviderEventsPerIteration || sawDone || item.ChoiceIndex != 0)
                        throw ProtocolFailure();
                    if (sawUsage && item.Type is not KejiProviderStreamEventKind.Done)
                        throw ProtocolFailure();
                    switch (item.Type)
                    {
                        case KejiProviderStreamEventKind.ReasoningToken:
                            if (string.IsNullOrEmpty(item.Content)) throw ProtocolFailure();
                            var reasoningBytes = StrictByteCount(item.Content);
                            state.ReasoningBytes = checked(state.ReasoningBytes + reasoningBytes);
                            if (state.ReasoningBytes > MaxReasoningBytes) throw ProtocolFailure();
                            if (!state.ThinkingStarted)
                            {
                                state.ThinkingStarted = true;
                                await emit(NewEvent(KejiAgentEventType.ThinkingStarted, iteration)).ConfigureAwait(false);
                            }
                            await emit(NewEvent(KejiAgentEventType.ThinkingDelta, iteration) with { ContentDelta = item.Content }).ConfigureAwait(false);
                            break;
                        case KejiProviderStreamEventKind.Token:
                            if (string.IsNullOrEmpty(item.Content)) throw ProtocolFailure();
                            var answerBytes = StrictByteCount(item.Content);
                            iterationContentBytes = checked(iterationContentBytes + answerBytes);
                            state.AnswerBytes = checked(state.AnswerBytes + answerBytes);
                            if (iterationContentBytes > MaxAssistantBytes || state.AnswerBytes > MaxAssistantBytes) throw ProtocolFailure();
                            if (!state.AnsweringStarted)
                            {
                                state.AnsweringStarted = true;
                                await emit(NewEvent(KejiAgentEventType.AnsweringStarted, iteration)).ConfigureAwait(false);
                            }
                            iterationContent.Append(item.Content);
                            state.Content.Append(item.Content);
                            await emit(NewEvent(KejiAgentEventType.AnswerDelta, iteration) with { ContentDelta = item.Content }).ConfigureAwait(false);
                            break;
                        case KejiProviderStreamEventKind.ToolCallBegin:
                            if (item.ToolCallIndex is < 0 or >= 1024 || calls.ContainsKey(item.ToolCallIndex) ||
                                !IsSafeIdentifier(item.ToolCallId ?? string.Empty, 256) || !ids.Add(item.ToolCallId!) ||
                                !IsSafeIdentifier(item.ToolName ?? string.Empty, 64)) throw ProtocolFailure();
                            calls.Add(item.ToolCallIndex, new StreamedToolCall(item.ToolCallId!, item.ToolName!));
                            break;
                        case KejiProviderStreamEventKind.ToolCallDelta:
                            if (!calls.TryGetValue(item.ToolCallIndex, out var call) || call.Ended || item.ToolArguments is null ||
                                item.ToolCallId is not null && !string.Equals(item.ToolCallId, call.Id, StringComparison.Ordinal)) throw ProtocolFailure();
                            var added = call.Append(item.ToolArguments, _options.MaxToolArgumentBytesPerCall);
                            totalToolArgumentBytes = checked(totalToolArgumentBytes + added);
                            if (totalToolArgumentBytes > _options.MaxToolArgumentBytesTotal) throw ProtocolFailure();
                            break;
                        case KejiProviderStreamEventKind.ToolCallEnd:
                            if (!calls.TryGetValue(item.ToolCallIndex, out var ended) || ended.Ended ||
                                item.ToolCallId is not null && !string.Equals(item.ToolCallId, ended.Id, StringComparison.Ordinal)) throw ProtocolFailure();
                            ended.Ended = true;
                            break;
                        case KejiProviderStreamEventKind.ChoiceFinished:
                            if (finishReason != KejiFinishReason.Invalid || calls.Values.Any(static call => !call.Ended)) throw ProtocolFailure();
                            finishReason = item.FinishReason;
                            declaredTools = item.HasToolCalls;
                            break;
                        case KejiProviderStreamEventKind.Usage:
                            if (sawUsage || finishReason == KejiFinishReason.Invalid || item.Usage is null) throw ProtocolFailure();
                            try
                            {
                                var current = new KejiAgentUsage(item.Usage.PromptTokens, item.Usage.CompletionTokens, item.Usage.CachedTokens);
                                state.Usage = state.Usage.Add(current.PromptTokens, current.CompletionTokens, current.CachedTokens);
                                state.HasUsage = true;
                            }
                            catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
                            { throw ProtocolFailure(); }
                            sawUsage = true;
                            break;
                        case KejiProviderStreamEventKind.Error:
                            throw new AgentFailureException(MapProviderError(item.ErrorCode));
                        case KejiProviderStreamEventKind.Done:
                            if (finishReason == KejiFinishReason.Invalid) throw ProtocolFailure();
                            sawDone = true;
                            break;
                        default:
                            throw ProtocolFailure();
                    }
                }
            }
            catch (AgentFailureException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new AgentFailureException(KejiAgentErrorCode.ProviderUnavailable); }

            if (!sawDone || finishReason == KejiFinishReason.Invalid || declaredTools != (calls.Count > 0) ||
                calls.Count > _options.MaxToolCallsPerIteration) throw ProtocolFailure();
            var responseToolCalls = BuildToolCalls(calls);
            if (calls.Count > 0)
            {
                if (finishReason != KejiFinishReason.ToolCalls || state.ToolCalls + calls.Count > _options.MaxToolCalls)
                    throw new AgentFailureException(KejiAgentErrorCode.ToolCallLimit, KejiAgentStopReason.ToolCallLimit);
                if (!TryValidateToolCalls(responseToolCalls))
                {
                    var rejected = responseToolCalls[0];
                    await emit(NewEvent(KejiAgentEventType.ToolCompleted, iteration) with
                    { ToolCallId = rejected.Id, ToolName = rejected.FunctionName, ToolCallIndex = calls.Keys.First(), ToolSucceeded = false, ToolErrorCode = "TOOL_REJECTED" }).ConfigureAwait(false);
                    throw new AgentFailureException(KejiAgentErrorCode.ToolRejected);
                }
                context.Add(new ChatMessage { Role = KejiChatRole.Assistant,
                    Content = iterationContent.Length == 0 ? null : iterationContent.ToString(), ToolCalls = responseToolCalls });
                var indexes = calls.Keys.ToArray();
                for (var i = 0; i < responseToolCalls.Length; i++)
                {
                    var toolCall = responseToolCalls[i];
                    var index = indexes[i];
                    if (!TryConvertArguments(toolCall.FunctionArguments, out var inputs))
                        throw new AgentFailureException(KejiAgentErrorCode.ToolRejected);
                    await emit(NewEvent(KejiAgentEventType.ToolStarted, iteration) with
                    { ToolCallId = toolCall.Id, ToolName = toolCall.FunctionName, ToolCallIndex = index }).ConfigureAwait(false);
                    await AuditAsync("agent_tool_started", request, userId, state, null, (toolCall.FunctionName, index, null, null), cancellationToken).ConfigureAwait(false);
                    if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                        throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
                    var toolStarted = _timeProvider.GetTimestamp();
                    ToolExecutionResult result;
                    try { result = await _toolPipeline.ExecuteAsync(toolCall.FunctionName, inputs, cancellationToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { result = ToolExecutionResult.Failed("Tool execution failed.", "TOOL_FAILED", TimeSpan.Zero); }
                    var durationMs = Math.Max(0, (long)_timeProvider.GetElapsedTime(toolStarted).TotalMilliseconds);
                    state.ToolCalls++;
                    state.ToolsUsed.Add(toolCall.FunctionName);
                    var safeCode = result.Success ? null : SafeToolErrorCode(result.ErrorCode);
                    await emit(NewEvent(KejiAgentEventType.ToolCompleted, iteration) with
                    { ToolCallId = toolCall.Id, ToolName = toolCall.FunctionName, ToolCallIndex = index,
                        ToolSucceeded = result.Success, ToolErrorCode = safeCode, ToolDurationMs = durationMs }).ConfigureAwait(false);
                    await AuditAsync("agent_tool_completed", request, userId, state, result.Success ? null : KejiAgentErrorCode.ToolFailed,
                        (toolCall.FunctionName, index, result.Success, durationMs), cancellationToken).ConfigureAwait(false);
                    if (!TryFormatToolResult(result, out var toolContent, out var resultBytes) ||
                        resultBytes > _options.MaxTotalToolResultBytes - totalToolResultBytes)
                        throw new AgentFailureException(KejiAgentErrorCode.ContextLimit, KejiAgentStopReason.ContextLimit);
                    totalToolResultBytes += resultBytes;
                    context.Add(new ChatMessage { Role = KejiChatRole.Tool, ToolCallId = toolCall.Id,
                        Name = toolCall.FunctionName, Content = toolContent });
                    state.Messages.Add(new KejiAgentTranscriptMessage(KejiAgentTranscriptRole.Tool,
                        result.Success ? "completed" : safeCode, toolCall.Id, toolCall.FunctionName));
                    if (!result.Success && IsTerminalToolRejection(safeCode))
                        throw new AgentFailureException(KejiAgentErrorCode.ToolRejected);
                    if (!result.Success && ++failedToolContinuations > 1)
                        throw new AgentFailureException(KejiAgentErrorCode.ToolFailed);
                }
                if (!TryEnforceContextBounds(context))
                    throw new AgentFailureException(KejiAgentErrorCode.ContextLimit, KejiAgentStopReason.ContextLimit);
                continue;
            }

            if (finishReason == KejiFinishReason.ToolCalls) throw ProtocolFailure();
            if ((finishReason == KejiFinishReason.Length || iterationContent.Length == 0) && recoveryAttempts++ < _options.MaxRecoveryAttempts)
            {
                if (iterationContent.Length > 0) context.Add(new ChatMessage { Role = KejiChatRole.Assistant, Content = iterationContent.ToString() });
                context.Add(new ChatMessage { Role = KejiChatRole.User, Content = "Continue the answer without repeating prior content." });
                if (!TryEnforceContextBounds(context)) throw new AgentFailureException(KejiAgentErrorCode.ContextLimit, KejiAgentStopReason.ContextLimit);
                continue;
            }
            if (finishReason == KejiFinishReason.Length)
                throw new AgentFailureException(KejiAgentErrorCode.IterationLimit, KejiAgentStopReason.Length);
            if (iterationContent.Length == 0)
                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError, KejiAgentStopReason.EmptyResponse);
            if (finishReason == KejiFinishReason.ContentFilter)
                throw new AgentFailureException(KejiAgentErrorCode.ProviderRejected, KejiAgentStopReason.ContentFiltered);
            var content = state.Content.ToString();
            if (!TryValidateAssistantContent(content, out content)) throw ProtocolFailure();
            if (!await IsStillOwnedAsync(request.ConversationId, userId, cancellationToken).ConfigureAwait(false))
                throw new AgentFailureException(KejiAgentErrorCode.ConversationNotFound);
            await _messages.AddOwnedAsync(request.ConversationId, userId, "assistant", content, cancellationToken).ConfigureAwait(false);
            state.Messages.Add(new KejiAgentTranscriptMessage(KejiAgentTranscriptRole.Assistant, content));
            if (state.HasUsage)
            {
                await emit(NewEvent(KejiAgentEventType.Usage, iteration) with { Usage = state.Usage }).ConfigureAwait(false);
                state.UsageEmitted = true;
            }
            var completedAt = _timeProvider.GetUtcNow();
            var transcript = BuildTranscript(request, started, completedAt, KejiAgentStopReason.Completed, state);
            await emit(NewEvent(KejiAgentEventType.RunCompleted, iteration) with
            { StopReason = KejiAgentStopReason.Completed, Usage = state.Usage, Transcript = transcript }).ConfigureAwait(false);
            await AuditAsync("agent_run_completed", request, userId, state, null, null, cancellationToken).ConfigureAwait(false);
            return;
        }
        throw new AgentFailureException(KejiAgentErrorCode.IterationLimit, KejiAgentStopReason.IterationLimit);
    }

    private static AgentFailureException ProtocolFailure() => new(KejiAgentErrorCode.ProviderProtocolError);

    private KejiAgentEvent NewEvent(KejiAgentEventType type, int iteration) => new()
    { RunId = string.Empty, Sequence = 0, Type = type, TimestampUtc = default, Iteration = iteration, ChoiceIndex = 0 };

    private static async IAsyncEnumerable<KejiAgentEvent> ReadEventsAsync(ChannelReader<KejiAgentEvent> reader,
        Task producer, CancellationTokenSource runCts, CancellationTokenSource stoppedCts,
        CancellationTokenSource terminalCts, ITimer timer,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) yield return item;
        }
        finally
        {
            stoppedCts.Cancel();
            runCts.Cancel();
            try { await producer.ConfigureAwait(false); } catch (OperationCanceledException) { }
            await timer.DisposeAsync().ConfigureAwait(false);
            terminalCts.Dispose(); stoppedCts.Dispose(); runCts.Dispose();
        }
    }

    private async Task AuditAsync(string action, KejiAgentRunRequest request, string userId, RunState state,
        KejiAgentErrorCode? error, (string Name, int Index, bool? Success, long? DurationMs)? tool,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["run_id"] = request.RunId, ["conversation_id"] = request.ConversationId,
            ["user_id"] = userId, ["provider"] = request.ProviderName, ["model"] = request.Model,
            ["iteration"] = state.Iterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        if (error is not null) metadata["safe_error_code"] = error.Value.ToString();
        if (tool is { } value)
        {
            metadata["tool"] = value.Name;
            metadata["tool_call_index"] = value.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (value.Success is not null) metadata["success"] = value.Success.Value ? "true" : "false";
            if (value.DurationMs is not null) metadata["duration_ms"] = value.DurationMs.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (state.HasUsage) metadata["usage_total"] = state.Usage.TotalTokens.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2), _timeProvider);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await _audit.WriteAsync(KejiAuditCategory.DataAccess, action,
                error is null ? KejiAuditOutcome.Success : KejiAuditOutcome.Failure,
                error is null ? KejiAuditSeverity.Information : KejiAuditSeverity.Warning,
                "conversation", request.ConversationId, metadata, linked.Token).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private static KejiAgentTranscript BuildTranscript(KejiAgentRunRequest request, DateTimeOffset started,
        DateTimeOffset completed, KejiAgentStopReason reason, RunState state) => new(
        request.RunId, request.ConversationId, started, completed, reason, state.Iterations, state.ToolCalls,
        state.ToolsUsed.ToImmutableArray(), state.Content.ToString(), state.Usage, state.Messages.ToImmutableArray());

    private static string SafeError(KejiAgentErrorCode code) => $"AGENT_{code.ToString().ToUpperInvariant()}";

    private static int StrictByteCount(string value)
    {
        try { return new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException) { throw ProtocolFailure(); }
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

    private static bool IsTerminalToolRejection(string? code) => code is
        "UNKNOWN_TOOL" or "CONTRACT_ONLY" or "UNAUTHORIZED" or "INVALID_INPUT" or
        "INVALID_TARGET" or "NO_CURRENT_USER" or "PROTOCOL_ERROR";

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
        KejiAgentErrorCode.ContextLimit => KejiAgentStopReason.ContextLimit,
        KejiAgentErrorCode.ToolCallLimit => KejiAgentStopReason.ToolCallLimit,
        KejiAgentErrorCode.IterationLimit => KejiAgentStopReason.IterationLimit,
        KejiAgentErrorCode.SessionBusy => KejiAgentStopReason.SessionBusy,
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
        KejiAgentErrorCode.ContextLimit or KejiAgentErrorCode.ToolCallLimit or
            KejiAgentErrorCode.IterationLimit => AgentRunStatus.LimitExceeded,
        _ => AgentRunStatus.ProviderFailed,
    };

    private static KejiAgentRunRequest ToKejiRequest(AgentRunRequest request) => new()
    {
        RunId = Guid.NewGuid().ToString("N"),
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

        public int Bytes { get; private set; }

        public int Append(string value, int maxBytes)
        {
            var added = StrictByteCount(value);
            Bytes = checked(Bytes + added);
            if (Bytes > maxBytes)
                throw new AgentFailureException(KejiAgentErrorCode.ProviderProtocolError);
            Arguments.Append(value);
            return added;
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
        public bool HasUsage { get; set; }
        public bool UsageEmitted { get; set; }
        public bool ThinkingStarted { get; set; }
        public bool AnsweringStarted { get; set; }
        public int ReasoningBytes { get; set; }
        public int AnswerBytes { get; set; }
        public List<string> ToolsUsed { get; } = new();
        public List<KejiAgentTranscriptMessage> Messages { get; } = new();
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
            !IsRunId(request.RunId) || request.Model.Length > MaxModelLength || request.Model.Any(char.IsControl) ||
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

    private static bool IsRunId(string value) => value.Length == 32 &&
        value.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

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
                Encoding.UTF8.GetByteCount(call.FunctionArguments.GetRawText()) > _options.MaxToolArgumentBytesPerCall)
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
                return value is string text && Encoding.UTF8.GetByteCount(text) <= 256 * 1024;
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
                Encoding.UTF8.GetByteCount(text) <= 256 * 1024)
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
