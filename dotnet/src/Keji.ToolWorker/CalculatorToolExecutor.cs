using System.Text.Json;

namespace Keji.ToolWorker;

public sealed class CalculatorToolExecutor : IKejiToolExecutor
{
    public string ToolName => "calculator";

    public object Execute(string inputJson)
    {
        using var doc = JsonDocument.Parse(inputJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("expr", out var exprProp))
            throw new ArgumentException("Missing required parameter: expr");

        var expr = exprProp.GetString();
        if (string.IsNullOrWhiteSpace(expr))
            throw new ArgumentException("expr must be a non-empty string");

        var result = SafeExpressionParser.Evaluate(expr);

        return new Dictionary<string, object>
        {
            ["result"] = decimal.ToDouble(result),
            ["expression"] = expr
        };
    }
}
