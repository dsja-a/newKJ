namespace Keji.ToolWorker.Tests;

public class CalculatorExpressionParserTests
{
    [Theory]
    [InlineData("2+2", 4)]
    [InlineData("10-3", 7)]
    [InlineData("4*5", 20)]
    [InlineData("20/4", 5)]
    [InlineData("10%3", 1)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("2*(3+4)", 14)]
    [InlineData(" -5 ", -5)]
    [InlineData("+5", 5)]
    [InlineData("1.5+2.5", 4.0)]
    [InlineData("0.1+0.2", 0.3)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("100/4", 25)]
    [InlineData("1+2+3+4+5", 15)]
    [InlineData("10-5-2", 3)]
    public void Evaluate_ValidExpressions_ReturnsExpected(string expr, decimal expected)
    {
        var result = SafeExpressionParser.Evaluate(expr);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Evaluate_EmptyExpression_Throws(string? expr)
    {
        Assert.Throws<ArgumentException>(() => SafeExpressionParser.Evaluate(expr!));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2+@")]
    [InlineData("sin(0)")]
    [InlineData("2^3")]
    [InlineData("sqrt(4)")]
    public void Evaluate_InvalidCharacters_Throws(string expr)
    {
        Assert.Throws<ArgumentException>(() => SafeExpressionParser.Evaluate(expr));
    }

    [Fact]
    public void Evaluate_DivisionByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => SafeExpressionParser.Evaluate("5/0"));
    }

    [Fact]
    public void Evaluate_ModuloByZero_Throws()
    {
        Assert.Throws<DivideByZeroException>(() => SafeExpressionParser.Evaluate("5%0"));
    }

    [Fact]
    public void Evaluate_MissingParenthesis_Throws()
    {
        Assert.Throws<ArgumentException>(() => SafeExpressionParser.Evaluate("(2+3"));
    }

    [Fact]
    public void Evaluate_NegativeResult_Works()
    {
        var result = SafeExpressionParser.Evaluate("3-10");
        Assert.Equal(-7m, result);
    }

    [Fact]
    public void Evaluate_DeepNesting_Works()
    {
        var expr = "((((((1+2)+3)+4)+5)+6)+7)";
        var result = SafeExpressionParser.Evaluate(expr);
        Assert.Equal(28m, result);
    }

    [Fact]
    public void Evaluate_LongExpression_Works()
    {
        var expr = string.Join("+", Enumerable.Range(1, 50));
        var result = SafeExpressionParser.Evaluate(expr);
        Assert.Equal(1275m, result);
    }

    [Fact]
    public void Evaluate_ExpressionTooLong_Throws()
    {
        var expr = new string('1', 2001) + "+2";
        Assert.Throws<ArgumentException>(() => SafeExpressionParser.Evaluate(expr));
    }
}
