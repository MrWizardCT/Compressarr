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
}
