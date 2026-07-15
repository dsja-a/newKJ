using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Keji.ToolWorker;

public static class SafeExpressionParser
{
    private const int MaxTokenCount = 100;
    private const int MaxDepth = 32;
    private const int MaxSteps = 10000;

    private static readonly HashSet<char> AllowedChars =
    [
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
        '+', '-', '*', '/', '%', '(', ')', '.', ' '
    ];

    public static decimal Evaluate(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            throw new ArgumentException("Expression must not be empty");

        if (expression.Length > 2000)
            throw new ArgumentException("Expression exceeds maximum length of 2000 characters");

        foreach (char c in expression)
        {
            if (!AllowedChars.Contains(c))
                throw new ArgumentException($"Invalid character in expression: '{c}'");
        }

        var tokens = Tokenize(expression);
        if (tokens.Count == 0)
            throw new ArgumentException("No valid tokens in expression");

        if (tokens.Count > MaxTokenCount)
            throw new ArgumentException($"Expression exceeds maximum token count of {MaxTokenCount}");

        int pos = 0;
        int steps = 0;
        var result = ParseAddSub(tokens, ref pos, ref steps);
        if (pos != tokens.Count)
            throw new ArgumentException("Unexpected tokens after end of expression");

        return result;
    }

    private enum TokenType { Number, Plus, Minus, Star, Slash, Percent, LParen, RParen }

    [InlineArray(8)]
    private struct TokenBuffer
    {
        private Token _element0;
    }

    private readonly struct Token(TokenType type, decimal value = 0)
    {
        public readonly TokenType Type = type;
        public readonly decimal Value = value;
    }

    private static List<Token> Tokenize(string expr)
    {
        var tokens = new List<Token>();
        int i = 0;

        while (i < expr.Length)
        {
            char c = expr[i];

            if (c == ' ') { i++; continue; }
            if (c == '+') { tokens.Add(new Token(TokenType.Plus)); i++; continue; }
            if (c == '-') { tokens.Add(new Token(TokenType.Minus)); i++; continue; }
            if (c == '*') { tokens.Add(new Token(TokenType.Star)); i++; continue; }
            if (c == '/') { tokens.Add(new Token(TokenType.Slash)); i++; continue; }
            if (c == '%') { tokens.Add(new Token(TokenType.Percent)); i++; continue; }
            if (c == '(') { tokens.Add(new Token(TokenType.LParen)); i++; continue; }
            if (c == ')') { tokens.Add(new Token(TokenType.RParen)); i++; continue; }

            if (c is >= '0' and <= '9' || c == '.')
            {
                int start = i;
                bool hasDot = false;
                while (i < expr.Length && (expr[i] is >= '0' and <= '9' || expr[i] == '.'))
                {
                    if (expr[i] == '.')
                    {
                        if (hasDot) throw new ArgumentException("Multiple decimal points in number");
                        hasDot = true;
                    }
                    i++;
                }

                var numStr = expr[start..i];
                if (numStr.Length > 50)
                    throw new ArgumentException("Number literal too long");

                if (!decimal.TryParse(numStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var val))
                    throw new ArgumentException($"Invalid number: {numStr}");

                tokens.Add(new Token(TokenType.Number, val));
                continue;
            }

            throw new ArgumentException($"Unexpected character at position {i}: '{c}'");
        }

        return tokens;
    }

    private static decimal ParseAddSub(List<Token> tokens, ref int pos, ref int steps)
    {
        var left = ParseMulDiv(tokens, ref pos, ref steps);

        while (pos < tokens.Count)
        {
            if (tokens[pos].Type == TokenType.Plus)
            {
                pos++;
                steps++;
                if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
                var right = ParseMulDiv(tokens, ref pos, ref steps);
                left += right;
            }
            else if (tokens[pos].Type == TokenType.Minus)
            {
                pos++;
                steps++;
                if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
                var right = ParseMulDiv(tokens, ref pos, ref steps);
                left -= right;
            }
            else break;
        }

        return left;
    }

    private static decimal ParseMulDiv(List<Token> tokens, ref int pos, ref int steps)
    {
        var left = ParseUnary(tokens, ref pos, ref steps);

        while (pos < tokens.Count)
        {
            if (tokens[pos].Type == TokenType.Star)
            {
                pos++;
                steps++;
                if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
                var right = ParseUnary(tokens, ref pos, ref steps);
                left *= right;
            }
            else if (tokens[pos].Type == TokenType.Slash)
            {
                pos++;
                steps++;
                if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
                var right = ParseUnary(tokens, ref pos, ref steps);
                if (right == 0) throw new DivideByZeroException("Division by zero");
                left /= right;
            }
            else if (tokens[pos].Type == TokenType.Percent)
            {
                pos++;
                steps++;
                if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
                var right = ParseUnary(tokens, ref pos, ref steps);
                if (right == 0) throw new DivideByZeroException("Modulo by zero");
                left %= right;
            }
            else break;
        }

        return left;
    }

    private static decimal ParseUnary(List<Token> tokens, ref int pos, ref int steps)
    {
        if (pos >= tokens.Count)
            throw new ArgumentException("Unexpected end of expression");

        if (tokens[pos].Type == TokenType.Minus)
        {
            pos++;
            steps++;
            if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
            var val = ParsePrimary(tokens, ref pos, ref steps);
            return -val;
        }

        if (tokens[pos].Type == TokenType.Plus)
        {
            pos++;
            steps++;
            if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
            return ParsePrimary(tokens, ref pos, ref steps);
        }

        return ParsePrimary(tokens, ref pos, ref steps);
    }

    private static decimal ParsePrimary(List<Token> tokens, ref int pos, ref int steps)
    {
        if (pos >= tokens.Count)
            throw new ArgumentException("Unexpected end of expression");

        if (tokens[pos].Type == TokenType.Number)
        {
            var val = tokens[pos].Value;
            pos++;
            return val;
        }

        if (tokens[pos].Type == TokenType.LParen)
        {
            pos++;
            steps++;
            if (steps > MaxSteps) throw new InvalidOperationException("Expression evaluation exceeded step limit");
            if (pos > MaxDepth) throw new InvalidOperationException("Expression nesting depth exceeded");
            var val = ParseAddSub(tokens, ref pos, ref steps);

            if (pos >= tokens.Count || tokens[pos].Type != TokenType.RParen)
                throw new ArgumentException("Missing closing parenthesis");

            pos++;
            return val;
        }

        throw new ArgumentException($"Unexpected token at position {pos}: {tokens[pos].Type}");
    }
}
