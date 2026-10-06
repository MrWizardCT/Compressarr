using System.Text.Json.Nodes;

namespace Compressarr.Core.Presets;

public enum ImportCandidateStatus
{
    /// <summary>Not in Compressarr yet.</summary>
    New,

    /// <summary>The name is already used by a built-in with the same definition - nothing to import.</summary>
    AlreadyBuiltIn,

    /// <summary>The name is already used by a profile (a built-in with a different definition, or one of yours).</summary>
    NameUsed
}

public sealed record ImportCandidate(string Name, string? Group, string Video, string Container, ImportCandidateStatus Status);

public sealed record ImportListing(string Path, IReadOnlyList<ImportCandidate> Candidates, string? Error);

/// <summary>What to do when an imported name is already taken by one of your own profiles. (A clash
/// with a built-in always keeps both: built-ins are locked.)</summary>
public enum ImportConflictMode { KeepBoth, Replace, Skip }

public sealed record ImportOutcome(IReadOnlyList<string> Imported, IReadOnlyList<string> Replaced, IReadOnlyList<string> Skipped);

public interface IHandBrakeProfileImporter
{
    /// <summary>Reads a HandBrake presets file (the app's presets.json, or a "Presets - Export" file)
    /// and lists what could be imported from it. Nothing is changed.</summary>
    ImportListing Read(string path);

    /// <summary>Copies the named presets from the file into the user's profiles. The file is only read.</summary>
    ImportOutcome Import(string path, IReadOnlyCollection<string> names, ImportConflictMode onConflict);
}

public sealed class HandBrakeProfileImporter : IHandBrakeProfileImporter
{
    private readonly IHandBrakeProfileStore _store;

    public HandBrakeProfileImporter(IHandBrakeProfileStore store) => _store = store;

    public ImportListing Read(string path)
    {
        if (!HandBrakePresetFile.TryRead(path, out var root, out var error))
        {
            return new ImportListing(path, Array.Empty<ImportCandidate>(), error);
        }

        var candidates = HandBrakePresetFile.GetLeaves(root).Select(leaf =>
        {
            var existing = _store.Find(leaf.Name);
            var status = existing is null ? ImportCandidateStatus.New
                : existing.IsBuiltIn && JsonNode.DeepEquals(existing.Definition, leaf.Definition) ? ImportCandidateStatus.AlreadyBuiltIn
                : ImportCandidateStatus.NameUsed;
            return new ImportCandidate(
                leaf.Name,
                leaf.Group,
                HandBrakeProfileSummary.Video(leaf.Definition),
                HandBrakeProfileSummary.Container(leaf.FileFormat),
                status);
        }).ToList();

        return new ImportListing(path, candidates, null);
    }

    public ImportOutcome Import(string path, IReadOnlyCollection<string> names, ImportConflictMode onConflict)
    {
        if (!HandBrakePresetFile.TryRead(path, out var root, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var leaves = HandBrakePresetFile.GetLeaves(root)
            .Where(l => wanted.Contains(l.Name))
            .DistinctBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var imported = new List<string>();
        var replaced = new List<string>();
        var skipped = new List<string>();

        // Names taken as the batch is built, so two imports (or one import and the suffix it needs)
        // can't collide with each other.
        var taken = new HashSet<string>(_store.GetAll().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var toAdd = new List<JsonObject>();
        foreach (var leaf in leaves)
        {
            var definition = Clean((JsonObject)leaf.Definition.DeepClone());
            var name = definition["PresetName"]!.GetValue<string>();
            var existing = _store.Find(leaf.Name);

            if (existing is null && !taken.Contains(name))
            {
                taken.Add(name);
                toAdd.Add(definition);
                imported.Add(name);
                continue;
            }

            if (existing is { IsBuiltIn: true } && JsonNode.DeepEquals(existing.Definition, leaf.Definition))
            {
                skipped.Add(leaf.Name); // the identical built-in is already here
                continue;
            }

            if (onConflict == ImportConflictMode.Skip)
            {
                skipped.Add(leaf.Name);
            }
            else if (onConflict == ImportConflictMode.Replace && existing is { IsBuiltIn: false })
            {
                _store.ReplaceUserProfile(existing.Name, definition);
                replaced.Add(existing.Name);
            }
            else
            {
                var unique = UniqueName($"{name} (imported)", taken);
                definition["PresetName"] = unique;
                taken.Add(unique);
                toAdd.Add(definition);
                imported.Add(unique);
            }
        }

        if (toAdd.Count > 0) _store.AddUserProfiles(toAdd);
        return new ImportOutcome(imported, replaced, skipped);
    }

    /// <summary>Makes a preset safe to keep as one of the user's own: a plain leaf (not a folder or the
    /// HandBrake app's default), a custom preset, and a name that works on the command line.</summary>
    private static JsonObject Clean(JsonObject preset)
    {
        var name = preset["PresetName"]!.GetValue<string>();
        // "/" is HandBrake's folder separator in --preset, and the name travels in quotes.
        name = name.Replace('/', '-').Replace('\\', '-').Replace("\"", "").Trim();
        preset["PresetName"] = name;
        preset["Folder"] = false;
        preset["Default"] = false;
        preset["Type"] = 1;
        preset["ChildrenArray"] = new JsonArray();
        return preset;
    }

    private static string UniqueName(string wanted, HashSet<string> taken)
    {
        if (!taken.Contains(wanted)) return wanted;
        for (var n = 2; ; n++)
        {
            var candidate = $"{wanted} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
