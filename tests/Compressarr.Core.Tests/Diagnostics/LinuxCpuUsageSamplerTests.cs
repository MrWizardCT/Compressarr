using Compressarr.Core.Diagnostics;

namespace Compressarr.Core.Tests.Diagnostics;

public class LinuxCpuUsageSamplerTests
{
    [Fact]
    public void ComputeCpuPercent_KnownDelta_ReturnsExpectedPercentage()
    {
        // user nice system idle iowait irq softirq steal
        // line1 total=1000 (idle=750), line2 total=2000 (idle=1500) -> idleDelta=750, totalDelta=1000 -> busy 25%
        var line1 = "cpu  100 0 100 750 0 0 50 0";
        var line2 = "cpu  200 0 200 1500 0 0 100 0"; // user+system+softirq delta = 250, idle delta = 750, total delta = 1000

        var result = LinuxCpuUsageSampler.ComputeCpuPercent(line1, line2);

        Assert.NotNull(result);
        Assert.Equal(25.0, result!.Value, precision: 1);
    }

    [Fact]
    public void ComputeCpuPercent_MalformedLine_ReturnsNullNotThrow()
    {
        var result = LinuxCpuUsageSampler.ComputeCpuPercent("not a valid stat line", "cpu 1 2 3 4 5 6 7 8");

        Assert.Null(result);
    }

    [Fact]
    public void ComputeCpuPercent_TooFewFields_ReturnsNull()
    {
        var result = LinuxCpuUsageSampler.ComputeCpuPercent("cpu 1 2 3", "cpu 1 2 3 4 5 6 7 8");

        Assert.Null(result);
    }

    [Fact]
    public void ComputeCpuPercent_ZeroDeltaWindow_ReturnsNull_NotDivideByZeroOrFalseZero()
    {
        var line = "cpu  100 0 100 750 0 0 50 0";

        var result = LinuxCpuUsageSampler.ComputeCpuPercent(line, line);

        Assert.Null(result);
    }

    [Fact]
    public void ComputeCpuPercent_WrongPrefix_ReturnsNull()
    {
        var result = LinuxCpuUsageSampler.ComputeCpuPercent("cpu0 1 2 3 4 5 6 7 8", "cpu0 1 2 3 4 5 6 7 8");

        Assert.Null(result);
    }
}
