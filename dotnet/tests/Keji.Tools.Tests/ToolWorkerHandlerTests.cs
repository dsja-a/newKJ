using Keji.Tools.Definitions;
using Keji.Tools.Execution;
using Keji.ToolWorker.Worker;

namespace Keji.Tools.Tests;

public class ToolWorkerHandlerTests
{
    [Fact]
    public void Handle_NullToolName_ReturnsError()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test1",
            Tool = null,
            Args = null,
            ContractVersion = 1
        };

        var response = handler.Handle(request);

        Assert.False(response.Success);
        Assert.Equal("INVALID_REQUEST", response.ErrorCode);
    }

    [Fact]
    public void Handle_CalculatorValidExpression_ReturnsResult()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test2",
            Tool = "calculator",
            Args = new Dictionary<string, object?> { ["expr"] = "2+2" },
            ContractVersion = 1
        };

        var response = handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
    }

    [Fact]
    public void Handle_CalculatorMissingExpr_ReturnsError()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test3",
            Tool = "calculator",
            Args = new Dictionary<string, object?>(),
            ContractVersion = 1
        };

        var response = handler.Handle(request);

        Assert.False(response.Success);
        Assert.Equal("VALIDATION_ERROR", response.ErrorCode);
    }

    [Fact]
    public void Handle_GetTime_ReturnsResult()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test4",
            Tool = "get_time",
            Args = new Dictionary<string, object?>(),
            ContractVersion = 1
        };

        var response = handler.Handle(request);

        Assert.True(response.Success);
        Assert.NotNull(response.Result);
        Assert.IsType<string>(response.Result);
    }

    [Fact]
    public void Handle_UnknownTool_ReturnsNotFound()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test5",
            Tool = "read_file",
            Args = new Dictionary<string, object?>(),
            ContractVersion = 1
        };

        var response = handler.Handle(request);

        Assert.False(response.Success);
        Assert.Equal("NOT_FOUND", response.ErrorCode);
    }

    [Fact]
    public void Handle_WrongContractVersion_ReturnsError()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var handler = new WorkerRequestHandler(registry);
        var request = new WorkerProtocolMessage
        {
            Protocol = "keji-toolworker-v1",
            RequestId = "test6",
            Tool = "calculator",
            Args = new Dictionary<string, object?> { ["expr"] = "2+2" },
            ContractVersion = 99
        };

        var response = handler.Handle(request);

        Assert.False(response.Success);
        Assert.Equal("VERSION_MISMATCH", response.ErrorCode);
    }

    [Fact]
    public void BuiltInToolWorkerRegistry_ContainsOnlyCalculatorAndGetTime()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        var all = registry.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, d => d.Name.Value == "calculator");
        Assert.Contains(all, d => d.Name.Value == "get_time");
    }

    [Fact]
    public void BuiltInToolWorkerRegistry_BothExecutable()
    {
        var registry = BuiltInToolWorkerRegistry.Instance;
        foreach (var def in registry.GetAll())
        {
            Assert.Equal(KejiToolAvailability.Executable, def.Availability);
            Assert.Equal(KejiToolExecutionTarget.ToolWorker, def.ExecutionTarget);
        }
    }
}
