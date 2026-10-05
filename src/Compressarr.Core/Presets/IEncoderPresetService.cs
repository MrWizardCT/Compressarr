namespace Compressarr.Core.Presets;

/// <summary>
/// The preset catalog the engine and the Settings/Lanes pages need from whichever encoder is in
/// use: which presets exist, and what container a preset writes. Engine-neutral - HandBrake reads
/// them from its presets.json (HandBrakePresetService); another encoder would read its own profile
/// source. "presetsPath" is wherever that catalog lives.
/// </summary>
public interface IEncoderPresetService
{
    IReadOnlyList<string> GetPresetNames(string presetsPath);
    bool PresetExists(string presetName, string presetsPath);

    /// <summary>The output file extension (".mkv"/".mp4") a preset writes. Falls back to ".mp4"
    /// with a warning if the preset is missing or its format is unrecognized, so a bad preset
    /// never stops a run - it just logs.</summary>
    string GetOutputExtension(string presetName, string presetsPath, out string? warning);

    void InvalidateCache(string? presetsPath = null);
}
