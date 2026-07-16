using System.Runtime.CompilerServices;
using System.Text;
using Keji.Providers;

namespace Keji.Streaming;

public static class KejiSseAdapter
{
    private const int MaxChoices = 16;
    private const int MaxChoiceIndex = 1024;
    private const int MaxToolCallIndex = 1024;
    private const int MaxToolCallsPerChoice = 128;
    private const int MaxProviderEvents = 262_144;
    private const int MaxDeltaBytes = 256 * 1024;
    private const int MaxContentBytesPerChoice = 4 * 1024 * 1024;
    private const int MaxReasoningBytesPerChoice = 4 * 1024 * 1024;
    private const int MaxToolArgumentsBytesPerCall = 256 * 1024;
    private const int MaxToolArgumentsBytesPerChoice = 1024 * 1024;
    private const long MaxReportedTokens = 1_000_000_000;
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static async IAsyncEnumerable<KejiSseEvent> ToSseEvents(
        IAsyncEnumerable<ChatCompletionStreamEvent> providerEvents,
        [EnumeratorCancellation] CancellationToken ct = default,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(providerEvents);
        if (ct.IsCancellationRequested)
            yield break;

        var clock = timeProvider ?? TimeProvider.System;
        var sequence = 0L;
        var lastTimestamp = DateTime.MinValue;
        var usageSeen = false;
        TokenUsage? pendingUsage = null;
        var sawAnyProviderEvent = false;
        var providerEventCount = 0;
        var activeToolCalls = new Dictionary<(int ChoiceIndex, int ToolCallIndex), string>();
        var activeToolArguments = new Dictionary<(int ChoiceIndex, int ToolCallIndex), ToolArgumentState>();
        var choiceStates = new Dictionary<int, AdapterChoiceState>();

        bool TryGetChoiceState(int choiceIndex, out AdapterChoiceState state)
        {
            if (choiceIndex is < 0 or > MaxChoiceIndex)
            {
                state = null!;
                return false;
            }

            if (choiceStates.TryGetValue(choiceIndex, out state!))
                return true;
            if (choiceStates.Count >= MaxChoices)
            {
                state = null!;
                return false;
            }

            state = new AdapterChoiceState();
            choiceStates.Add(choiceIndex, state);
            return true;
        }

        KejiSseEvent CreateEvent(
            KejiSseEventType eventType,
            KejiSsePhase eventPhase,
            string? delta = null,
            string? toolName = null,
            string? toolCallId = null,
            int? toolCallIndex = null,
            int? choiceIndex = null,
            TokenUsage? usage = null,
            string? errorCode = null,
            string? errorMessage = null)
        {
            var timestamp = clock.GetUtcNow().UtcDateTime;
            if (timestamp < lastTimestamp)
                timestamp = lastTimestamp;
            lastTimestamp = timestamp;

            var currentSequence = sequence++;
            return new KejiSseEvent
            {
                EventType = eventType,
                Phase = eventPhase,
                Delta = delta,
                ToolName = toolName,
                ToolCallId = toolCallId,
                ToolCallIndex = toolCallIndex,
                ChoiceIndex = choiceIndex,
                Usage = usage,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                Sequence = currentSequence,
                EventId = $"evt_{currentSequence}",
                TimestampUtc = timestamp,
            };
        }

        KejiSseEvent CreateProtocolError()
        {
            var error = ProviderErrorMapper.Sanitize("STREAM_PROTOCOL_ERROR");
            return CreateEvent(
                KejiSseEventType.Error,
                KejiSsePhase.Error,
                errorCode: error.Code,
                errorMessage: error.Message);
        }

        IAsyncEnumerator<ChatCompletionStreamEvent>? enumerator = null;
        Exception? acquisitionException = null;
        try
        {
            enumerator = providerEvents.GetAsyncEnumerator(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            acquisitionException = exception;
        }

        if (acquisitionException is not null || enumerator is null)
        {
            var error = ProviderErrorMapper.Sanitize("PROVIDER_ERROR");
            yield return CreateEvent(
                KejiSseEventType.Error,
                KejiSsePhase.Error,
                errorCode: error.Code,
                errorMessage: error.Message);
            yield break;
        }

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                bool hasNext;
                Exception? upstreamException = null;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    hasNext = false;
                    upstreamException = exception;
                }

                if (upstreamException is not null)
                {
                    var error = ProviderErrorMapper.Sanitize("PROVIDER_ERROR");
                    yield return CreateEvent(
                        KejiSseEventType.Error,
                        KejiSsePhase.Error,
                        errorCode: error.Code,
                        errorMessage: error.Message);
                    yield break;
                }

                if (!hasNext)
                    break;

                ct.ThrowIfCancellationRequested();
                sawAnyProviderEvent = true;
                var providerEvent = enumerator.Current;
                if (++providerEventCount > MaxProviderEvents || providerEvent is null)
                {
                    yield return CreateProtocolError();
                    yield break;
                }

                if (usageSeen && providerEvent.Type is not
                    (ChatCompletionStreamEventType.Done or ChatCompletionStreamEventType.Error))
                {
                    yield return CreateProtocolError();
                    yield break;
                }

                switch (providerEvent.Type)
                {
                    case ChatCompletionStreamEventType.ReasoningToken:
                        if (string.IsNullOrEmpty(providerEvent.Content) ||
                            !TryGetChoiceState(providerEvent.ChoiceIndex, out var reasoningState) ||
                            reasoningState.Finished ||
                            !TryAddUtf8Bytes(
                                providerEvent.Content,
                                MaxDeltaBytes,
                                MaxReasoningBytesPerChoice,
                                ref reasoningState.ReasoningBytes) ||
                            reasoningState.Phase == KejiSsePhase.Answering &&
                            !reasoningState.MayResumeThinking)
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        reasoningState.Phase = KejiSsePhase.Thinking;
                        if (!reasoningState.ThinkingAnnounced)
                        {
                            yield return CreateEvent(
                                KejiSseEventType.Thinking,
                                KejiSsePhase.Thinking,
                                choiceIndex: providerEvent.ChoiceIndex);
                            reasoningState.ThinkingAnnounced = true;
                        }
                        ct.ThrowIfCancellationRequested();
                        yield return CreateEvent(
                            KejiSseEventType.ThinkToken,
                            KejiSsePhase.Thinking,
                            delta: providerEvent.Content,
                            choiceIndex: providerEvent.ChoiceIndex);
                        break;

                    case ChatCompletionStreamEventType.Token:
                        if (string.IsNullOrEmpty(providerEvent.Content) ||
                            !TryGetChoiceState(providerEvent.ChoiceIndex, out var answerState) ||
                            answerState.Finished ||
                            !TryAddUtf8Bytes(
                                providerEvent.Content,
                                MaxDeltaBytes,
                                MaxContentBytesPerChoice,
                                ref answerState.ContentBytes))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        if (answerState.Phase != KejiSsePhase.Answering)
                        {
                            answerState.Phase = KejiSsePhase.Answering;
                            yield return CreateEvent(
                                KejiSseEventType.Answering,
                                KejiSsePhase.Answering,
                                choiceIndex: providerEvent.ChoiceIndex);
                        }
                        ct.ThrowIfCancellationRequested();
                        answerState.MayResumeThinking = false;
                        yield return CreateEvent(
                            KejiSseEventType.Answer,
                            KejiSsePhase.Answering,
                            delta: providerEvent.Content,
                            choiceIndex: providerEvent.ChoiceIndex);
                        break;

                    case ChatCompletionStreamEventType.ToolCallBegin:
                        var toolKey = (providerEvent.ChoiceIndex, providerEvent.ToolCallIndex);
                        if (providerEvent.ToolCallIndex is < 0 or > MaxToolCallIndex ||
                            !IsValidIdentifier(providerEvent.ToolCallId, 256) ||
                            !IsValidToolName(providerEvent.ToolName) ||
                            !TryGetChoiceState(providerEvent.ChoiceIndex, out var toolState) ||
                            toolState.Finished || toolState.ToolCallCount >= MaxToolCallsPerChoice ||
                            !toolState.ToolCallIds.Add(providerEvent.ToolCallId!) ||
                            !activeToolCalls.TryAdd(toolKey, providerEvent.ToolCallId!))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        activeToolArguments.Add(toolKey, new ToolArgumentState());
                        toolState.ToolCallCount++;
                        toolState.PendingToolCalls.Add(new PendingToolCall(
                            providerEvent.ToolCallId!,
                            providerEvent.ToolName!,
                            providerEvent.ToolCallIndex));
                        toolState.Phase = KejiSsePhase.Answering;
                        toolState.MayResumeThinking = false;
                        break;

                    case ChatCompletionStreamEventType.ToolCallDelta:
                        var deltaKey = (providerEvent.ChoiceIndex, providerEvent.ToolCallIndex);
                        if (providerEvent.ToolCallIndex is < 0 or > MaxToolCallIndex ||
                            providerEvent.ToolArguments is null ||
                            !choiceStates.TryGetValue(providerEvent.ChoiceIndex, out var deltaState) ||
                            deltaState.Finished ||
                            !activeToolCalls.TryGetValue(deltaKey, out var activeDeltaId) ||
                            !activeToolArguments.TryGetValue(deltaKey, out var toolArgumentState) ||
                            providerEvent.ToolCallId is { } deltaId &&
                            (!IsValidIdentifier(deltaId, 256) ||
                             !string.Equals(activeDeltaId, deltaId, StringComparison.Ordinal)) ||
                            !TryAppendToolArguments(
                                providerEvent.ToolArguments,
                                toolArgumentState,
                                deltaState))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        break;

                    case ChatCompletionStreamEventType.ToolCallEnd:
                        var endKey = (providerEvent.ChoiceIndex, providerEvent.ToolCallIndex);
                        if (providerEvent.ToolCallIndex is < 0 or > MaxToolCallIndex ||
                            !choiceStates.TryGetValue(providerEvent.ChoiceIndex, out var endedToolState) ||
                            endedToolState.Finished ||
                            !activeToolCalls.TryGetValue(endKey, out var activeEndId) ||
                            !activeToolArguments.TryGetValue(endKey, out var endedArguments) ||
                            providerEvent.ToolCallId is { } endId &&
                            (!IsValidIdentifier(endId, 256) ||
                             !string.Equals(activeEndId, endId, StringComparison.Ordinal)) ||
                            !IsValidToolArguments(endedArguments.Arguments.ToString()))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        activeToolCalls.Remove(endKey);
                        activeToolArguments.Remove(endKey);
                        if (!activeToolCalls.Keys.Any(key => key.ChoiceIndex == providerEvent.ChoiceIndex))
                        {
                            endedToolState.Phase = KejiSsePhase.Thinking;
                            endedToolState.ThinkingAnnounced = false;
                            endedToolState.MayResumeThinking = true;
                        }
                        break;

                    case ChatCompletionStreamEventType.ChoiceFinished:
                        if (!IsValidFinishReason(providerEvent.FinishReason) ||
                            !TryGetChoiceState(providerEvent.ChoiceIndex, out var finishedState) ||
                            finishedState.Finished ||
                            providerEvent.HasToolCalls != (finishedState.ToolCallCount > 0) ||
                            activeToolCalls.Keys.Any(key => key.ChoiceIndex == providerEvent.ChoiceIndex))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        finishedState.Finished = true;
                        finishedState.FinishReason = providerEvent.FinishReason;
                        break;

                    case ChatCompletionStreamEventType.Usage:
                        if (!IsValidUsage(providerEvent.Usage) || activeToolCalls.Count != 0 ||
                            choiceStates.Count == 0 ||
                            choiceStates.Values.Any(static state => !state.Finished))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        usageSeen = true;
                        pendingUsage = providerEvent.Usage;
                        break;

                    case ChatCompletionStreamEventType.Error:
                        var sanitized = ProviderErrorMapper.Sanitize(providerEvent.ErrorCode);
                        yield return CreateEvent(
                            KejiSseEventType.Error,
                            KejiSsePhase.Error,
                            errorCode: sanitized.Code,
                            errorMessage: sanitized.Message);
                        yield break;

                    case ChatCompletionStreamEventType.Done:
                        if (activeToolCalls.Count != 0 || choiceStates.Count == 0 ||
                            choiceStates.Values.Any(static state => !state.Finished))
                        {
                            yield return CreateProtocolError();
                            yield break;
                        }

                        foreach (var (choiceIndex, state) in choiceStates.OrderBy(static pair => pair.Key))
                        {
                            if (state.ToolCallCount == 0 ||
                                state.FinishReason is not ("tool_calls" or "stop"))
                            {
                                state.PendingToolCalls.Clear();
                                continue;
                            }

                            if (state.Phase != KejiSsePhase.Answering)
                            {
                                state.Phase = KejiSsePhase.Answering;
                                yield return CreateEvent(
                                    KejiSseEventType.Answering,
                                    KejiSsePhase.Answering,
                                    choiceIndex: choiceIndex);
                            }

                            foreach (var pendingToolCall in state.PendingToolCalls)
                            {
                                ct.ThrowIfCancellationRequested();
                                yield return CreateEvent(
                                    KejiSseEventType.ToolCall,
                                    KejiSsePhase.Answering,
                                    toolName: pendingToolCall.Name,
                                    toolCallId: pendingToolCall.Id,
                                    toolCallIndex: pendingToolCall.Index,
                                    choiceIndex: choiceIndex);
                            }
                            state.PendingToolCalls.Clear();
                        }

                        if (pendingUsage is not null)
                        {
                            ct.ThrowIfCancellationRequested();
                            yield return CreateEvent(
                                KejiSseEventType.Usage,
                                KejiSsePhase.Done,
                                usage: pendingUsage);
                        }

                        ct.ThrowIfCancellationRequested();
                        yield return CreateEvent(KejiSseEventType.Done, KejiSsePhase.Done);
                        yield break;

                    default:
                        yield return CreateProtocolError();
                        yield break;
                }
            }

            if (sawAnyProviderEvent)
            {
                var truncated = ProviderErrorMapper.Sanitize("STREAM_TRUNCATED");
                yield return CreateEvent(
                    KejiSseEventType.Error,
                    KejiSsePhase.Error,
                    errorCode: truncated.Code,
                    errorMessage: truncated.Message);
            }
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The provider boundary never lets disposal diagnostics replace a terminal wire event.
            }
        }
    }

    private static bool TryAddUtf8Bytes(
        string value,
        int maximumEventBytes,
        int maximumTotalBytes,
        ref int currentTotalBytes)
    {
        if (value.Length > maximumEventBytes)
            return false;

        int addedBytes;
        try
        {
            addedBytes = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (addedBytes > maximumEventBytes || addedBytes > maximumTotalBytes - currentTotalBytes)
            return false;
        currentTotalBytes += addedBytes;
        return true;
    }

    private static bool TryAppendToolArguments(
        string value,
        ToolArgumentState toolArgumentState,
        AdapterChoiceState choiceState)
    {
        if (value.Length > MaxDeltaBytes)
            return false;

        int addedBytes;
        try
        {
            addedBytes = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }

        if (addedBytes > MaxDeltaBytes ||
            addedBytes > MaxToolArgumentsBytesPerCall - toolArgumentState.Bytes ||
            addedBytes > MaxToolArgumentsBytesPerChoice - choiceState.ToolArgumentBytes)
        {
            return false;
        }

        toolArgumentState.Bytes += addedBytes;
        choiceState.ToolArgumentBytes += addedBytes;
        toolArgumentState.Arguments.Append(value);
        return true;
    }

    private static bool IsValidToolArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                arguments,
                new System.Text.Json.JsonDocumentOptions { MaxDepth = 64 });
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static bool IsValidUsage(TokenUsage? usage) =>
        usage is not null &&
        usage.PromptTokens is >= 0 and <= MaxReportedTokens &&
        usage.CompletionTokens is >= 0 and <= MaxReportedTokens &&
        usage.PromptTokens <= MaxReportedTokens - usage.CompletionTokens;

    private static bool IsValidIdentifier(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl) &&
        IsValidUnicode(value);

    private static bool IsValidUnicode(string value)
    {
        try
        {
            _ = StrictUtf8.GetByteCount(value);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static bool IsValidToolName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 128 &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');

    private static bool IsValidFinishReason(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 64 &&
        !value.Any(char.IsControl) &&
        IsValidUnicode(value);

    private sealed class AdapterChoiceState
    {
        public KejiSsePhase Phase { get; set; } = KejiSsePhase.Thinking;
        public bool ThinkingAnnounced { get; set; }
        public bool MayResumeThinking { get; set; } = true;
        public bool Finished { get; set; }
        public int ContentBytes;
        public int ReasoningBytes;
        public int ToolArgumentBytes;
        public int ToolCallCount;
        public string? FinishReason;
        public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);
        public List<PendingToolCall> PendingToolCalls { get; } = new();
    }

    private sealed record PendingToolCall(string Id, string Name, int Index);

    private sealed class ToolArgumentState
    {
        public int Bytes;
        public StringBuilder Arguments { get; } = new();
    }
}
