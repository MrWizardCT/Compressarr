using System.Collections.Concurrent;
using System.Text.Json.Nodes;

namespace Compressarr.Core.Presets;

public interface IHandBrakePresetService
{
    IReadOnlyList<HandBrakePreset> GetPresets(string presetsPath);
    IReadOnlyList<string> GetPresetNames(string presetsPath);
    bool PresetExists(string presetName, string presetsPath);
    HandBrakePreset? GetPreset(string presetName, string presetsPath);

    /// <summary>Maps a preset's FileFormat to an output extension. Tolerates both the modern
    /// "av_mp4"/"av_mkv" values and the plain "mp4"/"mkv" values seen in older HandBrake preset
    /// exports — both contain the container name as a substring, so a substring match covers
    /// both. Defaults to ".mp4" with a warning if the preset is missing or the format is
    /// unrecognized (ported verbatim from Get-CompressarrPresetExtension).</summary>
    string GetOutputExtension(string presetName, string presetsPath, out string? warning);

    void InvalidateCache(string? presetsPath = null);
}

/// <summary>
/// Parses/flattens a HandBrake presets.json's PresetList/ChildrenArray tree (HandBrake nests
/// presets under folder groupings) into every leaf preset found. Ported from
/// Get-CompressarrPresetTree/Get-CompressarrPresetObjects: a node is a leaf preset if it carries
/// a PresetName property; it may ALSO carry a ChildrenArray (both are checked independently, not
/// mutually exclusively), in which case its children are walked too. Cached per resolved path,
/// same as v1's module-scoped $script:PresetTreeCache — call InvalidateCache() if the underlying
/// file changes.
/// </summary>
public sealed class HandBrakePresetService : IHandBrakePresetService
{
    private readonly ConcurrentDictionary<string, JsonNode?> _treeCache = new();

    public IReadOnlyList<HandBrakePreset> GetPresets(string presetsPath)
    {
        var tree = GetTree(presetsPath);
        var results = new List<HandBrakePreset>();

        if (tree is JsonObject rootObj && rootObj.TryGetPropertyValue("PresetList", out var presetList) && presetList is not null)
        {
            WalkNode(presetList, results);
        }

        return results;
    }

    public IReadOnlyList<string> GetPresetNames(string presetsPath) =>
        GetPresets(presetsPath).Select(p => p.PresetName).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

    public bool PresetExists(string presetName, string presetsPath) =>
        GetPresetNames(presetsPath).Contains(presetName, StringComparer.OrdinalIgnoreCase);

    public HandBrakePreset? GetPreset(string presetName, string presetsPath) =>
        GetPresets(presetsPath).FirstOrDefault(p => string.Equals(p.PresetName, presetName, StringComparison.OrdinalIgnoreCase));

    public string GetOutputExtension(string presetName, string presetsPath, out string? warning)
    {
        warning = null;
        var preset = GetPreset(presetName, presetsPath);
        if (preset is null)
        {
            warning = $"Compressarr: preset '{presetName}' not found in presets.json - defaulting output extension to .mp4";
            return ".mp4";
        }

        var format = preset.FileFormat ?? "";
        if (format.Contains("mkv", StringComparison.OrdinalIgnoreCase)) return ".mkv";
        if (format.Contains("mp4", StringComparison.OrdinalIgnoreCase)) return ".mp4";

        warning = $"Compressarr: preset '{presetName}' has unrecognized FileFormat '{format}' - defaulting output extension to .mp4";
        return ".mp4";
    }

    public void InvalidateCache(string? presetsPath = null)
    {
        if (presetsPath is null)
        {
            _treeCache.Clear();
        }
        else
        {
            _treeCache.TryRemove(presetsPath, out _);
        }
    }

    private JsonNode? GetTree(string presetsPath)
    {
        return _treeCache.GetOrAdd(presetsPath, path =>
        {
            var raw = File.ReadAllText(path);
            return JsonNode.Parse(raw);
        });
    }

    private static void WalkNode(JsonNode? node, List<HandBrakePreset> results)
    {
        if (node is null) return;

        if (node is JsonArray array)
        {
            foreach (var child in array)
            {
                WalkNode(child, results);
            }
            return;
        }

        if (node is JsonObject obj)
        {
            // "Folder": true marks a category/group header (e.g. "General", "Web", "Matroska")
            // rather than a real, selectable preset - HandBrake's own presets.json carries a
            // PresetName on these too (it's what labels the folder in HandBrake's UI), so that
            // alone isn't enough to tell a real preset apart from a folder.
            var isFolder = obj.TryGetPropertyValue("Folder", out var folderNode) && folderNode is not null && folderNode.GetValue<bool>();

            if (!isFolder && obj.TryGetPropertyValue("PresetName", out var presetNameNode) && presetNameNode is not null)
            {
                results.Add(new HandBrakePreset
                {
                    PresetName = presetNameNode.GetValue<string>(),
                    FileFormat = obj.TryGetPropertyValue("FileFormat", out var formatNode) ? formatNode?.GetValue<string>() : null
                });
            }

            if (obj.TryGetPropertyValue("ChildrenArray", out var childrenNode) && childrenNode is not null)
            {
                WalkNode(childrenNode, results);
            }
        }
    }
}
