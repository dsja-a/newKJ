using System.Buffers;
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
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

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
            return ChatCompletionResponse.Failed("INVALID_REQUEST", "Model request is invalid");

        var retryCount = 0;
        while (true)
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
                    if (retryCount < MaxRetries && IsRetryableStatus(response.StatusCode))
                    {
                        var retryDelay = GetRetryDelay(response, retryCount + 1);
                        TryDisposeResponse(response);
                        response = null;
                        retryCount++;
                        await DelayBeforeRetryAsync(retryDelay, ct).ConfigureAwait(false);
                        continue;
                    }

                    var mapped = ProviderErrorMapper.Map(response.StatusCode, responseBody: null);
                    return ChatCompletionResponse.Failed(mapped.Code, mapped.Message);
                }

                var json = await DeserializeBoundedAsync<OpenAiChatCompletionResponse>(
                    response.Content,
                    timeoutCts.Token).ConfigureAwait(false);
                if (json is null)
                    return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");

                if (json.Choices is not { Count: > 0 and <= MaxChoices } ||
                    json.Choices.Any(static choice => choice is null) ||
                    json.Choices.Any(static choice => choice.Index is < 0 or > MaxChoiceIndex) ||
                    json.Choices.Select(static choice => choice.Index).Distinct().Count() != json.Choices.Count)
                    return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");

                var choice = json.Choices.OrderBy(static item => item.Index).First();
                if (choice.Message is null)
                    return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");

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
                var finishReason = ValidateFinishReason(choice.FinishReason);

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
                return ChatCompletionResponse.Failed("CANCELLED", "Request was cancelled");
            }
            catch (OperationCanceledException)
            {
                if (retryCount < MaxRetries)
                {
                    retryCount++;
                    try
                    {
                        await DelayBeforeRetryAsync(GetBackoffDelay(retryCount), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return ChatCompletionResponse.Failed("CANCELLED", "Request was cancelled");
                    }
                    continue;
                }

                return ChatCompletionResponse.Failed("TIMEOUT", "Provider request timed out");
            }
            catch (ProviderResponseTooLargeException)
            {
                return ChatCompletionResponse.Failed("RESPONSE_TOO_LARGE", "Provider response exceeded the size limit");
            }
            catch (JsonException)
            {
                return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");
            }
            catch (InvalidDataException)
            {
                return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");
            }
            catch (StreamProtocolException)
            {
                return ChatCompletionResponse.Failed("INVALID_RESPONSE", "Provider returned an invalid response");
            }
            catch (HttpRequestException exception)
            {
                if (retryCount < MaxRetries && IsRetryableException(exception))
                {
                    retryCount++;
                    try
                    {
                        await DelayBeforeRetryAsync(GetBackoffDelay(retryCount), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return ChatCompletionResponse.Failed("CANCELLED", "Request was cancelled");
                    }
                    continue;
                }

                var mapped = exception.StatusCode is { } statusCode
                    ? ProviderErrorMapper.Map(statusCode, responseBody: null)
                    : ProviderErrorMapper.MapFromException(exception);
                return ChatCompletionResponse.Failed(mapped.Code, mapped.Message);
            }
            catch (IOException)
            {
                if (retryCount < MaxRetries)
                {
                    retryCount++;
                    try
                    {
                        await DelayBeforeRetryAsync(GetBackoffDelay(retryCount), ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return ChatCompletionResponse.Failed("CANCELLED", "Request was cancelled");
                    }
                    continue;
                }

                return ChatCompletionResponse.Failed("CONNECTION_ERROR", "Failed to connect to provider");
            }
            finally
            {
                TryDisposeResponse(response);
            }
        }
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
                    "INVALID_REQUEST",
                    "Model request is invalid")).ConfigureAwait(false);
                return;
            }

            var retryCount = 0;
            while (true)
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
                        if (!output.HasEmitted && retryCount < MaxRetries && IsRetryableStatus(httpResponse.StatusCode))
                        {
                            retryDelay = GetRetryDelay(httpResponse, retryCount + 1);
                        }
                        else
                        {
                            var mapped = ProviderErrorMapper.Map(httpResponse.StatusCode, responseBody: null);
                            await output.WriteAsync(ChatCompletionStreamEvent.Error(mapped.Code, mapped.Message)).ConfigureAwait(false);
                        }
                    }
                    else if (!HasEventStreamContentType(httpResponse))
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            "INVALID_CONTENT_TYPE",
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
                                        "STREAM_TRUNCATED",
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
                                        "STREAM_TRUNCATED",
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
                    if (!output.HasEmitted && retryCount < MaxRetries)
                    {
                        retryDelay = GetBackoffDelay(retryCount + 1);
                    }
                    else
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            "TIMEOUT",
                            "Provider request timed out")).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    if (!output.HasEmitted && retryCount < MaxRetries)
                    {
                        retryDelay = GetBackoffDelay(retryCount + 1);
                    }
                    else
                    {
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(
                            "TIMEOUT",
                            "Provider request timed out")).ConfigureAwait(false);
                    }
                }
                catch (HttpRequestException exception)
                {
                    if (!output.HasEmitted && retryCount < MaxRetries && IsRetryableException(exception))
                    {
                        retryDelay = GetBackoffDelay(retryCount + 1);
                    }
                    else
                    {
                        var mapped = output.HasEmitted
                            ? (Code: "STREAM_INTERRUPTED", Message: "Provider stream was interrupted")
                            : exception.StatusCode is { } statusCode
                                ? ProviderErrorMapper.Map(statusCode, responseBody: null)
                                : ProviderErrorMapper.MapFromException(exception);
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(mapped.Code, mapped.Message)).ConfigureAwait(false);
                    }
                }
                catch (IOException)
                {
                    if (!output.HasEmitted && retryCount < MaxRetries)
                    {
                        retryDelay = GetBackoffDelay(retryCount + 1);
                    }
                    else
                    {
                        var errorCode = output.HasEmitted ? "STREAM_INTERRUPTED" : "CONNECTION_ERROR";
                        var errorMessage = output.HasEmitted
                            ? "Provider stream was interrupted"
                            : "Failed to connect to provider";
                        await output.WriteAsync(ChatCompletionStreamEvent.Error(errorCode, errorMessage)).ConfigureAwait(false);
                    }
                }
                catch (JsonException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        "STREAM_PROTOCOL_ERROR",
                        "Provider stream violated the protocol")).ConfigureAwait(false);
                }
                catch (DecoderFallbackException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        "STREAM_PROTOCOL_ERROR",
                        "Provider stream violated the protocol")).ConfigureAwait(false);
                }
                catch (StreamProtocolException)
                {
                    await output.WriteAsync(ChatCompletionStreamEvent.Error(
                        "STREAM_PROTOCOL_ERROR",
                        "Provider stream violated the protocol")).ConfigureAwait(false);
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
                    retryCount++;
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
                var finishReason = ValidateFinishReason(choice.FinishReason);
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
            if (!state.Finished || state.FinishReason is null)
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
            Role = message.Role,
            Content = string.Equals(message.Role, "assistant", StringComparison.Ordinal) &&
                message.ToolCalls is { Count: > 0 }
                    ? null
                    : message.Content,
            Name = message.Name,
            ToolCallId = message.ToolCallId,
            ReasoningContent = string.Equals(message.Role, "assistant", StringComparison.Ordinal) &&
                BackfillAssistantReasoningContent
                    ? message.ReasoningContent ?? string.Empty
                    : message.ReasoningContent,
            ToolCalls = message.ToolCalls is { Count: > 0 }
                ? message.ToolCalls.Select(static toolCall => new OutboundToolCall
                {
                    Id = toolCall.Id,
                    Type = toolCall.Type,
                    Function = new OutboundFunction
                    {
                        Name = toolCall.FunctionName,
                        Arguments = toolCall.FunctionArguments,
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

        if (AuthHeader is not null)
            httpRequest.Headers.Authorization = AuthHeader;

        return httpRequest;
    }

    private bool TryValidateRequest(ChatCompletionRequest request, out string errorCode)
    {
        errorCode = "INVALID_REQUEST";
        var model = string.IsNullOrWhiteSpace(request.Model) ? DefaultModel : request.Model;
        if (string.IsNullOrWhiteSpace(model) || model.Length > 256 || model.Any(char.IsControl))
            return false;
        if (request.Messages is null || request.Messages.Count is < 1 or > MaxMessages)
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
            if (message is null || !IsValidMessageRole(message.Role) ||
                message.Name is { } name && !IsValidMessageName(name) ||
                message.ReasoningContent is not null &&
                    (!string.Equals(message.Role, "assistant", StringComparison.Ordinal) ||
                     Encoding.UTF8.GetByteCount(message.ReasoningContent) > MaxReasoningBytesPerChoice))
            {
                return false;
            }

            var isAssistant = string.Equals(message.Role, "assistant", StringComparison.Ordinal);
            var isTool = string.Equals(message.Role, "tool", StringComparison.Ordinal);
            var isFunction = string.Equals(message.Role, "function", StringComparison.Ordinal);
            if (!isAssistant && message.Content is null ||
                isAssistant && message.Content is null &&
                    message.ReasoningContent is null && message.ToolCalls is not { Count: > 0 } ||
                isAssistant && message.ToolCalls is { Count: > 0 } &&
                    !string.IsNullOrEmpty(message.Content) ||
                isTool != (message.ToolCallId is not null) ||
                isTool && !IsValidProtocolIdentifier(message.ToolCallId, MaxToolCallIdLength) ||
                isFunction && message.Name is null ||
                !isAssistant && message.ToolCalls is not null)
            {
                return false;
            }

            var contentBytes = message.Content is null ? 0 : Encoding.UTF8.GetByteCount(message.Content);
            if (contentBytes > MaxMessageBytes)
                return false;
            totalTextBytes += contentBytes + Encoding.UTF8.GetByteCount(message.Role);
            if (message.Name is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.Name);
            if (message.ToolCallId is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.ToolCallId);
            if (message.ReasoningContent is not null)
                totalTextBytes += Encoding.UTF8.GetByteCount(message.ReasoningContent);

            if (message.ToolCalls is { Count: > MaxTools })
                return false;
            foreach (var toolCall in message.ToolCalls ?? Array.Empty<ChatToolCall>())
            {
                if (toolCall is null || !IsValidProtocolIdentifier(toolCall.Id, MaxToolCallIdLength) ||
                    !string.Equals(toolCall.Type, "function", StringComparison.Ordinal) ||
                    !IsValidToolName(toolCall.FunctionName) || toolCall.FunctionArguments is null ||
                    !IsValidToolArguments(toolCall.FunctionArguments))
                {
                    return false;
                }

                var argumentBytes = Encoding.UTF8.GetByteCount(toolCall.FunctionArguments);
                if (argumentBytes > MaxToolArgumentsBytesPerCall)
                    return false;
                totalTextBytes += argumentBytes + Encoding.UTF8.GetByteCount(toolCall.Id) +
                    Encoding.UTF8.GetByteCount(toolCall.FunctionName);
            }
        }

        if (request.Tools is { Count: > MaxTools })
            return false;
        foreach (var tool in request.Tools ?? Array.Empty<ChatTool>())
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

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int retryNumber)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta >= TimeSpan.Zero)
            return delta > MaxRetryAfter ? MaxRetryAfter : delta;

        if (retryAfter?.Date is { } date)
        {
            var dateDelay = date - GetUtcNow();
            if (dateDelay >= TimeSpan.Zero)
                return dateDelay > MaxRetryAfter ? MaxRetryAfter : dateDelay;
        }

        return GetBackoffDelay(retryNumber);
    }

    private static TimeSpan GetBackoffDelay(int retryNumber)
    {
        var exponent = Math.Clamp(retryNumber - 1, 0, 5);
        return TimeSpan.FromMilliseconds(Math.Min(250 * (1 << exponent), 8000));
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
        var totalTokens = usage.PromptTokens + usage.CompletionTokens;
        if (usage.PromptTokens is < 0 or > MaxReportedTokens ||
            usage.CompletionTokens is < 0 or > MaxReportedTokens ||
            totalTokens > MaxReportedTokens ||
            usage.TotalTokens is { } reportedTotal && reportedTotal != totalTokens)
        {
            throw new StreamProtocolException();
        }

        return new TokenUsage
        {
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens,
        };
    }

    private static IReadOnlyList<ChatToolCall>? MapAndValidateToolCalls(
        List<OpenAiToolCall>? providerToolCalls)
    {
        if (providerToolCalls is null)
            return null;
        if (providerToolCalls.Count > MaxToolCallsPerChoice)
            throw new StreamProtocolException();
        if (providerToolCalls.Any(static toolCall => toolCall is null))
            throw new StreamProtocolException();

        var mapped = new List<ChatToolCall>(providerToolCalls.Count);
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

            mapped.Add(new ChatToolCall
            {
                Id = id!,
                Type = "function",
                FunctionName = name!,
                FunctionArguments = arguments,
            });
        }

        return mapped;
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

    private static string ValidateFinishReason(string? finishReason)
    {
        if (string.IsNullOrWhiteSpace(finishReason) || finishReason.Length > 64 ||
            finishReason.Any(char.IsControl))
        {
            throw new StreamProtocolException();
        }

        if (!IsValidUnicode(finishReason))
            throw new StreamProtocolException();

        return finishReason;
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
        public string? FinishReason;
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
