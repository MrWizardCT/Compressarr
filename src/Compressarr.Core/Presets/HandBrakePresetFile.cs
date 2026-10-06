using System.Text.Json.Nodes;

namespace Compressarr.Core.Presets;

/// <summary>
/// Reads HandBrake's preset-file format (what presets.json and a "Presets - Export" file both
/// are): a PresetList whose nodes are either folders (grouping headers) or leaf presets, with
/// folders nesting further nodes under ChildrenArray. A node is a leaf if it carries a PresetName
/// and is not marked "Folder": true - HandBrake's own file puts a PresetName on folders too (it
/// labels the folder in its UI), so the name alone doesn't tell the two apart.
/// </summary>
public static class HandBrakePresetFile
{
    /// <summary>One leaf preset plus the folder it sat in (null at the top level).</summary>
    public sealed record Leaf(string Name, string? Group, JsonObject Definition)
    {
        public string? FileFormat => Definition["FileFormat"]?.GetValue<string>();
    }

    /// <summary>Every leaf preset in the tree, in file order. Definitions are the live nodes of
    /// <paramref name="root"/> - DeepClone before keeping or re-parenting one.</summary>
    public static List<Leaf> GetLeaves(JsonNode? root)
    {
        var results = new List<Leaf>();
        if (root is JsonObject obj && obj["PresetList"] is JsonNode presetList)
        {
            Walk(presetList, null, results);
        }
        return results;
    }

    /// <summary>Parses a presets file, or explains why it is not one.</summary>
    public static bool TryRead(string path, out JsonObject? root, out string? error)
    {
        root = null;
        error = null;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error = $"Could not read '{path}': {ex.Message}";
            return false;
        }

        if (root?["PresetList"] is not JsonArray)
        {
            root = null;
            error = "That file is not a HandBrake presets file (it has no PresetList).";
            return false;
        }

        return true;
    }

    private static void Walk(JsonNode? node, string? group, List<Leaf> results)
    {
        if (node is JsonArray array)
        {
            foreach (var child in array) Walk(child, group, results);
            return;
        }

        if (node is not JsonObject obj) return;

        var isFolder = obj["Folder"] is JsonNode folderNode && folderNode.GetValue<bool>();
        var name = obj["PresetName"]?.GetValue<string>();

        if (!isFolder && name is not null)
        {
            results.Add(new Leaf(name, group, obj));
        }

        if (obj["ChildrenArray"] is JsonNode children)
        {
            Walk(children, isFolder ? name : group, results);
        }
    }
}
