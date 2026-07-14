using Keji.Tools;
using Keji.Tools.Names;

namespace Keji.Tools.Tests;

public class KejiToolNameTests
{
    [Theory]
    [InlineData("read_file")]
    [InlineData("a")]
    [InlineData("tool123")]
    [InlineData("z")]
    [InlineData("a0")]
    [InlineData("test_tool")]
    public void Create_ValidNames_Succeeds(string name)
    {
        var n = KejiToolName.Create(name);
        Assert.Equal(name, n.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ReadFile")]
    [InlineData("1starts_with_digit")]
    [InlineData("has-dash")]
    [InlineData("has.period")]
    [InlineData("has space")]
    [InlineData("UPPERCASE")]
    [InlineData("MixedCase")]
    public void Create_InvalidNames_Throws(string? name)
    {
        Assert.Throws<KejiToolContractException>(() => KejiToolName.Create(name!));
    }

    [Fact]
    public void Create_NameTooLong_Throws()
    {
        Assert.Throws<KejiToolContractException>(() =>
            KejiToolName.Create(new string('x', 65)));
    }

    [Fact]
    public void Create_NameAtMaxLength_Succeeds()
    {
        var n = KejiToolName.Create(new string('x', 64));
        Assert.Equal(64, n.Value.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("ReadFile")]
    [InlineData("1starts_with_digit")]
    [InlineData("has-dash")]
    public void TryCreate_InvalidNames_ReturnsFalse(string? name)
    {
        var result = KejiToolName.TryCreate(name!, out _);
        Assert.False(result);
    }

    [Fact]
    public void TryCreate_ValidName_ReturnsTrue()
    {
        Assert.True(KejiToolName.TryCreate("valid_name", out var name));
        Assert.Equal("valid_name", name!.Value);
    }

    [Fact]
    public void Equals_SameName_ReturnsTrue()
    {
        var a = KejiToolName.Create("test_tool");
        var b = KejiToolName.Create("test_tool");
        Assert.True(a.Equals(b));
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentName_ReturnsFalse()
    {
        var a = KejiToolName.Create("tool_a");
        var b = KejiToolName.Create("tool_b");
        Assert.False(a.Equals(b));
        Assert.True(a != b);
    }

    [Fact]
    public void Equals_Null_ReturnsFalse()
    {
        var a = KejiToolName.Create("test_tool");
#pragma warning disable CS8625
        Assert.False(a.Equals(null));
#pragma warning restore CS8625
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var n = KejiToolName.Create("my_tool");
        Assert.Equal("my_tool", n.ToString());
    }

    [Fact]
    public void OrdinalComparison_OrdersCorrectly()
    {
        var a = KejiToolName.Create("a_tool");
        var b = KejiToolName.Create("b_tool");
        Assert.Equal(-1, string.CompareOrdinal(a.Value, b.Value));
        Assert.Equal(1, string.CompareOrdinal(b.Value, a.Value));
        Assert.Equal(0, string.CompareOrdinal(a.Value, a.Value));
    }
}
