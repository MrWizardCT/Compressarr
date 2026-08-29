namespace Compressarr.Core.Diagnostics;

public interface ICpuUsageSampler
{
    /// <summary>Best-effort system-wide CPU usage percent (0-100), sampled over a short internal
    /// window. Returns null if unavailable on this platform/environment - callers must treat
    /// null as "don't show a CPU figure", never coalesce it to 0.</summary>
    Task<double?> SampleAsync();
}
