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
/// can refuse while something still needs it. Scoped to one encoder: HandBrake and ffmpeg each have
/// their own profile of a given name (the built-ins share names), and a lane only ever picks from its
/// own encoder's list, so only that encoder's lanes (and their queued files) count.</summary>
public static class ProfileReferences
{
    public static ProfileUsage Find(CompressarrConfig config, IEnumerable<ResumeEntry> resume, string name, EncoderEngine engine = EncoderEngine.HandBrake)
    {
        bool Is(string? n) => string.Equals(n, name, StringComparison.OrdinalIgnoreCase);
        var engineByLane = config.Lanes.ToDictionary(l => l.Id, l => l.Engine);

        var lanes = config.Lanes.Where(l => l.Engine == engine && (Is(l.TvPreset) || Is(l.MoviePreset))).Select(l => l.DisplayName).ToList();
        // A finished file's override is history, not a need.
        var queued = resume.Count(e => e.Status != ResumeStatus.Completed && Is(e.PresetOverride)
            && engineByLane.TryGetValue(e.LaneId, out var laneEngine) && laneEngine == engine);
        return new ProfileUsage(lanes, queued);
    }

    /// <summary>Repoints every lane preset and queued-file override named in <paramref name="renames"/>
    /// (old name to new name, case-insensitive) for lanes of <paramref name="engine"/>, in the saved config
    /// and resume state.</summary>
    public static void Repoint(IConfigStore configStore, IResumeStateStore resumeStore, IReadOnlyDictionary<string, string> renames, EncoderEngine engine = EncoderEngine.HandBrake)
    {
        if (renames.Count == 0) return;
        var map = new Dictionary<string, string>(renames, StringComparer.OrdinalIgnoreCase);

        var laneIds = new HashSet<string>();
        configStore.Update(AppPaths.GetConfigFilePath(), c =>
        {
            foreach (var lane in c.Lanes.Where(l => l.Engine == engine))
            {
                laneIds.Add(lane.Id);
                if (map.TryGetValue(lane.TvPreset, out var tv)) lane.TvPreset = tv;
                if (map.TryGetValue(lane.MoviePreset, out var movie)) lane.MoviePreset = movie;
            }
            return true;
        });

        resumeStore.Update(AppPaths.GetResumeFilePath(), entries =>
        {
            foreach (var entry in entries.Where(e => laneIds.Contains(e.LaneId)))
            {
                if (entry.PresetOverride is not null && map.TryGetValue(entry.PresetOverride, out var name)) entry.PresetOverride = name;
            }
            return true;
        });
    }
}
