namespace Compressarr.Core.Conversion;

public sealed class ConversionResult
{
    public required string LaneId { get; init; }
    public required string FileName { get; init; }
    public required string FullName { get; init; }
    public string? NewFileName { get; init; }
    public required string ContentType { get; init; }
    public string? PresetName { get; init; }
    public double BeginSizeGb { get; init; }
    public double EndSizeGb { get; init; }
    public bool Success { get; init; }
    public string? DetailLogFile { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public string? ArrStatus { get; init; }

    /// <summary>True if this file's failure (encode or move) looked like the volume being out of
    /// space, rather than some other error. Bubbles up through RunResult so the run loop can stop
    /// monitoring automatically instead of repeatedly retrying a failure that won't resolve
    /// itself on the next poll.</summary>
    public bool DiskFull { get; init; }

    /// <summary>Short, human-readable reason shown in the report's Status column in place of the
    /// generic "ERROR", for the specific failure modes Compressarr can actually diagnose. Null for
    /// a failure with no more specific known cause (the report still shows plain "ERROR" for
    /// those) - this is deliberately not a catch-all "why did this fail" field.</summary>
    public string? FailureReason { get; init; }
}
