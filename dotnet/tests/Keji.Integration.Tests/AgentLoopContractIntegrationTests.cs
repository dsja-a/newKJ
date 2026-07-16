using Keji.Agent;
using Microsoft.Extensions.DependencyInjection;

namespace Keji.Integration.Tests;

public sealed class AgentLoopContractIntegrationTests
{
    [Theory]
    [InlineData("stream_interface")]
    [InlineData("stream_method")]
    [InlineData("request_type")]
    [InlineData("event_type")]
    [InlineData("stop_reason")]
    [InlineData("error_code")]
    [InlineData("usage")]
    [InlineData("transcript")]
    [InlineData("sse_adapter")]
    [InlineData("scoped_loop")]
    [InlineData("singleton_gate")]
    [InlineData("singleton_adapter")]
    public void AgentFoundationContractsAreIntegrated(string contract)
    {
        var services = new ServiceCollection();
        services.AddKejiAgentLoop();

        switch (contract)
        {
            case "stream_interface":
                Assert.True(typeof(IKejiAgentLoop).IsInterface);
                break;
            case "stream_method":
                var method = Assert.Single(typeof(IKejiAgentLoop).GetMethods());
                Assert.Equal(nameof(IKejiAgentLoop.RunStreamAsync), method.Name);
                Assert.Equal(typeof(IAsyncEnumerable<KejiAgentEvent>), method.ReturnType);
                break;
            case "request_type":
                Assert.True(typeof(KejiAgentRunRequest).IsClass);
                break;
            case "event_type":
                Assert.True(typeof(KejiAgentEvent).IsClass);
                break;
            case "stop_reason":
                Assert.True(typeof(KejiAgentStopReason).IsEnum);
                break;
            case "error_code":
                Assert.True(typeof(KejiAgentErrorCode).IsEnum);
                break;
            case "usage":
                Assert.True(typeof(KejiAgentUsage).IsClass);
                break;
            case "transcript":
                Assert.True(typeof(KejiAgentTranscript).IsClass);
                break;
            case "sse_adapter":
                Assert.NotNull(typeof(KejiAgentSseAdapter).GetMethod(nameof(KejiAgentSseAdapter.AdaptAsync)));
                break;
            case "scoped_loop":
                Assert.Equal(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(IKejiAgentLoop)).Lifetime);
                break;
            case "singleton_gate":
                Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(IKejiAgentSessionGate)).Lifetime);
                break;
            case "singleton_adapter":
                Assert.Equal(ServiceLifetime.Singleton, services.Single(x => x.ServiceType == typeof(KejiAgentSseAdapter)).Lifetime);
                break;
            default:
                throw new InvalidOperationException("Unknown contract case.");
        }
    }
}
