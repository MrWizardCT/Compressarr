using Compressarr.Core.Presets;

namespace Compressarr.Core.FFmpeg;

/// <summary>ffmpeg's view of its profile catalog: the names a lane can pick and the container each
/// profile writes. (The HandBrake counterpart is <see cref="HandBrakePresetService"/>.)</summary>
public sealed class FFmpegPresetService : IEncoderPresetService
{
    private readonly IFFmpegProfileStore _store;

    public FFmpegPresetService(IFFmpegProfileStore store) => _store = store;

    public IReadOnlyList<string> GetPresetNames() =>
        _store.GetAll().Select(p => p.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public bool PresetExists(string presetName) => _store.Find(presetName) is not null;

    public string GetOutputExtension(string presetName, out string? warning)
    {
        var profile = _store.Find(presetName);
        if (profile is null)
        {
            warning = $"Compressarr: ffmpeg profile '{presetName}' not found - defaulting output extension to .mkv";
            return ".mkv";
        }

        warning = null;
        return profile.Extension;
    }

    /// <summary>ffmpeg profiles are read straight from the store by the runner; there is no file to generate.</summary>
    public string PreparePresetSource() => "";
}
