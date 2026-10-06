using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Presets;

/// <summary>Who is using a profile: the lanes naming it as their TV or Movie preset, and the queued
/// files with a per-file override for it.</summary>
public sealed record ProfileUsage(IReadOnlyList<string> Lanes, int QueuedFiles)
{
    public bool InUse => Lanes.Count > 0 || QueuedFiles > 0;
}

/// <summary>Finds and rewrites the places that name a profile - lane presets (config) and per-file
/// overrides (resume state) - so a rename keeps everything pointing at the same recipe, and a delete
/// can refuse while something still needs it.</summary>
public static class ProfileReferences
{
    public static ProfileUsage Find(CompressarrConfig config, IEnumerable<ResumeEntry> resume, string name)
    {
        bool Is(string? n) => string.Equals(n, name, StringComparison.OrdinalIgnoreCase);

        var lanes = config.Lanes.Where(l => Is(l.TvPreset) || Is(l.MoviePreset)).Select(l => l.DisplayName).ToList();
        // A finished file's override is history, not a need.
        var queued = resume.Count(e => e.Status != ResumeStatus.Completed && Is(e.PresetOverride));
        return new ProfileUsage(lanes, queued);
    }

    /// <summary>Repoints every lane preset and queued-file override named in <paramref name="renames"/>
    /// (old name to new name, case-insensitive) in the saved config and resume state.</summary>
    public static void Repoint(IConfigStore configStore, IResumeStateStore resumeStore, IReadOnlyDictionary<string, string> renames)
    {
        if (renames.Count == 0) return;
        var map = new Dictionary<string, string>(renames, StringComparer.OrdinalIgnoreCase);

        configStore.Update(AppPaths.GetConfigFilePath(), c =>
        {
            foreach (var lane in c.Lanes)
            {
                if (map.TryGetValue(lane.TvPreset, out var tv)) lane.TvPreset = tv;
                if (map.TryGetValue(lane.MoviePreset, out var movie)) lane.MoviePreset = movie;
            }
            return true;
        });

        resumeStore.Update(AppPaths.GetResumeFilePath(), entries =>
        {
            foreach (var entry in entries)
            {
                if (entry.PresetOverride is not null && map.TryGetValue(entry.PresetOverride, out var name)) entry.PresetOverride = name;
            }
            return true;
        });
    }
}
