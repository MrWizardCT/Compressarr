namespace Compressarr.Core.Presets;

/// <summary>
/// HandBrake's view of Compressarr's profile catalog: the names a lane can pick, the container a
/// profile writes, and the generated presets file HandBrakeCLI is handed. Everything comes from
/// <see cref="IHandBrakeProfileStore"/> (built-ins plus the user's own profiles) - the HandBrake
/// app's presets.json plays no part here.
/// </summary>
public sealed class HandBrakePresetService : IEncoderPresetService
{
    private readonly IHandBrakeProfileStore _store;

    public HandBrakePresetService(IHandBrakeProfileStore store) => _store = store;

    public IReadOnlyList<HandBrakePreset> GetPresets() =>
        _store.GetAll().Select(p => new HandBrakePreset { PresetName = p.Name, FileFormat = p.FileFormat }).ToList();

    public IReadOnlyList<string> GetPresetNames() =>
        _store.GetAll().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public bool PresetExists(string presetName) => _store.Find(presetName) is not null;

    public string GetOutputExtension(string presetName, out string? warning)
    {
        warning = null;
        var profile = _store.Find(presetName);
        if (profile is null)
        {
            warning = $"Compressarr: profile '{presetName}' not found - defaulting output extension to .mp4";
            return ".mp4";
        }

        var format = profile.FileFormat ?? "";
        if (format.Contains("mkv", StringComparison.OrdinalIgnoreCase)) return ".mkv";
        if (format.Contains("mp4", StringComparison.OrdinalIgnoreCase)) return ".mp4";

        warning = $"Compressarr: profile '{presetName}' has unrecognized FileFormat '{format}' - defaulting output extension to .mp4";
        return ".mp4";
    }

    public string PreparePresetSource() => _store.EnsureActiveFile();
}
