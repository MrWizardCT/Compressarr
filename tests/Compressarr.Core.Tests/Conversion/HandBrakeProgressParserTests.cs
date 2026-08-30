using Compressarr.Core.Conversion;

namespace Compressarr.Core.Tests.Conversion;

public class HandBrakeProgressParserTests
{
    [Fact]
    public void TryParse_FullProgressLine_ParsesPercentFpsAndEta()
    {
        var result = HandBrakeProgressParser.TryParse("Encoding: task 1 of 1, 42.10 % (23.45 fps, avg 20.12 fps, ETA 00h05m32s)");

        Assert.NotNull(result);
        Assert.Equal(42.10, result!.Percent);
        Assert.Equal(23.45, result.Fps);
        Assert.Equal("00h05m32s", result.Eta);
    }

    [Fact]
    public void TryParse_PercentOnly_NoParenthetical_ParsesPercent()
    {
        var result = HandBrakeProgressParser.TryParse("Encoding: task 1 of 1, 7.00 %");

        Assert.NotNull(result);
        Assert.Equal(7.00, result!.Percent);
        Assert.Null(result.Fps);
        Assert.Null(result.Eta);
    }

    [Fact]
    public void TryParse_UnrelatedLine_ReturnsNull()
    {
        Assert.Null(HandBrakeProgressParser.TryParse("Finished work at Sat Jan  1 00:00:00 2026"));
        Assert.Null(HandBrakeProgressParser.TryParse(""));
        Assert.Null(HandBrakeProgressParser.TryParse("Encoding started..."));
    }

    [Fact]
    public void TryParse_MultiTaskRun_IgnoresTaskNumbers_UsesPercent()
    {
        var result = HandBrakeProgressParser.TryParse("Encoding: task 2 of 3, 99.99 % (1.00 fps, avg 1.00 fps, ETA 00h00m01s)");

        Assert.NotNull(result);
        Assert.Equal(99.99, result!.Percent);
    }
}
