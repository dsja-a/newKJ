# Model Providers and SSE Protocol Architecture

## Overview

The Model Providers layer (`Keji.Providers`) and the SSE Streaming layer (`Keji.Streaming`) form the bridge between the agent loop and external LLM APIs. `ProviderBase` handles HTTP communication with OpenAI-compatible chat completion endpoints (both non-streaming and streaming). `KejiSseAdapter` converts internal `ChatCompletionStreamEvent` events into the public SSE wire protocol.

## Architecture Diagram

```
Agent Loop
    │
    ├── IModelProvider.CompleteAsync(request)  → ChatCompletionResponse
    │       │
    │       └── ProviderBase
    │               ├── BuildRequest (POST /chat/completions, JSON body)
    │               ├── HttpClient.SendAsync
    │               ├── Deserialize → OpenAiChatCompletionResponse
    │               ├── Map to ChatCompletionResponse
    │               └── Error handling: ProviderErrorMapper (no raw leak)
    │
    └── IModelProvider.StreamAsync(request)  → IAsyncEnumerable<ChatCompletionStreamEvent>
            │
            └── ProviderBase
                    ├── BuildRequest (POST /chat/completions, stream: true)
                    ├── HttpClient.SendAsync (ResponseHeadersRead)
                    ├── SseLineReader (line-by-line, 64 KiB limit)
                    │       └── Strips "data: " prefix, returns JSON strings
                    ├── JsonSerializer.Deserialize<StreamedChunk>
                    ├── Per-choice state machine
                    │       └── ChoiceStreamState { ToolCallStates, counters, Finished }
                    ├── Channel<ChatCompletionStreamEvent> (producer/consumer)
                    ├── Retry loop (408/409/429/5xx, Retry-After)
                    └── Cancel/Timeout distinction
                            ├── OperationCanceledException when !ct.IsCancellationRequested → TIMEOUT
                            └── OperationCanceledException → CANCELLED

    KejiSseAdapter.ToSseEvents(providerEvents)
            │
            ├── IAsyncEnumerable<ChatCompletionStreamEvent> in
            ├── WithCancellation(ct)
            ├── Phase tracking (Thinking → Answering → Done/Error)
            ├── Sequence incrementing (per-event)
            ├── EventId generation ("evt_N")
            ├── TimestampUtc per event
            └── IAsyncEnumerable<KejiSseEvent> out

    KejiSseFormatter.FormatEvent(kejiSseEvent) → SSE wire string
            │
            ├── "event: <type>"
            ├── "id: <event-id>" (optional)
            └── "data: { protocol_version, sequence, timestamp_utc, phase, delta, ... }"
```

## ProviderBase Streaming Architecture

### True Streaming (Channel-based)

`StreamAsync` is an `async IAsyncEnumerable<ChatCompletionStreamEvent>`. Internally, it creates a `Channel<ChatCompletionStreamEvent>` (bounded, single-reader/single-writer) and starts a producer task (`RunStreamProducerAsync`). The consumer reads from the channel and yields events directly. This eliminates the old `StreamAsyncBuffer` pattern that accumulated all events before yielding.

```
StreamAsync(request)
  ├── Create Channel<ChatCompletionStreamEvent>
  ├── Task.Run → RunStreamProducerAsync(writer, request, ct)
  ├── foreach (evt in channel.Reader.ReadAllAsync(ct))
  │       yield return evt
  └── await producerTask (catch OperationCanceledException)
```

### SseLineReader

A disposable wrapper around `StreamReader` that:
- Calls `StreamReader.ReadLineAsync(ct)` with cancellation
- Strips `data: ` prefix from SSE lines
- Ignores non-data lines (comments, events, etc.)
- Enforces 64 KiB max line length
- Returns `[DONE]` verbatim for the stream termination signal
- Returns `null` on stream end

### Per-Choice State Machine

Each choice index maintains a `ChoiceStreamState`:

```csharp
class ChoiceStreamState {
    Dictionary<int, ToolCallState> ToolCallStates;  // keyed by toolCallIndex
    int TotalContentLength;
    int TotalReasoningLength;
    int TotalToolArgsLength;
    bool Finished;
}

class ToolCallState {
    string? CurrentToolCallId;
    string? CurrentToolName;
    StringBuilder ArgumentsBuilder;
    int TotalArgsLength;
    bool Completed;
}
```

Processing per chunk:
1. Parse `StreamedChunk` from JSON
2. Handle `[DONE]` → yield `Done` event, return
3. Handle `usage` → yield `UsageEvent`
4. For each choice:
   - Lookup/create `ChoiceStreamState` per choice index
   - If `finish_reason` present: emit `ToolCallEnd` for any active tool calls, mark choice `Finished`
   - Process delta:
     - `reasoning_content`: truncate at 4 MiB total, yield `ReasoningToken`
     - `content`: truncate at 4 MiB total, yield `Token`
     - `tool_calls`: per `(choiceIndex, toolCallIndex)`:
       - New tool call (has `id`): emit `ToolCallBegin`, reset args buffer
       - Tool call arguments (has `function.arguments`): truncate at 256 KiB per call, 1 MiB total, yield `ToolCallDelta`, accumulate in builder
   - After processing, if all choices are finished: yield `Done` and return

### Size Limits

| Limit | Value | Enforced at |
|---|---|---|
| Max SSE line length | 64 KiB | SseLineReader (throws InvalidOperationException) |
| Max content per choice | 4 MiB | ChoiceStreamState.TotalContentLength |
| Max reasoning per choice | 4 MiB | ChoiceStreamState.TotalReasoningLength |
| Max tool args per call | 256 KiB | ToolCallState.TotalArgsLength |
| Max total tool args per choice | 1 MiB | ChoiceStreamState.TotalToolArgsLength |
| Max tool calls per choice | 128 | ToolCallStates.Count check |

When a limit is reached, subsequent data for that dimension is silently dropped (yielded length is 0, which is skipped).

### Retry Logic

Retryable status codes: 408 (Request Timeout), 409 (Conflict), 429 (Too Many Requests), 5xx (Server Errors).

```csharp
bool ShouldRetryStream(HttpStatusCode statusCode) {
    var code = (int)statusCode;
    return code == 408 || code == 409 || code == 429 || code >= 500;
}

TimeSpan GetRetryDelay(HttpResponseMessage response, int retryCount) {
    // Prefer Retry-After header if present and valid (1ms–30s)
    if (response.Headers.RetryAfter?.Delta is TimeSpan delta && delta > TimeSpan.Zero && delta <= TimeSpan.FromSeconds(30))
        return delta;
    // Exponential backoff: 500ms, 1s, 2s, 4s, 8s (capped at 10s)
    return TimeSpan.FromMilliseconds(Math.Min(500 * (1 << (retryCount - 1)), 10000));
}
```

`MaxRetries` is configurable (default 2, max 5). `HttpRequestException` (connection failure) also triggers retry.

### Cancel vs Timeout

```csharp
catch (OperationCanceledException) when (!ct.IsCancellationRequested)
{
    // TaskCanceledException from HttpClient timeout — NOT user cancellation
    writer.TryWrite(ChatCompletionStreamEvent.Error("TIMEOUT", "..."));
}
catch (OperationCanceledException)
{
    // ct.IsCancellationRequested is true — user cancelled
    writer.TryComplete();  // silent stop, no error event
}
```

## ModelProviderRegistry

Frozen after construction via `ImmutableDictionary<string, IModelProvider>`. The constructor accepts either `Dictionary<string, IModelProvider>` (converted to immutable) or an `ImmutableDictionary<string, IModelProvider>` directly. Mutations to the original dictionary do not affect the registry.

## ModelProviderConfig

Security hardening:
- **HTTPS enforcement**: `Uri.Scheme` must be `"https"` unless the host is loopback (localhost, 127.0.0.1, ::1, or any loopback IP).
- **Loopback detection**: `Uri.IsLoopback` property plus manual IPv4/IPv6 loopback check.
- **Secret resolution**: `WithResolvedSecret(string)` creates a new instance with the resolved API key, enabling a secret resolution pipeline (e.g., environment variable → config).

## SSE Protocol v1

### Wire Format

```
event: think_token
id: evt_0
data: {"protocol_version":"1.0","sequence":0,"timestamp_utc":"2026-07-15T12:00:00.0000000Z","phase":"thinking","delta":"step 1"}

event: answering
id: evt_1
data: {"protocol_version":"1.0","sequence":1,"timestamp_utc":"...","phase":"answering"}

event: answer
id: evt_2
data: {"protocol_version":"1.0","sequence":2,"timestamp_utc":"...","phase":"answering","delta":"Hello"}

event: tool_call
id: evt_3
data: {"protocol_version":"1.0","sequence":3,"timestamp_utc":"...","phase":"answering","tool":"search"}

event: usage
id: evt_4
data: {"protocol_version":"1.0","sequence":4,"timestamp_utc":"...","phase":"done","usage":{"promptTokens":10,"completionTokens":5,"totalTokens":15}}

event: error
id: evt_5
data: {"protocol_version":"1.0","sequence":5,"timestamp_utc":"...","phase":"error","error":"Something failed"}

event: done
id: evt_6
data: {"protocol_version":"1.0","sequence":6,"timestamp_utc":"...","phase":"done"}
```

### Event Types

| Event Type | Wire Name | Phase | Delta | Tool | Usage | Error |
|---|---|---|---|---|---|---|
| Thinking | `thinking` | thinking | — | — | — | — |
| ThinkToken | `think_token` | thinking | reasoning text | — | — | — |
| Answering | `answering` | answering | — | — | — | — |
| Answer | `answer` | answering | text | — | — | — |
| ToolCall | `tool_call` | answering | — | tool name | — | — |
| Usage | `usage` | done | — | — | token counts | — |
| Error | `error` | error | — | — | — | error message |
| Done | `done` | done | — | — | — | — |

### Phase Transitions

```
Thinking ──(first token)──▶ Answering ──(usage/done)──▶ Done
                                      ──(error)────────▶ Error
Answering ──(tool call end)──▶ Thinking (for next reasoning)
```

The phase starts at `Thinking`. The first content/reasoning token after a reasoning phase stays in Thinking. When the first answer token or tool call begins, phase transitions to `Answering`. When a `ToolCallEnd` is received (without an intervening answer), the phase resets to `Thinking` (for models that emit reasoning after tool calls). Usage, Done, and Error set `Done` or `Error` phase permanently.

### Security

- Tool call deltas (arguments) are NOT exposed in SSE output — only the tool name is sent.
- Error messages use `ProviderErrorMapper` which maps HTTP codes to generic messages (no raw response body, no stack traces, no API keys leaked).
- `TokenUsage.TotalTokens` is calculated as `PromptTokens + CompletionTokens`.

## Configuration and Registration

```csharp
// Registration
services.AddSingleton<IModelProviderRegistry>(sp =>
{
    var dict = new Dictionary<string, IModelProvider>
    {
        ["openai"] = new OpenAIProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            ModelProviderConfig.Create("openai", resolvedKey, "https://api.openai.com", "gpt-4o")
                .WithTimeout(TimeSpan.FromSeconds(60))
                .WithMaxRetries(3)),
        ["ollama"] = new OllamaProvider(
            sp.GetRequiredService<IHttpClientFactory>(),
            ModelProviderConfig.Create("ollama", "", "http://localhost:11434", "llama3"))
    };
    return new ModelProviderRegistry(dict);  // frozen after construction
});
```

## Test Coverage

- **ProviderBaseNonStreamingTests** (30 tests): Basic success, reasoning, tool calls, error codes (401/403/404/429/503/500/502/504/400), retry (503→success, 400 no retry), timeout, cancellation, empty/null response, model name, retry on 408/409, HTTPS validation, loopback, frozen registry, secret resolver.
- **ProviderBaseStreamingTests** (26 tests): Token/ReasoningToken/Done/Error yielding, tool call begin/delta/end/accumulation, multiple choices, malformed line handling, non-data line ignoring, HTTP exception, cancellation, pre-cancellation, empty stream, size limit truncation (content, reasoning, tool calls), retry on 408, no duplicate Done, content after tool calls, multiple tool calls per index, tool call index on begin, max tool calls enforcement.
- **KejiSseFormatterTests** (26 tests): All event types, tool name only (no args), usage token counts, error message, Done format, phase field always present, protocol version, sequence, timestamp, event ID (present/absent), thinking phase start.
- **KejiSseAdapterTests** (23 tests): All mappings, phase transitions, multiple events, cancellation, empty stream, tool call without begin, sequence increments, EventId format, timestamp freshness, protocol version constant, monotonic sequence, reasoning→token sequence.
- **KejiSseSecurityTests** (8 tests): No raw exception leak, tool call parameters stripped, usage no content leak, error formatter no stack/key leak, answer formatter no key leak, error message pass-through.

Total: 165 Provider tests + 140 Streaming tests = 305 tests for this architecture.
