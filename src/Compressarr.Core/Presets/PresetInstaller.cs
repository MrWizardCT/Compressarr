using System.Reflection;
using System.Text.Json.Nodes;

namespace Compressarr.Core.Presets;

public interface IPresetInstaller
{
    /// <summary>Compressarr's own bundled presets.json content, as raw JSON text.</summary>
    string GetBundledPresetsJson();

    /// <summary>True if presetsPath does not exist yet - safe to install Compressarr's bundled
    /// presets.json fresh. False if a presets.json is already there (most commonly because the
    /// full HandBrake GUI is installed and populated it) - callers should prompt the user before
    /// calling Merge, since overwriting could discard their existing presets.</summary>
    bool NeedsMergePrompt(string presetsPath);

    /// <summary>Writes Compressarr's bundled presets.json to presetsPath as-is. Only safe to call
    /// when NeedsMergePrompt is false (no file exists yet) or the user has explicitly chosen to
    /// overwrite.</summary>
    void InstallFresh(string presetsPath);

    /// <summary>Merges Compressarr's own presets into an existing presets.json without disturbing
    /// anything else in it: if a "Custom Presets" folder already exists in the tree, Compressarr's
    /// presets are upserted into it by PresetName (replacing a same-named entry, or appended if
    /// not present); otherwise Compressarr's whole "Custom Presets" folder node is appended to the
    /// tree's PresetList. Every other preset - including a same-named "Custom Presets" folder
    /// holding the user's own unrelated presets - is left untouched.</summary>
    void Merge(string presetsPath);
}

public sealed class PresetInstaller : IPresetInstaller
{
    private const string BundledResourceName = "Compressarr.Core.Assets.compressarr-presets.json";
    private const string CompressarrFolderName = "Custom Presets";

    public string GetBundledPresetsJson()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{BundledResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public bool NeedsMergePrompt(string presetsPath) => File.Exists(presetsPath);

    public void InstallFresh(string presetsPath)
    {
        var folder = Path.GetDirectoryName(presetsPath);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

        File.WriteAllText(presetsPath, GetBundledPresetsJson());
    }

    public void Merge(string presetsPath)
    {
        var bundledTree = JsonNode.Parse(GetBundledPresetsJson())!.AsObject();
        var bundledFolder = FindTopLevelFolder(bundledTree, CompressarrFolderName)
            ?? throw new InvalidOperationException($"Bundled presets.json has no '{CompressarrFolderName}' folder - nothing to merge.");
        var bundledChildren = bundledFolder["ChildrenArray"]!.AsArray();

        var existingTree = JsonNode.Parse(File.ReadAllText(presetsPath))!.AsObject();
        if (existingTree["PresetList"] is not JsonArray presetList)
        {
            presetList = new JsonArray();
            existingTree["PresetList"] = presetList;
        }

        var existingFolder = FindTopLevelFolder(existingTree, CompressarrFolderName);
        if (existingFolder is null)
        {
            presetList.Add(bundledFolder.DeepClone());
        }
        else
        {
            if (existingFolder["ChildrenArray"] is not JsonArray existingChildren)
            {
                existingChildren = new JsonArray();
                existingFolder["ChildrenArray"] = existingChildren;
            }

            foreach (var bundledPreset in bundledChildren)
            {
                var presetName = bundledPreset!["PresetName"]!.GetValue<string>();
                var existingIndex = -1;
                for (var i = 0; i < existingChildren.Count; i++)
                {
                    if (existingChildren[i]?["PresetName"]?.GetValue<string>() == presetName)
                    {
                        existingIndex = i;
                        break;
                    }
                }

                if (existingIndex >= 0)
                {
                    existingChildren[existingIndex] = bundledPreset.DeepClone();
                }
                else
                {
                    existingChildren.Add(bundledPreset.DeepClone());
                }
            }
        }

        File.WriteAllText(presetsPath, existingTree.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private static JsonObject? FindTopLevelFolder(JsonObject tree, string folderName)
    {
        if (tree["PresetList"] is not JsonArray presetList) return null;

        foreach (var node in presetList)
        {
            if (node is JsonObject obj &&
                obj["PresetName"]?.GetValue<string>() == folderName &&
                obj["Folder"]?.GetValue<bool>() == true)
            {
                return obj;
            }
        }

        return null;
    }
}
