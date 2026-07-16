using System.Buffers;
using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Keji.Providers;

public abstract partial class ProviderBase : IModelProvider
{
    private const int MaxSseLineBytes = 64 * 1024;
    private const int MaxSseEventDataBytes = 256 * 1024;
    private const int MaxSseLinesPerEvent = 1024;
    private const int MaxSseChunks = 262_144;
    private const int MaxResponseBytes = 16 * 1024 * 1024;
    private const int MaxContentBytesPerChoice = 4 * 1024 * 1024;
    private const int MaxReasoningBytesPerChoice = 4 * 1024 * 1024;
    private const int MaxToolArgumentsBytesPerCall = 256 * 1024;
    private const int MaxToolArgumentsBytesPerChoice = 1024 * 1024;
    private const int MaxToolCallsPerChoice = 128;
    private const int MaxChoices = 16;
    private const int MaxChoiceIndex = 1024;
    private const int MaxMessages = 1024;
    private const int MaxTools = 128;
    private const int MaxMessageBytes = 4 * 1024 * 1024;
    private const int MaxMessageNameLength = 64;
    private const int MaxRequestTextBytes = 16 * 1024 * 1024;
    private const int MaxToolSchemaBytes = 256 * 1024;
    private const int MaxToolDescriptionBytes = 16 * 1024;
    private const int MaxToolNameLength = 128;
    private const int MaxToolCallIdLength = 256;
    private const long MaxReportedTokens = 1_000_000_000;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = 64,
    };
    private static readonly Encoding StrictUtf8 =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _timeout;

    public abstract string ProviderName { get; }
    protected abstract string BaseUri { get; }
    protected abstract AuthenticationHeaderValue? AuthHeader { get; }
    protected virtual string DefaultModel => string.Empty;
    protected virtual int DefaultMaxTokens => 131072;
    protected virtual bool BackfillAssistantReasoningContent => false;
    protected int MaxRetries { get; }

    protected ProviderBase(IHttpClientFactory httpClientFactory, TimeSpan timeout, int maxRetries)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        if (timeout < TimeSpan.FromMilliseconds(1) || timeout > TimeSpan.FromSeconds(120))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (maxRetries is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(maxRetries));

        _httpClient = httpClientFactory.CreateClient("KejiProvider");
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _timeout = timeout;
        MaxRetries = maxRetries;
    }

    public async Task<ChatCompletionResponse> CompleteAsync(
        ChatCompletionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryValidateRequest(request, out _))
            return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidRequest, "Model request is invalid");

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            HttpResponseMessage? response = null;
            try
            {
                using var httpRequest = BuildRequest(request, stream: false);
                using var timeoutCts = CreateTimeoutTokenSource(ct);
                response = await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeoutCts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var result = await ClassifyErrorAsync(response, attempt, ct).ConfigureAwait(false);
                    if (result.Retryable)
                    {
                        TryDisposeResponse(response);
                        await DelayBeforeRetryAsync(result.RetryDelay, ct).ConfigureAwait(false);
                        continue;
                    }

                    return ChatCompletionResponse.Failed(result.Code, result.SafeMessage);
                }

                var json = await DeserializeBoundedAsync<OpenAiChatCompletionResponse>(
                    response.Content,
                    timeoutCts.Token).ConfigureAwait(false);
                if (json is null)
                    return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");

                if (json.Choices is not { Count: > 0 and <= MaxChoices } ||
                    json.Choices.Any(static choice => choice is null) ||
                    json.Choices.Any(static choice => choice.Index is < 0 or > MaxChoiceIndex) ||
                    json.Choices.Select(static choice => choice.Index).Distinct().Count() != json.Choices.Count)
                    return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");

                var choice = json.Choices.OrderBy(static item => item.Index).First();
                if (choice.Message is null)
                    return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");

                var usage = MapAndValidateUsage(json.Usage);
                var content = choice.Message.Content ?? string.Empty;
                var reasoningContent = choice.Message.ReasoningContent;
                if (Encoding.UTF8.GetByteCount(content) > MaxContentBytesPerChoice ||
                    reasoningContent is not null &&
                    Encoding.UTF8.GetByteCount(reasoningContent) > MaxReasoningBytesPerChoice)
                {
                    throw new StreamProtocolException();
                }

                var toolCalls = MapAndValidateToolCalls(choice.Message.ToolCalls);
                var responseModel = string.IsNullOrWhiteSpace(json.Model)
                    ? string.Empty
                    : ValidateResponseModel(json.Model);
                var finishReason = MapFinishReason(choice.FinishReason);

                return ChatCompletionResponse.Succeeded(
                    content,
                    usage,
                    reasoningContent,
                    toolCalls: toolCalls,
                    model: responseModel,
                    finishReason: finishReason);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.Cancelled, "Request was cancelled");
            }
            catch (OperationCanceledException)
            {
                if (attempt < MaxAttempts)
                {
                    await DelayBeforeRetryAsync(GetBackoffDelay(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                return ChatCompletionResponse.Failed(KejiProviderErrorCode.Timeout, "Provider request timed out");
            }
            catch (ProviderResponseTooLargeException)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.ResponseTooLarge, "Provider response exceeded the size limit");
            }
            catch (JsonException)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");
            }
            catch (InvalidDataException)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");
            }
            catch (StreamProtocolException)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.InvalidResponse, "Provider returned an invalid response");
            }
            catch (KejiProviderSecretResolutionException)
            {
                return ChatCompletionResponse.Failed(KejiProviderErrorCode.ProviderError, "Provider secret is unavailable");
            }
            catch (HttpRequestException exception)
            {
                if (attempt < MaxAttempts && IsRetryableException(exception))
                {
                    await DelayBeforeRetryAsync(GetBackoffDelay(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                var mapped = exception.StatusCode is { } statusCode
                    ? ProviderErrorMapper.Map(statusCode, responseBody: null)
                    : ProviderErrorMapper.MapFromException(exception);
                return ChatCompletionResponse.Failed(mapped.Code, mapped.Message);
            }
            catch (IOException)
            {
                if (attempt < MaxAttempts)
                {
                    await DelayBeforeRetryAsync(GetBackoffDelay(attempt), ct).ConfigureAwait(false);
                    continue;
                }

                return ChatCompletionResponse.Failed(KejiProviderErrorCode.ConnectionError, "Failed to connect to provider");
            }
            finally
            {
                TryDisposeResponse(response);
            }
        }

        return ChatCompletionResponse.Failed(KejiProviderErrorCode.ServerError, "Provider request failed");
    }

    public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        using var lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateBounded<ChatCompletionStreamEvent>(new BoundedChannelOptions(32)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });

        var producerTask = RunStreamProducerAsync(channel.Writer, request, lifetimeCts.Token);
        try
        {
            await foreach (var streamEvent in channel.Reader.ReadAllAsync(lifetimeCts.Token).ConfigureAwait(false))
                yield return streamEvent;
        }
        finally
        {
            lifetimeCts.Cancel();
            try
            {
                await producerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (lifetimeCts.IsCancellationRequested)
            {
            }
        }
    }

    protected virtual Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken ct) =>
        Task.Delay(delay, ct);

    protected virtual DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow;

    private async Task RunStreamProducerAsync(
        ChannelWriter<ChatCompletionStreamEvent> writer,
        ChatCompletionRequest request,
        CancellationToken ct)
    {
        var output = new StreamOutput(writer, ct);
        try
        {
            if (!TryValidateRequest(request, out _))
            {
                await output.WriteAsync(ChatCompletionStreamEvent.Error(
                    KejiProviderErrorCode.InvalidRequest,
                    "Model request is invalid")).ConfigureAwait(false);
                return;
            }

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                HttpRequestMessage? httpRequest = null;
                HttpResponseMessage? httpResponse = null;
                Stream? responseStream = null;
                SseEventReader? reader = null;
                CancellationTokenRegistration streamCancellationRegistration = default;
                TimeSpan? retryDelay = null;

                try
                {
                    httpRequest = BuildRequest(request, stream: true);
                    using (var headersTimeoutCts = CreateTimeoutTokenSource(ct))
                    {
                        httpResponse = await _httpClient.SendAsync(
                            httpRequest,
                            HttpCompletionOption.ResponseHeadersRead,
                            headersTimeoutCts.Token).ConfigureAwait(false);
                    }

                    if (!httpResponse.IsSuccessStatusCode)
                    {
                        var result = await ClassifyErrorAsync(httpResponse, attempt, ct).ConfigureAwait(false);
                        if (!output.HasEmitted && result.Retryable)
                        {
                            retryDelay = result.RetryDelay;
                        }
                        else
                        {
                            await output.WriteAsync(ChatCompletionStreamEvent.Error(result.Code, result.SafeMessage)).ConfigureAwait(false);
                        }
                    }
                    else if (!HasEventStreamContentType(httpResponse))
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            KejiProviderErrorCode.InvalidContentType,
                            "Provider returned an invalid stream content type")).ConfigureAwait(false);
                    }
                    else
                    {
                        using (var streamTimeoutCts = CreateTimeoutTokenSource(ct))
                        {
                            responseStream = await httpResponse.Content.ReadAsStreamAsync(streamTimeoutCts.Token).ConfigureAwait(false);
                        }

                        streamCancellationRegistration = ct.Register(
                            static state => TryDisposeStream((Stream)state!),
                            responseStream);
                        reader = new SseEventReader(responseStream);
                        var streamState = new StreamParseState();
                        var chunkCount = 0;

                        while (true)
                        {
                            var sseData = await ReadSseDataWithTimeoutAsync(
                                reader,
                                responseStream,
                                ct).ConfigureAwait(false);

                            if (sseData is null)
                            {
                                if (streamState.ChoiceStates.Count > 0 &&
                                    streamState.ChoiceStates.Values.All(static state => state.Finished))
                                {
                                    await EmitTerminalEventsAsync(output, streamState).ConfigureAwait(false);
                                }
                                else
                                {
                                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                                        KejiProviderErrorCode.StreamTruncated,
                                        "Provider stream ended unexpectedly")).ConfigureAwait(false);
                                }
                                return;
                            }

                            if (string.Equals(sseData, "[DONE]", StringComparison.Ordinal))
                            {
                                if (streamState.ChoiceStates.Count == 0 ||
                                    streamState.ChoiceStates.Values.Any(static state => !state.Finished))
                                {
                                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                                        KejiProviderErrorCode.StreamTruncated,
                                        "Provider stream ended unexpectedly")).ConfigureAwait(false);
                                    return;
                                }

                                await EmitTerminalEventsAsync(output, streamState).ConfigureAwait(false);
                                return;
                            }

                            if (++chunkCount > MaxSseChunks)
                                throw new StreamProtocolException();

                            var chunk = JsonSerializer.Deserialize<StreamedChunk>(sseData, JsonOptions)
                                ?? throw new StreamProtocolException();
                            await ProcessChunkAsync(output, chunk, streamState).ConfigureAwait(false);
                        }
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (IOException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }
                catch (ProviderReadTimeoutException)
                {
                    if (!output.HasEmitted && attempt < MaxAttempts)
                    {
                        retryDelay = GetBackoffDelay(attempt);
                    }
                    else
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            KejiProviderErrorCode.Timeout,
                            "Provider request timed out")).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!output.HasEmitted && attempt < MaxAttempts)
                    {
                        retryDelay = GetBackoffDelay(attempt);
                    }
                    else
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            KejiProviderErrorCode.Timeout,
                            "Provider request timed out")).ConfigureAwait(false);
                    }
                }
                catch (HttpRequestException exception)
                {
                    if (!output.HasEmitted && attempt < MaxAttempts && IsRetryableException(exception))
                    {
                        retryDelay = GetBackoffDelay(attempt);
                    }
                    else
                    {
                        var (code, message) = output.HasEmitted
                            ? (KejiProviderErrorCode.StreamInterrupted, "Provider stream was interrupted")
                            : exception.StatusCode is { } statusCode
                                ? ProviderErrorMapper.Map(statusCode, responseBody: null)
                                : ProviderErrorMapper.MapFromException(exception);
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(code, message)).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    if (!output.HasEmitted && attempt < MaxAttempts)
                    {
                        retryDelay = GetBackoffDelay(attempt);
                    }
                    else
                    {
                        var (code, message) = output.HasEmitted
                            ? (KejiProviderErrorCode.StreamInterrupted, "Provider stream was interrupted")
                            : (KejiProviderErrorCode.ConnectionError, "Failed to connect to provider");
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(code, message)).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        KejiProviderErrorCode.StreamProtocolError,
                        "Provider stream violated the protocol")).ConfigureAwait(false);
                }
                catch (DecoderFallbackException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        KejiProviderErrorCode.StreamProtocolError,
                        "Provider stream violated the protocol")).ConfigureAwait(false);
                }
                catch (StreamProtocolException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        KejiProviderErrorCode.StreamProtocolError,
                        "Provider stream violated the protocol")).ConfigureAwait(false);
                }
                catch (KejiProviderSecretResolutionException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        KejiProviderErrorCode.ProviderError,
                        "Provider secret is unavailable")).ConfigureAwait(false);
                }
                finally
                {
                    streamCancellationRegistration.Dispose();
                    TryDisposeReader(reader);
                    if (responseStream is not null)
                        TryDisposeStream(responseStream);
                    TryDisposeResponse(httpResponse);
                    httpRequest?.Dispose();
                }

                if (retryDelay is { } delay)
                {
                    await DelayBeforeRetryAsync(delay, ct).ConfigureAwait(false);
                    continue;
                }

                return;
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task ProcessChunkAsync(
        StreamOutput output,
        StreamedChunk chunk,
        StreamParseState streamState)
    {
        if (chunk.Usage is not null)
        {
            if (streamState.UsageSeen || chunk.Choices is { Count: > 0 } ||
                streamState.ChoiceStates.Count == 0 ||
                streamState.ChoiceStates.Values.Any(static state => !state.Finished))
            {
                throw new StreamProtocolException();
            }

            var usage = MapAndValidateUsage(chunk.Usage) ?? throw new StreamProtocolException();
            streamState.UsageSeen = true;
            streamState.PendingUsage = usage;
        }
        else if (streamState.UsageSeen)
        {
            throw new StreamProtocolException();
        }

        if (chunk.Choices is null || chunk.Choices.Count == 0)
            return;
        if (chunk.Choices.Count > MaxChoices)
            throw new StreamProtocolException();
        if (chunk.Choices.Any(static choice => choice is null))
            throw new StreamProtocolException();

        var seenChoiceIndexes = new HashSet<int>();
        foreach (var choice in chunk.Choices.OrderBy(static item => item.Index))
        {
            if (choice.Index is < 0 or > MaxChoiceIndex || !seenChoiceIndexes.Add(choice.Index))
                throw new StreamProtocolException();
            if (!streamState.ChoiceStates.TryGetValue(choice.Index, out var state))
            {
                if (streamState.ChoiceStates.Count >= MaxChoices)
                    throw new StreamProtocolException();
                state = new ChoiceStreamState();
                streamState.ChoiceStates.Add(choice.Index, state);
            }

            if (state.Finished)
                throw new StreamProtocolException();

            if (choice.Delta is { } delta)
                await ProcessDeltaAsync(output, choice.Index, state, delta).ConfigureAwait(false);

            if (choice.FinishReason is not null)
            {
                var finishReason = MapFinishReason(choice.FinishReason);
                await CloseActiveToolCallsAsync(output, choice.Index, state).ConfigureAwait(false);
                state.FinishReason = finishReason;
                state.Finished = true;
            }
        }
    }

    private static async Task EmitTerminalEventsAsync(
        StreamOutput output,
        StreamParseState streamState)
    {
        foreach (var (choiceIndex, state) in streamState.ChoiceStates.OrderBy(static pair => pair.Key))
        {
            if (!state.Finished || state.FinishReason == KejiFinishReason.Invalid)
                throw new StreamProtocolException();

            await output.WriteAsync(ChatCompletionStreamEvent.ChoiceFinished(
                state.FinishReason,
                state.ToolCallCount > 0,
                choiceIndex)).ConfigureAwait(false);
        }

        if (streamState.PendingUsage is not null)
            await output.WriteAsync(ChatCompletionStreamEvent.UsageEvent(streamState.PendingUsage)).ConfigureAwait(false);

        await output.WriteAsync(ChatCompletionStreamEvent.Done()).ConfigureAwait(false);
    }

    private static async Task ProcessDeltaAsync(
        StreamOutput output,
        int choiceIndex,
        ChoiceStreamState state,
        StreamedDelta delta)
    {
        if (!string.IsNullOrEmpty(delta.ReasoningContent))
        {
            AddBoundedUtf8Bytes(
                delta.ReasoningContent,
                ref state.TotalReasoningBytes,
                MaxReasoningBytesPerChoice);
            await output.WriteAsync(ChatCompletionStreamEvent.ReasoningToken(
                delta.ReasoningContent,
                choiceIndex)).ConfigureAwait(false);
        }

        if (!string.IsNullOrEmpty(delta.Content))
        {
            AddBoundedUtf8Bytes(delta.Content, ref state.TotalContentBytes, MaxContentBytesPerChoice);
            await output.WriteAsync(ChatCompletionStreamEvent.Token(delta.Content, choiceIndex)).ConfigureAwait(false);
        }

        if (delta.ToolCalls is not { Count: > 0 })
            return;
        if (delta.ToolCalls.Any(static toolCall => toolCall is null))
            throw new StreamProtocolException();

        foreach (var toolCall in delta.ToolCalls.OrderBy(static item => item.Index))
        {
            if (toolCall.Index is < 0 or > MaxChoiceIndex)
                throw new StreamProtocolException();
            if (!state.ToolCalls.TryGetValue(toolCall.Index, out var toolState))
            {
                toolState = new ToolCallState();
                state.ToolCalls.Add(toolCall.Index, toolState);
            }

            var incomingId = toolCall.Id;
            var incomingName = toolCall.Function?.Name;
            if (!string.IsNullOrEmpty(incomingId))
            {
                ValidateProtocolIdentifier(incomingId, MaxToolCallIdLength);
                if (toolState.Active && !string.Equals(toolState.Id, incomingId, StringComparison.Ordinal))
                {
                    ValidateToolArguments(toolState.Arguments.ToString());
                    await output.WriteAsync(ChatCompletionStreamEvent.ToolCallEnd(
                        toolState.Id,
                        toolCall.Index,
                        choiceIndex)).ConfigureAwait(false);
                    toolState.End();
                }

                if (!toolState.Active)
                {
                    if (state.ToolCallCount >= MaxToolCallsPerChoice || string.IsNullOrWhiteSpace(incomingName))
                        throw new StreamProtocolException();
                    ValidateToolName(incomingName);
                    if (!state.ToolCallIds.Add(incomingId))
                        throw new StreamProtocolException();

                    state.ToolCallCount++;
                    toolState.Begin(incomingId, incomingName);
                    await output.WriteAsync(ChatCompletionStreamEvent.ToolCallBegin(
                        incomingId,
                        incomingName,
                        toolCall.Index,
                        choiceIndex)).ConfigureAwait(false);
                }
                else if (!string.IsNullOrEmpty(incomingName) &&
                         !string.Equals(toolState.Name, incomingName, StringComparison.Ordinal))
                {
                    throw new StreamProtocolException();
                }
            }
            else if (!string.IsNullOrEmpty(incomingName))
            {
                if (!toolState.Active || !string.Equals(toolState.Name, incomingName, StringComparison.Ordinal))
                    throw new StreamProtocolException();
            }

            if (toolCall.Function?.Arguments is { Length: > 0 } arguments)
            {
                if (!toolState.Active)
                    throw new StreamProtocolException();

                var argumentBytes = Encoding.UTF8.GetByteCount(arguments);
                if (argumentBytes > MaxToolArgumentsBytesPerCall - toolState.ArgumentBytes ||
                    argumentBytes > MaxToolArgumentsBytesPerChoice - state.TotalToolArgumentBytes)
                {
                    throw new StreamProtocolException();
                }

                toolState.ArgumentBytes += argumentBytes;
                state.TotalToolArgumentBytes += argumentBytes;
                toolState.Arguments.Append(arguments);
                await output.WriteAsync(ChatCompletionStreamEvent.ToolCallDelta(
                    arguments,
                    toolCall.Index,
                    choiceIndex,
                    toolState.Id)).ConfigureAwait(false);
            }
        }
    }

    private static async Task CloseActiveToolCallsAsync(
        StreamOutput output,
        int choiceIndex,
        ChoiceStreamState state)
    {
        foreach (var (toolCallIndex, toolState) in state.ToolCalls.OrderBy(static pair => pair.Key))
        {
            if (!toolState.Active)
                continue;

            ValidateToolArguments(toolState.Arguments.ToString());
            await output.WriteAsync(ChatCompletionStreamEvent.ToolCallEnd(
                toolState.Id,
                toolCallIndex,
                choiceIndex)).ConfigureAwait(false);
            toolState.End();
        }
    }

    private HttpRequestMessage BuildRequest(ChatCompletionRequest request, bool stream)
    {
        var effectiveModel = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model.Trim();
        var effectiveMaxTokens = request.MaxTokens ?? DefaultMaxTokens;

        var messages = request.Messages.Select(message => new OutboundMessage
        {
            Role = RoleToString(message.Role),
            Content = message.Role == KejiChatRole.Assistant && !message.ToolCalls.IsDefaultOrEmpty
                    ? null
                    : message.Content,
            Name = message.Name,
            ToolCallId = message.ToolCallId,
            ReasoningContent = message.Role == KejiChatRole.Assistant &&
                BackfillAssistantReasoningContent
                    ? message.ReasoningContent ?? string.Empty
                    : message.ReasoningContent,
            ToolCalls = !message.ToolCalls.IsDefaultOrEmpty
                ? message.ToolCalls.Select(static toolCall => new OutboundToolCall
                {
                    Id = toolCall.Id,
                    Type = toolCall.Type,
                    Function = new OutboundFunction
                    {
                        Name = toolCall.FunctionName,
                        Arguments = toolCall.FunctionArguments.GetRawText(),
                    },
                }).ToList()
                : null,
        }).ToList();

        var tools = request.HasTools
            ? request.Tools!.Select(static tool => new
            {
                type = "function",
                function = new
                {
                    name = tool.Name,
                    description = tool.Description,
                    parameters = ParseToolSchema(tool.InputSchemaJson),
                },
            }).ToList()
            : null;

        var body = new
        {
            model = effectiveModel,
            messages,
            stream,
            stream_options = stream ? new { include_usage = true } : null,
            tools,
            temperature = request.Temperature,
            max_tokens = effectiveMaxTokens,
        };

        var endpoint = new Uri(new Uri(BaseUri, UriKind.Absolute), "chat/completions");
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions),
                Encoding.UTF8,
                "application/json"),
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            stream ? "text/event-stream" : "application/json"));

        var authHeader = AuthHeader;
        if (authHeader is not null)
            httpRequest.Headers.Authorization = authHeader;

        return httpRequest;
    }

    private bool TryValidateRequest(ChatCompletionRequest request, out string errorCode)
    {
        errorCode = "INVALID_REQUEST";
        var model = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model;
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256 || model.Any(char.IsControl))
            return false;
        if (request.Messages.IsDefaultOrEmpty || request.Messages.Length is < 1 or > MaxMessages)
            return false;
        if (request.Temperature is { } temperature &&
            (!double.IsFinite(temperature) || temperature is < 0 or > 2))
            return false;

        var maxTokens = request.MaxTokens ?? DefaultMaxTokens;
        if (maxTokens is < 1 or > 131072 || maxTokens > DefaultMaxTokens)
            return false;

        long totalTextBytes = Encoding.UTF8.GetByteCount(model);
        foreach (var message in request.Messages)
        {
            var roleString = RoleToString(message.Role);
            if (message.Role == KejiChatRole.Invalid ||
                message.Name is { } name && !IsValidMessageName(name) ||
                message.ReasoningContent is not null &&
                    (message.Role != KejiChatRole.Assistant ||
                     Encoding.UTF8.GetByteCount(message.ReasoningContent) > MaxReasoningBytesPerChoice))
            {
                return false;
            }

            var isAssistant = message.Role == KejiChatRole.Assistant;
            var isTool = message.Role == KejiChatRole.Tool;
            var isFunction = message.Role == KejiChatRole.Function;
            if (!isAssistant && message.Content is null ||
                isAssistant && message.Content is null &&
                    message.ReasoningContent is null && message.ToolCalls.IsDefaultOrEmpty ||
                isAssistant && !message.ToolCalls.IsDefaultOrEmpty &&
                    !string.IsNullOrEmpty(message.Content) ||
                isTool != (message.ToolCallId is not null) ||
                isTool && !IsValidProtocolIdentifier(message.ToolCallId, MaxToolCallIdLength) ||
                isFunction && message.Name is null ||
                !isAssistant && !message.ToolCalls.IsDefaultOrEmpty)
            {
                return false;
            }

            var contentBytes = message.Content is null ? 0 : Encoding.UTF8.GetByteCount(message.Content);
            if (contentBytes > MaxMessageBytes)
                return false;
            totalTextBytes += contentBytes + Encoding.UTF8.GetByteCount(roleString);
            if (message.Name is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.Name);
            if (message.ToolCallId is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.ToolCallId);
            if (message.ReasoningContent is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.ReasoningContent);

            if (!message.ToolCalls.IsDefaultOrEmpty)
            {
                if (message.ToolCalls.Length > MaxTools)
                    return false;
                foreach (var toolCall in message.ToolCalls)
                {
                    var argsText = toolCall.FunctionArguments.ValueKind == JsonValueKind.Undefined ? "" : toolCall.FunctionArguments.GetRawText();
                    if (!IsValidProtocolIdentifier(toolCall.Id, MaxToolCallIdLength) ||
                        !string.Equals(toolCall.Type, "function", StringComparison.Ordinal) ||
                        !IsValidToolName(toolCall.FunctionName) ||
                        !IsValidToolArguments(argsText))
                    {
                        return false;
                    }

                    var argumentBytes = Encoding.UTF8.GetByteCount(argsText);
                    if (argumentBytes > MaxToolArgumentsBytesPerCall)
                        return false;
                    totalTextBytes += argumentBytes + Encoding.UTF8.GetByteCount(toolCall.Id) +
                        Encoding.UTF8.GetByteCount(toolCall.FunctionName);
                }
            }
        }

        if (!request.Tools.IsDefaultOrEmpty)
        {
            if (request.Tools.Length > MaxTools)
                return false;
            foreach (var tool in request.Tools)
            {
                if (tool is null || !IsValidToolName(tool.Name) || tool.Description is null ||
                    Encoding.UTF8.GetByteCount(tool.Description) > MaxToolDescriptionBytes)
                {
                    return false;
                }

                totalTextBytes += Encoding.UTF8.GetByteCount(tool.Name) +
                    Encoding.UTF8.GetByteCount(tool.Description);

                if (tool.InputSchemaJson is { } schema)
                {
                    var schemaBytes = Encoding.UTF8.GetByteCount(schema);
                    if (schemaBytes > MaxToolSchemaBytes)
                        return false;
                    totalTextBytes += schemaBytes;
                    try
                    {
                        using var schemaDocument = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 64 });
                        if (schemaDocument.RootElement.ValueKind != JsonValueKind.Object)
                            return false;
                    }
                    catch (JsonException)
                    {
                        return false;
                    }
                }
            }
        }

        return totalTextBytes <= MaxRequestTextBytes;
    }

    private static JsonElement? ParseToolSchema(string? schema)
    {
        if (schema is null)
            return null;

        using var document = JsonDocument.Parse(schema, new JsonDocumentOptions { MaxDepth = 64 });
        return document.RootElement.Clone();
    }

    private CancellationTokenSource CreateTimeoutTokenSource(CancellationToken ct)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);
        return timeoutCts;
    }

    private async Task<string?> ReadSseDataWithTimeoutAsync(
        SseEventReader reader,
        Stream responseStream,
        CancellationToken ct)
    {
        using var readTimeoutCts = CreateTimeoutTokenSource(ct);
        var readTimedOut = 0;
        using var timeoutRegistration = readTimeoutCts.Token.Register(() =>
        {
            if (!ct.IsCancellationRequested)
            {
                Interlocked.Exchange(ref readTimedOut, 1);
                TryDisposeStream(responseStream);
            }
        });

        try
        {
            var data = await reader.ReadDataAsync(readTimeoutCts.Token).ConfigureAwait(false);
            if (Volatile.Read(ref readTimedOut) != 0)
                throw new ProviderReadTimeoutException();
            return data;
        }
        catch (Exception exception) when (
            Volatile.Read(ref readTimedOut) != 0 &&
            exception is OperationCanceledException or IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new ProviderReadTimeoutException();
        }
    }

    private static void TryDisposeStream(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception)
        {
            // Cancellation disposal is best-effort; the producer observes the cancellation separately.
        }
    }

    private static void TryDisposeResponse(HttpResponseMessage? response)
    {
        if (response is null)
            return;

        try
        {
            response.Dispose();
        }
        catch (Exception)
        {
            // A provider-controlled response cannot replace the sanitized boundary result during cleanup.
        }
    }

    private static void TryDisposeReader(SseEventReader? reader)
    {
        if (reader is null)
            return;

        try
        {
            reader.Dispose();
        }
        catch (Exception)
        {
            // Reader cleanup is best-effort and cannot replace a sanitized provider result.
        }
    }

    private async Task<T?> DeserializeBoundedAsync<T>(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes)
            throw new ProviderResponseTooLargeException();

        var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        using var cancellationRegistration = ct.Register(
            static state => TryDisposeStream((Stream)state!),
            source);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (destination.Length + read > MaxResponseBytes)
                    throw new ProviderResponseTooLargeException();
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();

            var result = JsonSerializer.Deserialize<T>(
                destination.GetBuffer().AsSpan(0, checked((int)destination.Length)),
                JsonOptions);
            ct.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception exception) when (
            ct.IsCancellationRequested &&
            exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            throw new OperationCanceledException(ct);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            TryDisposeStream(source);
        }
    }

    private static TimeSpan GetRetryDelay(HttpResponseMessage response, int retryNumber)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta > MaxRetryAfter ? MaxRetryAfter : delta;

        if (retryAfter?.Date is { } date)
        {
            var dateDelay = date - DateTimeOffset.UtcNow;
            if (dateDelay >= TimeSpan.Zero)
                return dateDelay > MaxRetryAfter ? MaxRetryAfter : dateDelay;
        }

        return GetBackoffDelay(retryNumber);
    }

    private static TimeSpan GetBackoffDelay(int attempt)
    {
        var index = Math.Clamp(attempt - 1, 0, DefaultBackoff.Length - 1);
        return DefaultBackoff[index];
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        var numericStatus = (int)statusCode;
        return numericStatus is 408 or 409 or 429 || numericStatus >= 500;
    }

    private static bool IsRetryableException(HttpRequestException exception) =>
        exception.StatusCode is null || IsRetryableStatus(exception.StatusCode.Value);

    private static bool HasEventStreamContentType(HttpResponseMessage response) =>
        string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/event-stream",
            StringComparison.OrdinalIgnoreCase) &&
        IsUtf8Charset(response.Content.Headers.ContentType?.CharSet);

    private static bool IsUtf8Charset(string? charset)
    {
        if (string.IsNullOrWhiteSpace(charset))
            return true;

        return string.Equals(charset.Trim().Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase);
    }

    private static TokenUsage? MapAndValidateUsage(OpenAiUsage? usage)
    {
        if (usage is null)
            return null;
        if (usage.PromptTokens is < 0 or > MaxReportedTokens ||
            usage.CompletionTokens is < 0 or > MaxReportedTokens)
        {
            throw new StreamProtocolException();
        }

        var cachedTokens = usage.CachedTokens;
        if (usage.PromptTokensDetails?.CachedTokens is { } dtCached)
            cachedTokens = dtCached;
        if (usage.PromptCacheHitTokens is { } hitTokens)
            cachedTokens = hitTokens;

        cachedTokens ??= 0;
        if (cachedTokens < 0 || cachedTokens > MaxReportedTokens || cachedTokens > usage.PromptTokens)
            throw new StreamProtocolException();

        var totalTokens = usage.PromptTokens + usage.CompletionTokens;
        if (totalTokens > MaxReportedTokens)
            throw new StreamProtocolException();

        if (usage.TotalTokens is { } reportedTotal)
        {
            if (reportedTotal < 0 || reportedTotal > MaxReportedTokens)
                throw new StreamProtocolException();
            if (reportedTotal < totalTokens)
                throw new StreamProtocolException();
        }

        return new TokenUsage
        {
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
            CachedTokens = cachedTokens.Value,
        };
    }

    private static ImmutableArray<ChatToolCall> MapAndValidateToolCalls(
        List<OpenAiToolCall>? providerToolCalls)
    {
        if (providerToolCalls is null)
            return default;
        if (providerToolCalls.Count > MaxToolCallsPerChoice)
            throw new StreamProtocolException();
        if (providerToolCalls.Any(static toolCall => toolCall is null))
            throw new StreamProtocolException();

        var builder = ImmutableArray.CreateBuilder<ChatToolCall>(providerToolCalls.Count);
        var totalArgumentBytes = 0;
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var providerToolCall in providerToolCalls)
        {
            var id = providerToolCall.Id;
            var name = providerToolCall.Function?.Name;
            var arguments = providerToolCall.Function?.Arguments;
            if (!IsValidProtocolIdentifier(id, MaxToolCallIdLength) || !identifiers.Add(id!) ||
                !string.Equals(providerToolCall.Type, "function", StringComparison.Ordinal) ||
                !IsValidToolName(name) || arguments is null)
            {
                throw new StreamProtocolException();
            }

            var argumentBytes = Encoding.UTF8.GetByteCount(arguments);
            if (argumentBytes > MaxToolArgumentsBytesPerCall ||
                argumentBytes > MaxToolArgumentsBytesPerChoice - totalArgumentBytes)
            {
                throw new StreamProtocolException();
            }
            totalArgumentBytes += argumentBytes;
            ValidateToolArguments(arguments);

            using var argsDoc = JsonDocument.Parse(arguments, new JsonDocumentOptions { MaxDepth = 64 });
            builder.Add(new ChatToolCall
            {
                Id = id!,
                Type = "function",
                FunctionName = name!,
                FunctionArguments = argsDoc.RootElement.Clone(),
            });
        }

        return builder.MoveToImmutable();
    }

    private static string ValidateResponseModel(string model)
    {
        if (model.Length > 256 || model.Any(char.IsControl))
            throw new StreamProtocolException();
        return model;
    }

    private static void ValidateToolArguments(string arguments)
    {
        if (!IsValidToolArguments(arguments))
            throw new StreamProtocolException();
    }

    private static bool IsValidToolArguments(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;

        try
        {
            using var document = JsonDocument.Parse(
                arguments,
                new JsonDocumentOptions { MaxDepth = 64 });
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static KejiFinishReason MapFinishReason(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason) || finishReason.Length > 64 ||
            finishReason.Any(char.IsControl))
        {
            throw new StreamProtocolException();
        }

        if (!IsValidUnicode(finishReason))
            throw new StreamProtocolException();

        return finishReason.Trim().ToLowerInvariant() switch
        {
            "stop" => KejiFinishReason.Stop,
            "length" => KejiFinishReason.Length,
            "tool_calls" => KejiFinishReason.ToolCalls,
            "content_filter" => KejiFinishReason.ContentFilter,
            "refusal" => KejiFinishReason.ContentFilter,
            "error" => KejiFinishReason.Error,
            "end_turn" => KejiFinishReason.EndTurn,
            _ => throw new StreamProtocolException(),
        };
    }

    private static string FinishReasonToString(KejiFinishReason reason) => reason switch
    {
        KejiFinishReason.Stop => "stop",
        KejiFinishReason.Length => "length",
        KejiFinishReason.ToolCalls => "tool_calls",
        KejiFinishReason.ContentFilter => "content_filter",
        KejiFinishReason.Error => "error",
        KejiFinishReason.EndTurn => "end_turn",
        _ => "unknown",
    };

    private static string RoleToString(KejiChatRole role) => role switch
    {
        KejiChatRole.System => "system",
        KejiChatRole.Developer => "developer",
        KejiChatRole.User => "user",
        KejiChatRole.Assistant => "assistant",
        KejiChatRole.Tool => "tool",
        KejiChatRole.Function => "function",
        _ => "",
    };

    private static async Task<string?> ReadErrorBodyAsync(HttpContent content, CancellationToken ct)
    {
        try
        {
            var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var destination = new MemoryStream();
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            try
            {
                var totalRead = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                    if (read == 0)
                        break;
                    totalRead += read;
                    if (totalRead > MaxErrorBodyBytes)
                    {
                        totalRead = MaxErrorBodyBytes;
                        break;
                    }
                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                }

                return Encoding.UTF8.GetString(destination.GetBuffer().AsSpan(0, checked((int)destination.Length)));
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                TryDisposeStream(source);
            }
        }
        catch
        {
            return null;
        }
    }

    private const int MaxErrorBodyBytes = 64 * 1024;

    internal readonly struct ProviderHttpErrorResult
    {
        public KejiProviderErrorCode Code { get; }
        public string SafeMessage { get; }
        public bool Retryable { get; }
        public TimeSpan RetryDelay { get; }

        public ProviderHttpErrorResult(KejiProviderErrorCode code, string safeMessage, bool retryable, TimeSpan retryDelay)
        {
            Code = code;
            SafeMessage = safeMessage;
            Retryable = retryable;
            RetryDelay = retryDelay;
        }
    }

    private static async Task<ProviderHttpErrorResult> ClassifyErrorAsync(
        HttpResponseMessage response, int attempt, CancellationToken ct)
    {
        var body = await ReadErrorBodyAsync(response.Content, ct).ConfigureAwait(false);
        var (code, message) = ProviderErrorMapper.Map(response.StatusCode, body);

        if (attempt >= MaxAttempts)
            return new ProviderHttpErrorResult(code, message, false, TimeSpan.Zero);

        var statusCode = (int)response.StatusCode;

        if (statusCode == 429 && code != KejiProviderErrorCode.RateLimited)
            return new ProviderHttpErrorResult(code, message, false, TimeSpan.Zero);

        if (statusCode is 408 or 409 or 429 || statusCode >= 500)
        {
            var delay = GetRetryDelay(response, attempt);
            return new ProviderHttpErrorResult(code, message, true, delay);
        }

        return new ProviderHttpErrorResult(code, message, false, TimeSpan.Zero);
    }

    private static bool IsValidMessageRole(string? role) =>
        role is "system" or "developer" or "user" or "assistant" or "tool" or "function";

    private static bool IsValidMessageName(string name) =>
        name.Length is > 0 and <= MaxMessageNameLength &&
        name.All(static character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-');

    private static bool HasVisibleDelta(StreamedDelta? delta) =>
        delta is not null &&
        (!string.IsNullOrEmpty(delta.Content) ||
         !string.IsNullOrEmpty(delta.ReasoningContent) ||
         delta.ToolCalls is { Count: > 0 });

    private static void AddBoundedUtf8Bytes(string value, ref int currentBytes, int maximumBytes)
    {
        var addedBytes = Encoding.UTF8.GetByteCount(value);
        if (addedBytes > maximumBytes - currentBytes)
            throw new StreamProtocolException();
        currentBytes += addedBytes;
    }

    private static void ValidateProtocolIdentifier(string value, int maximumLength)
    {
        if (!IsValidProtocolIdentifier(value, maximumLength))
            throw new StreamProtocolException();
    }

    private static bool IsValidProtocolIdentifier(string? value, int maximumLength) =>
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

    private static void ValidateToolName(string value)
    {
        if (!IsValidToolName(value))
            throw new StreamProtocolException();
    }

    private static bool IsValidToolName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= MaxToolNameLength &&
        value.All(static character =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.');

    private sealed class StreamOutput
    {
        private readonly ChannelWriter<ChatCompletionStreamEvent> _writer;
        private readonly CancellationToken _ct;

        public StreamOutput(ChannelWriter<ChatCompletionStreamEvent> writer, CancellationToken ct)
        {
            _writer = writer;
            _ct = ct;
        }

        public bool HasEmitted { get; private set; }

        public async ValueTask WriteAsync(ChatCompletionStreamEvent streamEvent)
        {
            await _writer.WriteAsync(streamEvent, _ct).ConfigureAwait(false);
            HasEmitted = true;
        }
    }

    private sealed class SseEventReader : IDisposable
    {
        private readonly StreamReader _reader;
        private readonly char[] _buffer = new char[4096];
        private int _bufferOffset;
        private int _bufferLength;
        private bool _skipLeadingLineFeed;
        private bool _isFirstCharacter = true;
        private bool _disposed;

        public SseEventReader(Stream stream)
        {
            _reader = new StreamReader(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
        }

        public async Task<string?> ReadDataAsync(CancellationToken ct)
        {
            StringBuilder? data = null;
            var dataBytes = 0;
            var lineCount = 0;

            while (true)
            {
                var line = await ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                    return null;

                if (line.Length == 0)
                {
                    lineCount = 0;
                    if (data is null)
                        continue;

                    if (data.Length > 0 && data[^1] == '\n')
                        data.Length--;
                    return data.ToString();
                }

                lineCount++;
                if (lineCount > MaxSseLinesPerEvent)
                    throw new StreamProtocolException();
                if (line[0] == ':')
                    continue;

                var colonIndex = line.IndexOf(':');
                var field = colonIndex < 0 ? line : line[..colonIndex];
                var value = colonIndex < 0 ? string.Empty : line[(colonIndex + 1)..];
                if (value.StartsWith(' '))
                    value = value[1..];
                if (!string.Equals(field, "data", StringComparison.Ordinal))
                    continue;

                var addedBytes = Encoding.UTF8.GetByteCount(value) + 1;
                if (addedBytes > MaxSseEventDataBytes - dataBytes)
                    throw new StreamProtocolException();
                dataBytes += addedBytes;
                data ??= new StringBuilder();
                data.Append(value).Append('\n');
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _reader.Dispose();
        }

        private async Task<string?> ReadLineAsync(CancellationToken ct)
        {
            var line = new StringBuilder();
            var utf8Bytes = 0;
            var hasPendingHighSurrogate = false;

            while (true)
            {
                var next = await ReadCharacterAsync(ct).ConfigureAwait(false);
                if (next < 0)
                {
                    if (line.Length == 0)
                        return null;
                    if (hasPendingHighSurrogate)
                        utf8Bytes += 3;
                    EnsureLineLimit(utf8Bytes);
                    return line.ToString();
                }

                var character = (char)next;
                if (character is '\r' or '\n')
                {
                    if (character == '\r')
                        _skipLeadingLineFeed = true;
                    if (hasPendingHighSurrogate)
                        utf8Bytes += 3;
                    EnsureLineLimit(utf8Bytes);
                    return line.ToString();
                }

                line.Append(character);
                if (hasPendingHighSurrogate)
                {
                    utf8Bytes += char.IsLowSurrogate(character) ? 4 : 3;
                    hasPendingHighSurrogate = false;
                    if (!char.IsLowSurrogate(character))
                        CountCharacterBytes(character, ref utf8Bytes, ref hasPendingHighSurrogate);
                }
                else
                {
                    CountCharacterBytes(character, ref utf8Bytes, ref hasPendingHighSurrogate);
                }

                EnsureLineLimit(utf8Bytes);
            }
        }

        private async ValueTask<int> ReadCharacterAsync(CancellationToken ct)
        {
            while (true)
            {
                if (_bufferOffset >= _bufferLength)
                {
                    _bufferLength = await _reader.ReadAsync(_buffer.AsMemory(), ct).ConfigureAwait(false);
                    _bufferOffset = 0;
                    if (_bufferLength == 0)
                        return -1;
                }

                var character = _buffer[_bufferOffset++];
                if (_isFirstCharacter)
                {
                    _isFirstCharacter = false;
                    if (character == '\uFEFF')
                        continue;
                }

                if (_skipLeadingLineFeed)
                {
                    _skipLeadingLineFeed = false;
                    if (character == '\n')
                        continue;
                }

                return character;
            }
        }

        private static void CountCharacterBytes(
            char character,
            ref int utf8Bytes,
            ref bool hasPendingHighSurrogate)
        {
            if (char.IsHighSurrogate(character))
            {
                hasPendingHighSurrogate = true;
            }
            else if (character <= 0x7f)
            {
                utf8Bytes++;
            }
            else if (character <= 0x7ff)
            {
                utf8Bytes += 2;
            }
            else
            {
                utf8Bytes += 3;
            }
        }

        private static void EnsureLineLimit(int utf8Bytes)
        {
            if (utf8Bytes > MaxSseLineBytes)
                throw new StreamProtocolException();
        }
    }

    private sealed class StreamParseState
    {
        public Dictionary<int, ChoiceStreamState> ChoiceStates { get; } = new();
        public bool UsageSeen;
        public TokenUsage? PendingUsage;
    }

    private sealed class ChoiceStreamState
    {
        public Dictionary<int, ToolCallState> ToolCalls { get; } = new();
        public HashSet<string> ToolCallIds { get; } = new(StringComparer.Ordinal);
        public int TotalContentBytes;
        public int TotalReasoningBytes;
        public int TotalToolArgumentBytes;
        public int ToolCallCount;
        public bool Finished;
        public KejiFinishReason FinishReason;
    }

    private sealed class ToolCallState
    {
        public string? Id { get; private set; }
        public string? Name { get; private set; }
        public int ArgumentBytes;
        public StringBuilder Arguments { get; } = new();
        public bool Active { get; private set; }

        public void Begin(string id, string name)
        {
            Id = id;
            Name = name;
            ArgumentBytes = 0;
            Arguments.Clear();
            Active = true;
        }

        public void End()
        {
            Active = false;
            Id = null;
            Name = null;
            ArgumentBytes = 0;
            Arguments.Clear();
        }
    }

    private sealed class ProviderResponseTooLargeException : Exception;
    private sealed class ProviderReadTimeoutException : Exception;
    private sealed class StreamProtocolException : Exception;

    private sealed class OutboundMessage
    {
        public required string Role { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public string? Content { get; init; }

        public string? Name { get; init; }
        public string? ToolCallId { get; init; }
        public string? ReasoningContent { get; init; }
        public List<OutboundToolCall>? ToolCalls { get; init; }
    }

    private sealed class OutboundToolCall
    {
        public required string Id { get; init; }
        public required string Type { get; init; }
        public required OutboundFunction Function { get; init; }
    }

    private sealed class OutboundFunction
    {
        public required string Name { get; init; }
        public required string Arguments { get; init; }
    }

    private sealed class OpenAiChatCompletionResponse
    {
        public string? Id { get; init; }
        public string? Model { get; init; }
        public List<OpenAiChoice>? Choices { get; init; }
        public OpenAiUsage? Usage { get; init; }
    }

    private sealed class OpenAiChoice
    {
        public int Index { get; init; }
        public OpenAiMessage? Message { get; init; }
        public string? FinishReason { get; init; }
    }

    private sealed class OpenAiMessage
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public string? ReasoningContent { get; init; }
        public List<OpenAiToolCall>? ToolCalls { get; init; }
    }

    private sealed class OpenAiToolCall
    {
        public string? Id { get; init; }
        public string? Type { get; init; }
        public int Index { get; init; }
        public OpenAiFunction? Function { get; init; }
    }

    private sealed class OpenAiFunction
    {
        public string? Name { get; init; }
        public string? Arguments { get; init; }
    }

    private sealed class OpenAiUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public long PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public long CompletionTokens { get; init; }

        [JsonPropertyName("total_tokens")]
        public long? TotalTokens { get; init; }

        [JsonPropertyName("cached_tokens")]
        public long? CachedTokens { get; init; }

        [JsonPropertyName("prompt_tokens_details")]
        public PromptTokensDetails? PromptTokensDetails { get; init; }

        [JsonPropertyName("prompt_cache_hit_tokens")]
        public long? PromptCacheHitTokens { get; init; }
    }

    private sealed class PromptTokensDetails
    {
        [JsonPropertyName("cached_tokens")]
        public long? CachedTokens { get; init; }
    }

    private sealed class StreamedChunk
    {
        public string? Id { get; init; }
        public string? Model { get; init; }
        public List<StreamedChoice>? Choices { get; init; }
        public OpenAiUsage? Usage { get; init; }
    }

    private sealed class StreamedChoice
    {
        public int Index { get; init; }
        public StreamedDelta? Delta { get; init; }
        public string? FinishReason { get; init; }
    }

    private sealed class StreamedDelta
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public string? ReasoningContent { get; init; }
        public List<OpenAiToolCall>? ToolCalls { get; init; }
    }
}
