using System.Text.Json;
using System.Text.Json.Nodes;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Presets;

/// <summary>What the one-time migration did, for the log and the marker file.</summary>
public sealed record HandBrakeProfileMigrationResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<KeyValuePair<string, string>> Renamed,
    IReadOnlyList<string> Missing);

public interface IHandBrakeProfileMigration
{
    /// <summary>Runs once, the first time 2.2 starts: carries the presets a lane (or a queued file's
    /// override) was using over from the old HandBrake presets.json into Compressarr's own profiles,
    /// so nothing a user relied on changes behind their back. Returns null when it has already run.</summary>
    HandBrakeProfileMigrationResult? RunIfNeeded();

    /// <summary>Safe to call any time (start-up, after a settings import or a backup restore): copies
    /// into your own profiles any HandBrake preset a lane or queued file still names that Compressarr
    /// doesn't have, from the old presets.json. Unlike the one-time migration it never renames or
    /// repoints anything - it only fills in what is missing - so settings that arrive after the first
    /// start (an imported config, a restored 2.1.x backup, lanes added later) still find their presets.
    /// Returns the names it copied.</summary>
    IReadOnlyList<string> RecoverMissing();
}

/// <summary>
/// Until 2.2, lanes named presets that lived in HandBrake's own presets.json. From 2.2 Compressarr
/// only uses its own profiles (built-ins plus the user's), so every preset still referenced is
/// resolved once:
///  - already a built-in with an identical definition: nothing to do, the built-in is used;
///  - not a built-in: copied from presets.json into the user's profiles under the same name;
///  - same name as a built-in but a different definition (the user edited it): copied as
///    "&lt;name&gt; (yours)" and the lane/queue references are repointed to it, so the user's recipe
///    keeps running and the built-in stays the locked original;
///  - not found in presets.json at all: left alone (the Lanes page flags it as it always did).
/// Presets nothing references are not copied - Import (Profiles page) is the way to bring those in.
/// A marker file under Profiles records that this ran and what it did.
/// </summary>
public sealed class HandBrakeProfileMigration : IHandBrakeProfileMigration
{
    private const string MarkerFileName = "migration-2.2.json";

    private readonly IConfigStore _configStore;
    private readonly IResumeStateStore _resumeStore;
    private readonly IPathExpander _pathExpander;
    private readonly IHandBrakeProfileStore _profiles;
    private readonly IRunLogger _logger;

    public HandBrakeProfileMigration(
        IConfigStore configStore,
        IResumeStateStore resumeStore,
        IPathExpander pathExpander,
        IHandBrakeProfileStore profiles,
        IRunLogger logger)
    {
        _configStore = configStore;
        _resumeStore = resumeStore;
        _pathExpander = pathExpander;
        _profiles = profiles;
        _logger = logger;
    }

    public HandBrakeProfileMigrationResult? RunIfNeeded()
    {
        var markerPath = Path.Combine(AppPaths.GetProfilesDirectory(), MarkerFileName);
        if (File.Exists(markerPath)) return null;

        var configPath = AppPaths.GetConfigFilePath();
        var resumePath = AppPaths.GetResumeFilePath();
        var config = _configStore.Load(configPath);
        var resume = _resumeStore.Load(resumePath);

        var referenced = config.Lanes
            .SelectMany(l => new[] { l.TvPreset, l.MoviePreset })
            .Concat(resume.Select(e => e.PresetOverride ?? ""))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var oldLeaves = new List<HandBrakePresetFile.Leaf>();
        var oldPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
        if (referenced.Count > 0 && !string.IsNullOrWhiteSpace(oldPath) && File.Exists(oldPath)
            && HandBrakePresetFile.TryRead(oldPath, out var oldRoot, out _))
        {
            oldLeaves = HandBrakePresetFile.GetLeaves(oldRoot);
        }

        var toAdd = new List<JsonObject>();
        var imported = new List<string>();
        var renamed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();

        foreach (var name in referenced)
        {
            var old = oldLeaves.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            var current = _profiles.Find(name);

            if (old is null)
            {
                if (current is null) missing.Add(name);
                continue;
            }

            if (current is null)
            {
                toAdd.Add((JsonObject)old.Definition.DeepClone());
                imported.Add(old.Name);
            }
            else if (!JsonNode.DeepEquals(old.Definition, current.Definition))
            {
                var newName = $"{old.Name} (yours)";
                renamed[name] = newName;

                // Re-running after a partial earlier attempt finds the copy already there.
                if (_profiles.Find(newName) is null)
                {
                    var copy = (JsonObject)old.Definition.DeepClone();
                    copy["PresetName"] = newName;
                    toAdd.Add(copy);
                }
            }
        }

        if (toAdd.Count > 0) _profiles.AddUserProfiles(toAdd);

        ProfileReferences.Repoint(_configStore, _resumeStore, renamed);

        var result = new HandBrakeProfileMigrationResult(imported, renamed.ToList(), missing);
        WriteMarker(markerPath, result);
        Log(result);
        return result;
    }

    public IReadOnlyList<string> RecoverMissing()
    {
        var config = _configStore.Load(AppPaths.GetConfigFilePath());
        var resume = _resumeStore.Load(AppPaths.GetResumeFilePath());
        var handBrakeLanes = config.Lanes.Where(l => l.Engine == EncoderEngine.HandBrake).ToList();
        var laneIds = handBrakeLanes.Select(l => l.Id).ToHashSet();

        var missing = handBrakeLanes
            .SelectMany(l => new[] { l.TvPreset, l.MoviePreset })
            .Concat(resume.Where(e => laneIds.Contains(e.LaneId) && e.Status != Conversion.ResumeStatus.Completed).Select(e => e.PresetOverride ?? ""))
            .Where(n => !string.IsNullOrWhiteSpace(n) && _profiles.Find(n) is null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (missing.Count == 0) return Array.Empty<string>();

        var oldPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
        if (string.IsNullOrWhiteSpace(oldPath) || !File.Exists(oldPath) || !HandBrakePresetFile.TryRead(oldPath, out var root, out _))
        {
            return Array.Empty<string>();
        }

        var leaves = HandBrakePresetFile.GetLeaves(root);
        var toAdd = missing
            .Select(name => leaves.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<HandBrakePresetFile.Leaf>()
            .Select(l => (JsonObject)l.Definition.DeepClone())
            .ToList();
        if (toAdd.Count == 0) return Array.Empty<string>();

        _profiles.AddUserProfiles(toAdd);
        var names = toAdd.Select(d => d["PresetName"]!.GetValue<string>()).ToList();
        _logger.Log($"Profiles: copied {names.Count} preset(s) your lanes use from HandBrake's presets.json into Compressarr's own profiles: {string.Join(", ", names)}.");
        return names;
    }

    private void Log(HandBrakeProfileMigrationResult result)
    {
        if (result.Imported.Count > 0)
        {
            _logger.Log($"Profiles: copied {result.Imported.Count} preset(s) your lanes use from HandBrake's presets.json into Compressarr's own profiles: {string.Join(", ", result.Imported)}.");
        }
        foreach (var (from, to) in result.Renamed)
        {
            _logger.Log($"Profiles: your preset '{from}' differs from Compressarr's built-in of the same name, so it was kept as '{to}' and your lanes now use that.");
        }
        foreach (var name in result.Missing)
        {
            _logger.Log($"Profiles: preset '{name}' is used by a lane or queued file but was not found - add or import a profile with that name, or pick another on the Lanes page.", LogSeverity.Error);
        }
    }

    private static void WriteMarker(string path, HandBrakeProfileMigrationResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var marker = new JsonObject
        {
            ["migratedAtUtc"] = DateTime.UtcNow.ToString("o"),
            ["imported"] = new JsonArray(result.Imported.Select(n => (JsonNode)n).ToArray()),
            ["renamed"] = new JsonArray(result.Renamed.Select(r => (JsonNode)new JsonObject { ["from"] = r.Key, ["to"] = r.Value }).ToArray()),
            ["missing"] = new JsonArray(result.Missing.Select(n => (JsonNode)n).ToArray())
        };
        File.WriteAllText(path, marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
