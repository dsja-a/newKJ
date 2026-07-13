using Keji.Security.Authorization;

namespace Keji.Security.Tests.Authorization;

public sealed class ToolPermissionDescriptorTests
{
    [Fact]
    public void Constructor_ValidDescriptor_PreservesExactImmutableValues()
    {
        const string name = "read_file";
        var descriptor = new KejiToolPermissionDescriptor(name, KejiToolAccessLevel.Read);

        Assert.Equal(name, descriptor.Name);
        Assert.Equal(KejiToolAccessLevel.Read, descriptor.AccessLevel);
        Assert.Null(typeof(KejiToolPermissionDescriptor).GetProperty(nameof(descriptor.Name))!.SetMethod);
        Assert.Null(typeof(KejiToolPermissionDescriptor).GetProperty(nameof(descriptor.AccessLevel))!.SetMethod);
    }

    [Fact]
    public void Constructor_NullName_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new KejiToolPermissionDescriptor(null!, KejiToolAccessLevel.Read));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public void Constructor_EmptyOrWhitespaceName_ThrowsArgumentException(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new KejiToolPermissionDescriptor(name, KejiToolAccessLevel.Read));
    }

    [Theory]
    [InlineData(" read_file")]
    [InlineData("read_file ")]
    [InlineData("\tread_file")]
    [InlineData("read_file\r\n")]
    public void Constructor_SurroundingWhitespace_IsRejectedRatherThanTrimmed(string name)
    {
        Assert.Throws<ArgumentException>(() =>
            new KejiToolPermissionDescriptor(name, KejiToolAccessLevel.Read));
    }

    [Fact]
    public void Constructor_ExactlyTwoHundredFiftySixCharacters_IsAccepted()
    {
        var name = new string('a', 256);

        var descriptor = new KejiToolPermissionDescriptor(name, KejiToolAccessLevel.Write);

        Assert.Equal(name, descriptor.Name);
        Assert.Equal(256, descriptor.Name.Length);
    }

    [Fact]
    public void Constructor_MoreThanTwoHundredFiftySixCharacters_ThrowsArgumentException()
    {
        var name = new string('a', 257);

        Assert.Throws<ArgumentException>(() =>
            new KejiToolPermissionDescriptor(name, KejiToolAccessLevel.Write));
    }

    [Fact]
    public void Constructor_InvalidAccessLevel_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new KejiToolPermissionDescriptor("read_file", (KejiToolAccessLevel)int.MaxValue));
    }
}
