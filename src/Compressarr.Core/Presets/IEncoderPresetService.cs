namespace Compressarr.Core.Presets;

/// <summary>
/// The profile catalog the engine and the Lanes/Monitor pages need from whichever encoder is in
/// use: which profiles exist, and what container a profile writes. Engine-neutral - HandBrake's
/// come from Compressarr's own profile store (HandBrakePresetService); another encoder would read
/// its own profiles. There is no "where is the file" parameter: Compressarr always uses its own.
/// </summary>
public interface IEncoderPresetService
{
    IReadOnlyList<string> GetPresetNames();
    bool PresetExists(string presetName);

    /// <summary>The output file extension (".mkv"/".mp4") a profile writes. Falls back to ".mp4"
    /// with a warning if the profile is missing or its format is unrecognized, so a bad profile
    /// never stops a run - it just logs.</summary>
    string GetOutputExtension(string presetName, out string? warning);

    /// <summary>Gets the encoder's profile source up to date and returns what to hand the encoder
    /// runner as EncodeRequest.PresetSource (for HandBrake: the generated presets file, rewritten
    /// only if it is missing or out of date). Called before every encode.</summary>
    string PreparePresetSource();
}
