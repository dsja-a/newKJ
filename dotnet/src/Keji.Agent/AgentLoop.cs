using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Keji.Persistence.Repositories;
using Keji.Providers;
using Keji.Security.Auth;
using Keji.Tools.Definitions;
using Keji.Tools.Definitions.Parameters;
using Keji.Tools.Execution;
using Keji.Tools.Names;
using Keji.Tools.Registry;

namespace Keji.Agent;

public sealed class AgentLoop : IAgentLoop
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

    public AgentLoop(
        ICurrentUserAccessor userAccessor,
        IConversationRepository conversations,
        IMessageRepository messages,
        IModelProviderRegistry providers,
        IKejiToolRegistry tools,
        IToolExecutionPipeline toolPipeline,
        AgentLoopOptions options)
    {
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _toolPipeline = toolPipeline ?? throw new ArgumentNullException(nameof(toolPipeline));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<AgentRunResult> RunAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await RunCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AgentRunResult.Failed(AgentRunStatus.ProviderFailed);
        }
    }

    private async Task<AgentRunResult> RunCoreAsync(
        AgentRunRequest request,
        CancellationToken cancellationToken)
    {

        if (!TryValidateRequest(request))
            return AgentRunResult.Failed(AgentRunStatus.InvalidRequest);

        var user = _userAccessor.CurrentUser;
        if (!IsValidUser(user))
            return AgentRunResult.Failed(AgentRunStatus.Unauthenticated);

        if (!await IsStillOwnedAsync(request.ConversationId, user!.Id, cancellationToken).ConfigureAwait(false))
            return AgentRunResult.Failed(AgentRunStatus.ConversationNotFound);

        var provider = _providers.GetProvider(request.ProviderName);
        if (provider is null)
            return AgentRunResult.Failed(AgentRunStatus.ProviderNotFound);

        var history = await _messages.ListOwnedMessagesAsync(
            request.ConversationId,
            user.Id,
            _options.MaxContextMessages,
            cancellationToken).ConfigureAwait(false);
        if (!TryBuildContext(history, request.UserMessage, out var context))
            return AgentRunResult.Failed(AgentRunStatus.InvalidRequest);

        if (!await IsStillOwnedAsync(request.ConversationId, user.Id, cancellationToken).ConfigureAwait(false))
            return AgentRunResult.Failed(AgentRunStatus.ConversationNotFound);
        await _messages.AddOwnedAsync(
            request.ConversationId,
            user.Id,
            "user",
            request.UserMessage,
            cancellationToken).ConfigureAwait(false);

        var advertisedTools = BuildAdvertisedTools();
        var toolCalls = 0;
        var totalToolResultBytes = 0;

        for (var iteration = 1; iteration <= _options.MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await IsStillOwnedAsync(request.ConversationId, user.Id, cancellationToken).ConfigureAwait(false))
                return AgentRunResult.Failed(AgentRunStatus.ConversationNotFound, iteration - 1, toolCalls);

            var response = await provider.CompleteAsync(new ChatCompletionRequest
            {
                Model = request.Model,
                Messages = context.ToImmutableArray(),
                Tools = advertisedTools,
                Temperature = request.Temperature,
                MaxTokens = request.MaxTokens,
            }, cancellationToken).ConfigureAwait(false);

            if (!response.Success)
                return AgentRunResult.Failed(AgentRunStatus.ProviderFailed, iteration, toolCalls);

            if (!response.HasToolCalls)
            {
                if (!TryValidateAssistantContent(response.Content, out var content))
                    return AgentRunResult.Failed(AgentRunStatus.ProviderFailed, iteration, toolCalls);
                if (!await IsStillOwnedAsync(request.ConversationId, user.Id, cancellationToken).ConfigureAwait(false))
                    return AgentRunResult.Failed(AgentRunStatus.ConversationNotFound, iteration, toolCalls);
                await _messages.AddOwnedAsync(
                    request.ConversationId,
                    user.Id,
                    "assistant",
                    content,
                    cancellationToken).ConfigureAwait(false);
                return AgentRunResult.Completed(content, iteration, toolCalls);
            }

            if (!response.ShouldExecuteTools || response.ToolCalls.Length > _options.MaxToolCalls - toolCalls)
                return AgentRunResult.Failed(AgentRunStatus.LimitExceeded, iteration, toolCalls);

            if (!TryValidateToolCalls(response.ToolCalls))
                return AgentRunResult.Failed(AgentRunStatus.ToolFailed, iteration, toolCalls);

            context.Add(new ChatMessage
            {
                Role = KejiChatRole.Assistant,
                Content = null,
                ToolCalls = response.ToolCalls,
            });

            foreach (var toolCall in response.ToolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await IsStillOwnedAsync(request.ConversationId, user.Id, cancellationToken).ConfigureAwait(false))
                    return AgentRunResult.Failed(AgentRunStatus.ConversationNotFound, iteration, toolCalls);
                if (!TryConvertArguments(toolCall.FunctionArguments, out var inputs))
                    return AgentRunResult.Failed(AgentRunStatus.ToolFailed, iteration, toolCalls);

                var result = await _toolPipeline.ExecuteAsync(
                    toolCall.FunctionName,
                    inputs,
                    cancellationToken).ConfigureAwait(false);
                toolCalls++;

                if (!TryFormatToolResult(result, out var toolContent, out var resultBytes) ||
                    resultBytes > _options.MaxTotalToolResultBytes - totalToolResultBytes)
                {
                    return AgentRunResult.Failed(AgentRunStatus.LimitExceeded, iteration, toolCalls);
                }
                totalToolResultBytes += resultBytes;
                context.Add(new ChatMessage
                {
                    Role = KejiChatRole.Tool,
                    ToolCallId = toolCall.Id,
                    Name = toolCall.FunctionName,
                    Content = toolContent,
                });
            }

            if (!TryEnforceContextBounds(context))
                return AgentRunResult.Failed(AgentRunStatus.LimitExceeded, iteration, toolCalls);
        }

        return AgentRunResult.Failed(AgentRunStatus.LimitExceeded, _options.MaxIterations, toolCalls);
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

    private static bool TryValidateRequest(AgentRunRequest request)
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

        foreach (var record in history.TakeLast(_options.MaxContextMessages - 1))
        {
            var role = record.Role switch
            {
                "user" => KejiChatRole.User,
                "assistant" => KejiChatRole.Assistant,
                _ => KejiChatRole.Invalid,
            };
            if (role == KejiChatRole.Invalid || record.Content is null)
                continue;
            var added = Encoding.UTF8.GetByteCount(record.Content);
            if (added > _options.MaxContextBytes - bytes)
                continue;
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
