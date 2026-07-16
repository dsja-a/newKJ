using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Keji.Configuration.Models;
using System.Collections.Immutable;
using Keji.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Providers.Tests;

public sealed class ProviderHardeningTests
{
    [Fact]
    public void Config_FromTrustedDocumentBindsAllProviderSettings()
    {
        var document = ConfigurationDocument(
            provider: "openai",
            endpoint: "https://gateway.example.test/openai/v1",
            apiKey: "resolved-secret",
            model: "gpt-test",
            timeout: "17",
            retries: "4",
            maxTokens: "8192");

        var config = ModelProviderConfig.FromConfiguration(document);

        Assert.Equal("openai", config.ProviderType);
        Assert.Equal("https://gateway.example.test/openai/v1/", config.Endpoint);
        Assert.Equal("gpt-test", config.DefaultModel);
        Assert.Equal(TimeSpan.FromSeconds(17), config.Timeout);
        Assert.Equal(4, config.MaxRetries);
        Assert.Equal(8192, config.MaxTokens);
    }

    [Fact]
    public void Config_SecretIsExcludedFromSerializationAndDiagnostics()
    {
        const string secret = "secret-that-must-never-be-serialized";
        var config = ModelProviderConfig.Create("openai", secret, "https://api.example.test/v1", "model");

        var json = JsonSerializer.Serialize(config);
        var diagnostic = config.ToString();

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, diagnostic, StringComparison.Ordinal);
        Assert.Contains("HasSecret = True", diagnostic, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.example.test/v1")]
    [InlineData("https://user:password@api.example.test/v1")]
    [InlineData("https://api.example.test/v1?tenant=secret")]
    [InlineData("https://api.example.test/v1#fragment")]
    [InlineData("file:///tmp/provider")]
    public void Config_UntrustedEndpointFormsAreRejected(string endpoint)
    {
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", "key", endpoint, "model"));
    }

    [Fact]
    public void Config_ControlCharactersAndUnresolvedSecretsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", "key\r\nInjected: true", "https://api.example.test", "model"));
        Assert.Throws<ArgumentException>(() =>
            ModelProviderConfig.Create("openai", "${PROVIDER_KEY}", "https://api.example.test", "model"));
    }

    [Fact]
    public async Task OpenAiProvider_UsesConfiguredEndpointAuthorizationDefaultsAndLimits()
    {
        Uri? requestUri = null;
        AuthenticationHeaderValue? authorization = null;
        MediaTypeWithQualityHeaderValue? accept = null;
        string? body = null;
        var handler = new CapturingHandler(async (request, _) =>
        {
            requestUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            accept = request.Headers.Accept.Single();
            body = await request.Content!.ReadAsStringAsync();
            return SuccessfulResponse();
        });
        var config = ModelProviderConfig.Create(
                "openai", "provider-key", "https://gateway.example.test/custom/v1", "configured-model")
            .WithMaxTokens(3210);
        var provider = new OpenAIProvider(Factory(handler), config);

        var result = await provider.CompleteAsync(Request(model: string.Empty));

        Assert.True(result.Success);
        Assert.Equal("https://gateway.example.test/custom/v1/chat/completions", requestUri!.AbsoluteUri);
        Assert.Equal("Bearer", authorization!.Scheme);
        Assert.Equal("provider-key", authorization.Parameter);
        Assert.Equal("application/json", accept!.MediaType);
        using var payload = JsonDocument.Parse(body!);
        Assert.Equal("configured-model", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal(3210, payload.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.False(payload.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task OllamaProvider_NormalizesRootToOpenAiCompatibleV1AndSendsNoBearer()
    {
        Uri? requestUri = null;
        AuthenticationHeaderValue? authorization = new("sentinel");
        var handler = new CapturingHandler((request, _) =>
        {
            requestUri = request.RequestUri;
            authorization = request.Headers.Authorization;
            return Task.FromResult(SuccessfulResponse());
        });
        var config = ModelProviderConfig.Create("ollama", null, "http://127.0.0.1:11434", "llama-test");
        var provider = new OllamaProvider(Factory(handler), config);

        var result = await provider.CompleteAsync(Request(model: string.Empty));

        Assert.True(result.Success);
        Assert.Equal("http://127.0.0.1:11434/v1/chat/completions", requestUri!.AbsoluteUri);
        Assert.Null(authorization);
    }

    [Fact]
    public void ProviderConstructorsRejectMismatchedConfigurationTypes()
    {
        var factory = Factory(new CapturingHandler((_, _) => Task.FromResult(SuccessfulResponse())));
        var openAi = ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model");
        var ollama = ModelProviderConfig.Create("ollama", null, "http://localhost:11434", "model");

        Assert.Throws<ArgumentException>(() => new DeepSeekProvider(factory, openAi));
        Assert.Throws<ArgumentException>(() => new OpenAIProvider(factory, ollama));
        Assert.Throws<ArgumentException>(() => new OllamaProvider(factory, openAi));
    }

    [Fact]
    public async Task InvalidRequestIsRejectedBeforeAnyNetworkActivity()
    {
        var handler = new CapturingHandler((_, _) => Task.FromResult(SuccessfulResponse()));
        var provider = new OpenAIProvider(
            Factory(handler),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));
        var request = new ChatCompletionRequest
        {
            Model = "model\r\nInjected",
            Messages = new[] { new ChatMessage { Role = KejiChatRole.User, Content = "hello" } }.ToImmutableArray()
        };

        var response = await provider.CompleteAsync(request);
        var streamEvents = await CollectAsync(provider.StreamAsync(request));

        Assert.False(response.Success);
        Assert.Equal(KejiProviderErrorCode.InvalidRequest, response.ErrorCode);
        Assert.Equal(KejiProviderErrorCode.InvalidRequest, Assert.Single(streamEvents).ErrorCode);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void RegistrySnapshotsInputAndReturnsStableView()
    {
        var openAi = new StubProvider("openai");
        var source = new List<KeyValuePair<string, IModelProvider>>
        {
            new("openai", openAi),
            new("deepseek", new StubProvider("deepseek"))
        };
        var registry = new ModelProviderRegistry(source);
        source.Clear();

        Assert.Same(openAi, registry.GetProvider("openai"));
        Assert.Equal(new[] { "deepseek", "openai" }, registry.RegisteredProviders);
        Assert.Equal(2, registry.Providers.Count);
    }

    [Fact]
    public void RegistryRejectsDuplicateMismatchedInvalidAndNullEntries()
    {
        Assert.Throws<ArgumentException>(() => new ModelProviderRegistry(new[]
        {
            Pair("openai", new StubProvider("openai")),
            Pair("OPENAI", new StubProvider("openai"))
        }));
        Assert.Throws<ArgumentException>(() => new ModelProviderRegistry(new[]
        {
            Pair("openai", new StubProvider("deepseek"))
        }));
        Assert.Throws<ArgumentException>(() => new ModelProviderRegistry(new[]
        {
            Pair("bad\r\nname", new StubProvider("bad\r\nname"))
        }));
        Assert.Throws<ArgumentNullException>(() => new ModelProviderRegistry(new[]
        {
            Pair("openai", null!)
        }));
    }

    [Fact]
    public void DependencyInjectionBuilderFreezesAfterConfigurationAndRejectsDuplicates()
    {
        var openAi = ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model");
        var deepSeek = ModelProviderConfig.Create("deepseek", "key", "https://api.example.test", "model");
        IModelProviderRegistryBuilder? captured = null;
        var services = new ServiceCollection();

        services.AddKejiProviders(builder =>
        {
            captured = builder;
            builder.AddOpenAI(openAi);
            Assert.Throws<InvalidOperationException>(() => builder.AddOpenAI(openAi));
            Assert.Throws<ArgumentException>(() => builder.AddDeepSeek(openAi));
        });

        Assert.Throws<InvalidOperationException>(() => captured!.AddDeepSeek(deepSeek));
        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<IModelProviderRegistry>();
        Assert.Equal(new[] { "openai" }, registry.RegisteredProviders);
    }

    [Theory]
    [InlineData("tool_calls", KejiFinishReason.ToolCalls, true)]
    [InlineData("stop", KejiFinishReason.Stop, true)]
    [InlineData("content_filter", KejiFinishReason.ContentFilter, false)]
    [InlineData("refusal", KejiFinishReason.ContentFilter, false)]
    [InlineData("error", KejiFinishReason.Error, false)]
    [InlineData("STOP", KejiFinishReason.Stop, true)]
    [InlineData(" TOOL_CALLS ", KejiFinishReason.ToolCalls, true)]
    public async Task NonStreamingFinishReasonIsPreservedAndToolAuthorizationIsExact(
        string finishReason,
        KejiFinishReason expectedReason,
        bool shouldExecuteTools)
    {
        var json = JsonSerializer.Serialize(new
        {
            model = "provider-model",
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new
                    {
                        role = "assistant",
                        content = (string?)null,
                        tool_calls = new[]
                        {
                            new
                            {
                                id = "call_1",
                                type = "function",
                                function = new { name = "lookup", arguments = "{}" }
                            }
                        }
                    },
                    finish_reason = finishReason
                }
            }
        });
        var provider = new OpenAIProvider(
            Factory(new CapturingHandler((_, _) => Task.FromResult(JsonResponse(json)))),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));

        var response = await provider.CompleteAsync(Request());

        Assert.True(response.Success);
        Assert.Equal(expectedReason, response.FinishReason);
        Assert.True(response.HasToolCalls);
        Assert.Equal(shouldExecuteTools, response.ShouldExecuteTools);
    }

    [Fact]
    public void FailedResponseUsesErrorFinishReasonAndNeverAuthorizesTools()
    {
        var response = ChatCompletionResponse.Failed(KejiProviderErrorCode.AuthFailed, "Authentication failed");

        Assert.False(response.Success);
        Assert.Equal(KejiFinishReason.Error, response.FinishReason);
        Assert.False(response.HasToolCalls);
        Assert.False(response.ShouldExecuteTools);
    }

    [Fact]
    public async Task RequestBodyRoundTripsAssistantToolCallAndToolResultFields()
    {
        string? requestBody = null;
        var handler = new CapturingHandler(async (request, _) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return SuccessfulResponse();
        });
        var provider = new OpenAIProvider(
            Factory(handler),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));
        var request = new ChatCompletionRequest
        {
            Model = "model",
            Messages = new ChatMessage[]
            {
                new() { Role = KejiChatRole.User, Content = "look it up" },
                new()
                {
                    Role = KejiChatRole.Assistant,
                    Content = null,
                    ReasoningContent = "need a tool",
                    ToolCalls = new[]
                    {
                        new ChatToolCall
                        {
                            Id = "call_1",
                            Type = "function",
                            FunctionName = "lookup",
                            FunctionArguments = JsonDocument.Parse("{\"id\":7}").RootElement.Clone()
                        }
                    }.ToImmutableArray()
                },
                new() { Role = KejiChatRole.Tool, Content = "{\"value\":42}", ToolCallId = "call_1" },
                new() { Role = KejiChatRole.Function, Content = "legacy result", Name = "legacy_lookup" }
            }.ToImmutableArray()
        };

        var response = await provider.CompleteAsync(request);

        Assert.True(response.Success);
        using var payload = JsonDocument.Parse(requestBody!);
        var messages = payload.RootElement.GetProperty("messages");
        Assert.Equal(JsonValueKind.Null, messages[1].GetProperty("content").ValueKind);
        Assert.Equal("need a tool", messages[1].GetProperty("reasoning_content").GetString());
        var outboundCall = messages[1].GetProperty("tool_calls")[0];
        Assert.Equal("call_1", outboundCall.GetProperty("id").GetString());
        Assert.Equal("lookup", outboundCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"id\":7}", outboundCall.GetProperty("function").GetProperty("arguments").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString());
        Assert.Equal("call_1", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Equal("{\"value\":42}", messages[2].GetProperty("content").GetString());
        Assert.Equal("legacy_lookup", messages[3].GetProperty("name").GetString());
    }

    [Fact]
    public async Task DeepSeekBackfillsMissingAssistantReasoningButOpenAiOmitsIt()
    {
        string? openAiBody = null;
        string? deepSeekBody = null;
        var openAi = new OpenAIProvider(
            Factory(new CapturingHandler(async (request, _) =>
            {
                openAiBody = await request.Content!.ReadAsStringAsync();
                return SuccessfulResponse();
            })),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));
        var deepSeek = new DeepSeekProvider(
            Factory(new CapturingHandler(async (request, _) =>
            {
                deepSeekBody = await request.Content!.ReadAsStringAsync();
                return SuccessfulResponse();
            })),
            ModelProviderConfig.Create("deepseek", "key", "https://api.example.test", "model"));
        var request = new ChatCompletionRequest
        {
            Model = "model",
            Messages = new[] { new ChatMessage { Role = KejiChatRole.Assistant, Content = "answer" } }.ToImmutableArray()
        };

        Assert.True((await openAi.CompleteAsync(request)).Success);
        Assert.True((await deepSeek.CompleteAsync(request)).Success);

        using var openAiJson = JsonDocument.Parse(openAiBody!);
        using var deepSeekJson = JsonDocument.Parse(deepSeekBody!);
        Assert.False(openAiJson.RootElement.GetProperty("messages")[0].TryGetProperty("reasoning_content", out _));
        Assert.Equal(string.Empty,
            deepSeekJson.RootElement.GetProperty("messages")[0].GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public async Task InvalidRoleFieldCombinationsFailBeforeNetworkForBothModes()
    {
        var handler = new CapturingHandler((_, _) => Task.FromResult(SuccessfulResponse()));
        var provider = new OpenAIProvider(
            Factory(handler),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));
        var toolCall = new ChatToolCall
        {
            Id = "call_1",
            Type = "function",
            FunctionName = "lookup",
            FunctionArguments = JsonDocument.Parse("{}").RootElement.Clone()
        };
        var invalidMessages = new ChatMessage[]
        {
            new() { Role = KejiChatRole.Tool, Content = "result" },
            new() { Role = KejiChatRole.User, Content = "hello", ToolCallId = "call_1" },
            new() { Role = KejiChatRole.Assistant, Content = "must be null", ToolCalls = new[] { toolCall }.ToImmutableArray() },
            new() { Role = KejiChatRole.User, Content = "hello", ToolCalls = new[] { toolCall }.ToImmutableArray() },
            new() { Role = KejiChatRole.Function, Content = "result" },
            new() { Role = KejiChatRole.User, Content = "hello", ReasoningContent = "not allowed" },
            new() { Role = KejiChatRole.Assistant, Content = null }
        };

        foreach (var message in invalidMessages)
        {
            var request = new ChatCompletionRequest { Model = "model", Messages = new[] { message }.ToImmutableArray() };
            var response = await provider.CompleteAsync(request);
            var streamEvents = await CollectAsync(provider.StreamAsync(request));
            Assert.False(response.Success);
            Assert.Equal(KejiProviderErrorCode.InvalidRequest, response.ErrorCode);
            Assert.Equal(KejiProviderErrorCode.InvalidRequest, Assert.Single(streamEvents).ErrorCode);
        }

        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("{\"model\":\"test\",\"choices\":[null]}")]
    [InlineData("{\"model\":\"test\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[null]},\"finish_reason\":\"tool_calls\"}]}")]
    public async Task NonStreamingNullCollectionElementsFailClosed(string json)
    {
        var provider = new OpenAIProvider(
            Factory(new CapturingHandler((_, _) => Task.FromResult(JsonResponse(json)))),
            ModelProviderConfig.Create("openai", "key", "https://api.example.test", "model"));

        var response = await provider.CompleteAsync(Request());

        Assert.False(response.Success);
        Assert.Equal(KejiProviderErrorCode.InvalidResponse, response.ErrorCode);
        Assert.Equal(KejiFinishReason.Error, response.FinishReason);
    }

    [Fact]
    public async Task CompleteAsync_BodyTimeoutDisposesStreamThatIgnoresReadCancellation()
    {
        var streams = new List<DisposeOnlyUnblocksStream>();
        var handler = new CapturingHandler((_, _) =>
        {
            var s = new DisposeOnlyUnblocksStream();
            streams.Add(s);
            return Task.FromResult(StreamJsonResponse(s));
        });
        var config = ModelProviderConfig.Create(
                "openai", "key", "https://api.example.test", "model")
            .WithTimeout(TimeSpan.FromMilliseconds(50))
            .WithMaxRetries(0);
        var provider = new OpenAIProvider(Factory(handler), config);
        var completionTask = provider.CompleteAsync(Request());

        try
        {
            var firstStream = streams[0];

            Assert.Same(firstStream.ReadStarted.Task,
                await Task.WhenAny(firstStream.ReadStarted.Task, Task.Delay(TimeSpan.FromSeconds(2))));
            Assert.Same(completionTask,
                await Task.WhenAny(completionTask, Task.Delay(TimeSpan.FromSeconds(5))));

            var response = await completionTask;
            Assert.False(response.Success);
            Assert.Equal(KejiProviderErrorCode.Timeout, response.ErrorCode);
            Assert.Equal(KejiFinishReason.Error, response.FinishReason);
            Assert.True(firstStream.Disposed);
            Assert.Equal(3, handler.CallCount);
        }
        finally
        {
            foreach (var s in streams) s.ReleaseForCleanup();
        }
    }

    [Fact]
    public async Task CompleteAsync_UserCancellationDisposesStubbornBodyAndReturnsCancelled()
    {
        var stream = new DisposeOnlyUnblocksStream();
        var handler = new CapturingHandler((_, _) =>
            Task.FromResult(StreamJsonResponse(stream)));
        var config = ModelProviderConfig.Create(
                "openai", "key", "https://api.example.test", "model")
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithMaxRetries(0);
        var provider = new OpenAIProvider(Factory(handler), config);
        using var cts = new CancellationTokenSource();
        var completionTask = provider.CompleteAsync(Request(), cts.Token);

        try
        {
            Assert.Same(stream.ReadStarted.Task,
                await Task.WhenAny(stream.ReadStarted.Task, Task.Delay(TimeSpan.FromSeconds(2))));
            cts.Cancel();
            Assert.Same(completionTask,
                await Task.WhenAny(completionTask, Task.Delay(TimeSpan.FromSeconds(2))));

            var response = await completionTask;
            Assert.False(response.Success);
            Assert.Equal(KejiProviderErrorCode.Cancelled, response.ErrorCode);
            Assert.Equal(KejiFinishReason.Error, response.FinishReason);
            Assert.True(stream.Disposed);
            Assert.Equal(1, handler.CallCount);
        }
        finally
        {
            stream.ReleaseForCleanup();
        }
    }

    private static KejiConfigurationDocument ConfigurationDocument(
        string provider,
        string endpoint,
        string apiKey,
        string model,
        string timeout,
        string retries,
        string maxTokens)
    {
        var providerMap = Map(
            ("base_url", Scalar(endpoint)),
            ("api_key", Scalar(apiKey)),
            ("model", Scalar(model)),
            ("timeout", Scalar(timeout)),
            ("max_retries", Scalar(retries)),
            ("max_tokens", Scalar(maxTokens)));
        return new KejiConfigurationDocument(Map(
            ("models", Map(("default", Scalar(provider)), (provider, providerMap)))));
    }

    private static ConfigMap Map(params (string Key, ConfigNode Value)[] entries) =>
        new(entries.Select(entry => new KeyValuePair<string, ConfigNode>(entry.Key, entry.Value)));

    private static ConfigScalar Scalar(string value) => new(value);

    private static KeyValuePair<string, IModelProvider> Pair(string name, IModelProvider provider) => new(name, provider);

    private static ChatCompletionRequest Request(string model = "model") => new()
    {
        Model = model,
        Messages = new[] { new ChatMessage { Role = KejiChatRole.User, Content = "hello" } }.ToImmutableArray()
    };

    private static HttpResponseMessage SuccessfulResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            "{\"model\":\"test\",\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
            Encoding.UTF8,
            "application/json")
    };

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage StreamJsonResponse(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static IHttpClientFactory Factory(HttpMessageHandler handler) =>
        new LocalHttpClientFactory(new HttpClient(handler));

    private static async Task<List<ChatCompletionStreamEvent>> CollectAsync(
        IAsyncEnumerable<ChatCompletionStreamEvent> source)
    {
        var result = new List<ChatCompletionStreamEvent>();
        await foreach (var item in source)
            result.Add(item);
        return result;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _send;
        public CapturingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) => _send = send;
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return _send(request, cancellationToken);
        }
    }

    private sealed class LocalHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public LocalHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubProvider : IModelProvider
    {
        public StubProvider(string providerName) => ProviderName = providerName;
        public string ProviderName { get; }
        public Task<ChatCompletionResponse> CompleteAsync(ChatCompletionRequest request, CancellationToken ct = default) =>
            Task.FromResult(ChatCompletionResponse.Succeeded("ok"));

        public async IAsyncEnumerable<ChatCompletionStreamEvent> StreamAsync(
            ChatCompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return ChatCompletionStreamEvent.Done();
        }
    }

    private sealed class DisposeOnlyUnblocksStream : Stream
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Disposed { get; private set; }
        public void ReleaseForCleanup() => _released.TrySetResult();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            ReadStarted.TrySetResult();
            await _released.Task.ConfigureAwait(false);
            return 0;
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            _released.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
