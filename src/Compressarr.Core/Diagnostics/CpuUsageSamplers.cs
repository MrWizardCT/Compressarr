using System.Runtime.Versioning;

namespace Compressarr.Core.Diagnostics;

/// <summary>Windows implementation via System.Diagnostics.PerformanceCounter (builds
/// cross-platform, functions only on Windows at runtime - same story as the already-referenced
/// Microsoft.VisualBasic.FileIO used by WindowsTrashService). Any failure (missing perf counter
/// category on a locked-down/Server-Core box, etc.) maps to null, never thrown.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuUsageSampler : ICpuUsageSampler
{
    public Task<double?> SampleAsync()
    {
        try
        {
            using var counter = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            counter.NextValue(); // first call always returns 0 - primes the counter
            Thread.Sleep(150);
            return Task.FromResult<double?>(Math.Round(counter.NextValue(), 1));
        }
        catch
        {
            return Task.FromResult<double?>(null);
        }
    }
}

/// <summary>Reads /proc/stat's first line twice ~150ms apart and computes the busy/total delta.
/// Parsing factored into an internal static pure function for fixture-based testing without a
/// real /proc filesystem.</summary>
public sealed class LinuxCpuUsageSampler : ICpuUsageSampler
{
    private const string StatPath = "/proc/stat";

    public async Task<double?> SampleAsync()
    {
        try
        {
            var line1 = await ReadFirstLineAsync();
            await Task.Delay(150);
            var line2 = await ReadFirstLineAsync();
            return ComputeCpuPercent(line1, line2);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadFirstLineAsync()
    {
        using var reader = new StreamReader(StatPath);
        return await reader.ReadLineAsync() ?? "";
    }

    /// <summary>Parses two /proc/stat "cpu " lines and returns the busy percentage over the
    /// interval between them. Returns null (not a throw, not a false 0%/divide-by-zero) for a
    /// malformed line or a degenerate zero-tick sampling window.</summary>
    internal static double? ComputeCpuPercent(string statLine1, string statLine2)
    {
        var a = ParseCpuLine(statLine1);
        var b = ParseCpuLine(statLine2);
        if (a is null || b is null) return null;

        var (idleA, totalA) = a.Value;
        var (idleB, totalB) = b.Value;

        var idleDelta = idleB - idleA;
        var totalDelta = totalB - totalA;
        if (totalDelta <= 0) return null;

        var busyDelta = totalDelta - idleDelta;
        return Math.Round(100.0 * busyDelta / totalDelta, 1);
    }

    private static (long Idle, long Total)? ParseCpuLine(string line)
    {
        // "cpu  user nice system idle iowait irq softirq steal [guest guest_nice]"
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 8 || parts[0] != "cpu") return null;

        if (!long.TryParse(parts[1], out var user)) return null;
        if (!long.TryParse(parts[2], out var nice)) return null;
        if (!long.TryParse(parts[3], out var system)) return null;
        if (!long.TryParse(parts[4], out var idle)) return null;
        if (!long.TryParse(parts[5], out var iowait)) return null;
        if (!long.TryParse(parts[6], out var irq)) return null;
        if (!long.TryParse(parts[7], out var softirq)) return null;
        var steal = parts.Length > 8 && long.TryParse(parts[8], out var s) ? s : 0;

        var idleTotal = idle + iowait;
        var nonIdle = user + nice + system + irq + softirq + steal;
        return (idleTotal, idleTotal + nonIdle);
    }
}

/// <summary>No macOS environment available to build/test this in - explicit deferral, same
/// framing as MacTrashService's known gaps, not a guess.</summary>
public sealed class MacCpuUsageSampler : ICpuUsageSampler
{
    public Task<double?> SampleAsync() => Task.FromResult<double?>(null);
}

public static class CpuUsageSamplerFactory
{
    public static ICpuUsageSampler CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsCpuUsageSampler();
        if (OperatingSystem.IsLinux()) return new LinuxCpuUsageSampler();
        return new MacCpuUsageSampler();
    }
}
