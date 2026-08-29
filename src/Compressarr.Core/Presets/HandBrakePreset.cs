namespace Compressarr.Core.Presets;

/// <summary>A single leaf preset flattened out of HandBrake's presets.json tree.</summary>
public sealed class HandBrakePreset
{
    public required string PresetName { get; init; }
    public string? FileFormat { get; init; }
}
