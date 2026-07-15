using Keji.Providers;

namespace Keji.Streaming;

public static class KejiSseAdapter
{
    public static async IAsyncEnumerable<KejiSseEvent> ToSseEvents(
        IAsyncEnumerable<ChatCompletionStreamEvent> providerEvents,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) yield break;

        var phase = KejiSsePhase.Thinking;

        await foreach (var evt in providerEvents.ConfigureAwait(false))
        {
            if (ct.IsCancellationRequested) yield break;

            switch (evt.Type)
            {
                case ChatCompletionStreamEventType.ReasoningToken:
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.ThinkToken,
                        Phase = KejiSsePhase.Thinking,
                        Delta = evt.Content
                    };
                    break;

                case ChatCompletionStreamEventType.Token:
                    if (phase != KejiSsePhase.Answering)
                    {
                        phase = KejiSsePhase.Answering;
                        yield return new KejiSseEvent
                        {
                            EventType = KejiSseEventType.Answering,
                            Phase = KejiSsePhase.Answering
                        };
                    }
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.Answer,
                        Phase = KejiSsePhase.Answering,
                        Delta = evt.Content
                    };
                    break;

                case ChatCompletionStreamEventType.ToolCallBegin:
                    if (phase != KejiSsePhase.Answering)
                    {
                        phase = KejiSsePhase.Answering;
                        yield return new KejiSseEvent
                        {
                            EventType = KejiSseEventType.Answering,
                            Phase = KejiSsePhase.Answering
                        };
                    }
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.ToolCall,
                        Phase = KejiSsePhase.Answering,
                        ToolName = evt.ToolName
                    };
                    break;

                case ChatCompletionStreamEventType.ToolCallDelta:
                    // Tool parameters are NOT exposed to SSE - name only
                    break;

                case ChatCompletionStreamEventType.ToolCallEnd:
                    phase = KejiSsePhase.Thinking;
                    break;

                case ChatCompletionStreamEventType.Usage:
                    phase = KejiSsePhase.Done;
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.Usage,
                        Phase = KejiSsePhase.Done,
                        Usage = evt.Usage
                    };
                    break;

                case ChatCompletionStreamEventType.Error:
                    phase = KejiSsePhase.Error;
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.Error,
                        Phase = KejiSsePhase.Error,
                        ErrorMessage = evt.ErrorMessage ?? "An error occurred"
                    };
                    break;

                case ChatCompletionStreamEventType.Done:
                    phase = KejiSsePhase.Done;
                    yield return new KejiSseEvent
                    {
                        EventType = KejiSseEventType.Done,
                        Phase = KejiSsePhase.Done
                    };
                    break;
            }
        }
    }
}
