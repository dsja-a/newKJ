using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keji.Providers;

public abstract class ProviderBase : IModelProvider
{
    public abstract string ProviderName { get; }
    protected abstract string BaseUri { get; }
    protected abstract AuthenticationHeaderValue? AuthHeader { get; }

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    protected ProviderBase(IHttpClientFactory httpClientFactory, TimeSpan timeout, int maxRetries)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _httpClient = httpClientFactory.CreateClient("KejiProvider");
        _httpClient.Timeout = timeout;
        MaxRetries = maxRetries;
    }

    protected int MaxRetries { get; }

    public async Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default)
    {
        var retryCount = 0;
        while (true)
        {
            try
            {
                using var httpRequest = BuildRequest(request, stream: false);
                using var response = await _httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    if (retryCount < MaxRetries && (int)response.StatusCode >= 500)
                    {
                        retryCount++;
                        await Task.Delay(TimeSpan.FromMilliseconds(500 * retryCount), ct).ConfigureAwait(false);
                        continue;
                    }

                    var body = await SafeReadBodyAsync(response).ConfigureAwait(false);
                    var mapped = ProviderErrorMapper.Map(response.StatusCode, body);
                    return ChatCompletionResponse.Failed(mapped.Code, mapped.Message);
                }

                var json = await response.Content.ReadFromJsonAsync<OpenAiChatCompletionResponse>(JsonOptions, ct).ConfigureAwait(false);
                if (json is null)
                    return ChatCompletionResponse.Failed("EMPTY_RESPONSE", "Provider returned empty response");

                var choice = json.Choices?.FirstOrDefault();
                if (choice?.Message is null)
                    return ChatCompletionResponse.Failed("EMPTY_CHOICE", "Provider returned no choices");

                var content = choice.Message.Content ?? "";
                var reasoning = choice.Message.ReasoningContent;
                var toolCalls = choice.Message.ToolCalls?
                    .Select(tc => new ChatToolCall
                    {
                        Id = tc.Id ?? "",
                        Type = tc.Type ?? "function",
                        FunctionName = tc.Function?.Name ?? "",
                        FunctionArguments = tc.Function?.Arguments ?? ""
                    }).ToList();

                return ChatCompletionResponse.Succeeded(
                    content,
                    MapUsage(json.Usage),
                    reasoningContent: reasoning,
                    toolCalls: toolCalls,
                    model: json.Model ?? "");
            }
            catch (OperationCanceledException)
            {
                return ChatCompletionResponse.Failed("CANCELLED", "Request was cancelled");
            }
            catch (HttpRequestException ex)
            {
                if (retryCount < MaxRetries)
                {
                    retryCount++;
                    await Task.Delay(TimeSpan.FromMilliseconds(500 * retryCount), ct).ConfigureAwait(false);
                    continue;
                }

                var mapped = ProviderErrorMapper.MapFromException(ex);
                return ChatCompletionResponse.Failed(mapped.Code, mapped.Message);
            }
        }
    }

    public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(
        ChatCompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var events = await StreamAsyncBuffer(request, ct).ConfigureAwait(false);
        foreach (var evt in events)
            yield return evt;
    }

    private async Task<List<ChatCompletionStreamEvent>> StreamAsyncBuffer(
        ChatCompletionRequest request, CancellationToken ct)
    {
        HttpRequestMessage? httpRequest = null;
        HttpResponseMessage? httpResponse = null;
        Stream? responseStream = null;
        StreamReader? reader = null;

        try
        {
            httpRequest = BuildRequest(request, stream: true);
            httpResponse = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            if (!httpResponse.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(httpResponse).ConfigureAwait(false);
                var mapped = ProviderErrorMapper.Map(httpResponse.StatusCode, body);
                return new List<ChatCompletionStreamEvent>
                {
                    ChatCompletionStreamEvent.Error(mapped.Code, mapped.Message)
                };
            }

            responseStream = await httpResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            reader = new StreamReader(responseStream);

            var results = new List<ChatCompletionStreamEvent>();
            var choiceState = new Dictionary<int, ChoiceStreamState>();
            string? line;
            bool streamCompleted = false;

            while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null && !streamCompleted)
            {
                ct.ThrowIfCancellationRequested();

                if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;

                var data = line.AsSpan(6);
                if (data is "[DONE]")
                {
                    if (!streamCompleted)
                    {
                        results.Add(ChatCompletionStreamEvent.Done());
                        streamCompleted = true;
                    }
                    return results;
                }

                StreamedChunk? chunk;
                try
                {
                    chunk = JsonSerializer.Deserialize<StreamedChunk>(data, JsonOptions);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (chunk is null) continue;

                if (chunk.Usage is not null)
                {
                    var mapped = MapUsage(chunk.Usage);
                    if (mapped is not null)
                        results.Add(ChatCompletionStreamEvent.UsageEvent(mapped));
                }

                if (chunk.Choices is null || chunk.Choices.Count == 0) continue;

                foreach (var choice in chunk.Choices.OrderBy(c => c.Index))
                {
                    if (!choiceState.TryGetValue(choice.Index, out var state))
                    {
                        state = new ChoiceStreamState();
                        choiceState[choice.Index] = state;
                    }

                    if (choice.FinishReason is not null)
                    {
                        if (state.CurrentToolCallId is not null)
                        {
                            results.Add(ChatCompletionStreamEvent.ToolCallEnd());
                            state.CurrentToolCallId = null;
                            state.CurrentToolName = null;
                        }
                        state.Finished = true;
                    }

                    var delta = choice.Delta;
                    if (delta is null) continue;

                    if (!string.IsNullOrEmpty(delta.ReasoningContent))
                    {
                        results.Add(ChatCompletionStreamEvent.ReasoningToken(delta.ReasoningContent));
                    }

                    if (!string.IsNullOrEmpty(delta.Content))
                    {
                        results.Add(ChatCompletionStreamEvent.Token(delta.Content));
                    }

                    if (delta.ToolCalls is { Count: > 0 })
                    {
                        foreach (var tc in delta.ToolCalls)
                        {
                            if (!string.IsNullOrEmpty(tc.Id))
                            {
                                if (state.CurrentToolCallId is not null)
                                {
                                    results.Add(ChatCompletionStreamEvent.ToolCallEnd());
                                }
                                state.CurrentToolCallId = tc.Id;
                                state.CurrentToolName = tc.Function?.Name ?? "";
                                results.Add(ChatCompletionStreamEvent.ToolCallBegin(tc.Id, state.CurrentToolName));
                            }

                            if (!string.IsNullOrEmpty(tc.Function?.Arguments))
                            {
                                results.Add(ChatCompletionStreamEvent.ToolCallDelta(tc.Function.Arguments));
                            }
                        }
                    }
                }

                if (choiceState.Values.All(s => s.Finished))
                {
                    if (!streamCompleted)
                    {
                        results.Add(ChatCompletionStreamEvent.Done());
                        streamCompleted = true;
                    }
                    return results;
                }
            }

            if (!streamCompleted)
            {
                results.Add(ChatCompletionStreamEvent.Done());
            }
            return results;
        }
        catch (OperationCanceledException)
        {
            return new List<ChatCompletionStreamEvent>
            {
                ChatCompletionStreamEvent.Error("CANCELLED", "Request was cancelled")
            };
        }
        catch (HttpRequestException)
        {
            return new List<ChatCompletionStreamEvent>
            {
                ChatCompletionStreamEvent.Error("CONNECTION_ERROR", "Failed to connect to provider")
            };
        }
        finally
        {
            reader?.Dispose();
            responseStream?.Dispose();
            httpResponse?.Dispose();
            httpRequest?.Dispose();
        }
    }

    private HttpRequestMessage BuildRequest(ChatCompletionRequest request, bool stream)
    {
        var messages = request.Messages.Select(m => new
        {
            role = m.Role,
            content = m.Content,
            tool_calls = m.ToolCalls?.Select(tc => new
            {
                id = tc.Id,
                type = tc.Type,
                function = new { name = tc.FunctionName, arguments = tc.FunctionArguments }
            }).ToList()
        }).ToList();

        object? body = new
        {
            model = request.Model,
            messages,
            stream,
            tools = request.HasTools ? request.Tools!.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = t.InputSchemaJson is not null
                        ? JsonSerializer.Deserialize<JsonElement?>(t.InputSchemaJson)
                        : null
                }
            }).ToList() : null,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(BaseUri), "chat/completions"))
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        if (AuthHeader is not null)
            httpRequest.Headers.Authorization = AuthHeader;

        return httpRequest;
    }

    private static TokenUsage? MapUsage(OpenAiUsage? usage)
    {
        if (usage is null) return null;
        return new TokenUsage
        {
            PromptTokens = usage.PromptTokens,
            CompletionTokens = usage.CompletionTokens
        };
    }

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
        catch
        {
            return "";
        }
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
        public int PromptTokens { get; init; }
        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; init; }
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

    private sealed class ChoiceStreamState
    {
        public string? CurrentToolCallId { get; set; }
        public string? CurrentToolName { get; set; }
        public bool Finished { get; set; }
    }

    private sealed class StreamedDelta
    {
        public string? Role { get; init; }
        public string? Content { get; init; }
        public string? ReasoningContent { get; init; }
        public List<OpenAiToolCall>? ToolCalls { get; init; }
    }
}
