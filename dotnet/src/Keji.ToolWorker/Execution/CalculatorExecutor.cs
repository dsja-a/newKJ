using System.Data;

namespace Keji.ToolWorker.Execution;

public static class CalculatorExecutor
{
    public static object Execute(string expression)
    {
        var result = new DataTable().Compute(expression, null);
        return Convert.ToDouble(result);
    }
}
