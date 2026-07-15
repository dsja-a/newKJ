using Keji.Tools.Execution;

namespace Keji.Tools.Tests;

public class ToolExecutionPipelineTests
{
    private sealed class FakeCoordinator(ToolExecutionResult result) : IToolExecutionCoordinator
    {
        public string? LastToolName { get; private set; }
        public IReadOnlyDictionary<string, object?>? LastInputs { get; private set; }

        public Task<ToolExecutionResult> ExecuteAsync(
            string toolName,
            IReadOnlyDictionary<string, object?>? inputs,
            CancellationToken ct = default)
        {
            LastToolName = toolName;
            LastInputs = inputs;
            return Task.FromResult(result);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsToCoordinator()
    {
        var expectedResult = ToolExecutionResult.Successful("ok", TimeSpan.Zero);
        var coordinator = new FakeCoordinator(expectedResult);
        var pipeline = new ToolExecutionPipeline(coordinator);

        var inputs = new Dictionary<string, object?> { ["expr"] = "2+2" };
        var result = await pipeline.ExecuteAsync("calculator", inputs);

        Assert.True(result.Success);
        Assert.Equal("calculator", coordinator.LastToolName);
        Assert.Same(inputs, coordinator.LastInputs);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsFailureResult()
    {
        var expectedResult = ToolExecutionResult.Failed("error", "TEST_ERROR");
        var coordinator = new FakeCoordinator(expectedResult);
        var pipeline = new ToolExecutionPipeline(coordinator);

        var result = await pipeline.ExecuteAsync("test", null);

        Assert.False(result.Success);
        Assert.Equal("error", result.ErrorMessage);
        Assert.Equal("TEST_ERROR", result.ErrorCode);
    }

    [Fact]
    public void Constructor_RequiresCoordinator()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolExecutionPipeline(null!));
    }
}
