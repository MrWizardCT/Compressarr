using Compressarr.Core.Config;

namespace Compressarr.Core.Tests.Config;

public class ByteSizeTests
{
    [Theory]
    [InlineData("0gb", 0L)]
    [InlineData("500mb", 500L * 1024 * 1024)]
    [InlineData("2GB", 2L * 1024 * 1024 * 1024)]
    [InlineData("2gb", 2L * 1024 * 1024 * 1024)]
    [InlineData("1024", 1024L)]
    public void Parse_ValidSizes_ReturnsExpectedByteCount(string value, long expected)
    {
        Assert.Equal(expected, ByteSize.Parse(value));
    }

    [Fact]
    public void Parse_Blank_ReturnsZero()
    {
        Assert.Equal(0, ByteSize.Parse(""));
        Assert.Equal(0, ByteSize.Parse("   "));
    }

    [Fact]
    public void Parse_Garbage_Throws()
    {
        Assert.Throws<FormatException>(() => ByteSize.Parse("not-a-size"));
    }
}
