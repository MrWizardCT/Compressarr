using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compressarr.Core.Config;

namespace Compressarr.Core.Presets;

/// <summary>One HandBrake profile Compressarr knows about: a built-in that ships inside the app
/// (locked), or one of the user's own (created, duplicated or imported). Definition is the full
/// HandBrake preset node, kept exactly as stored so nothing the editor doesn't show is lost.</summary>
public sealed record HandBrakeProfile(string Name, string? FileFormat, bool IsBuiltIn, JsonObject Definition);

/// <summary>
/// Compressarr's own HandBrake profile catalog - the only preset source Compressarr ever uses.
/// Built-ins are embedded in the app; the user's profiles live in
/// %AppData%\Compressarr\Profiles\handbrake-profiles.json and are backed up with the rest of the
/// settings. HandBrake itself is handed one generated file (handbrake-active.json: built-ins plus
/// the user's profiles) through a single --preset-import-file; the HandBrake app's own presets.json
/// is never read at run time and never written.
/// </summary>
public interface IHandBrakeProfileStore
{
    /// <summary>Built-ins first, then the user's profiles in stored order.</summary>
    IReadOnlyList<HandBrakeProfile> GetAll();

    HandBrakeProfile? Find(string name);

    /// <summary>The profiles that ship inside the app (locked; always the same for a given version).</summary>
    IReadOnlyList<HandBrakeProfile> GetBuiltIns();

    /// <summary>Set when the user's profile file exists but could not be read - the profiles in it
    /// are then not offered (and no write will overwrite it). Null when everything is fine.</summary>
    string? UserFileError { get; }

    /// <summary>Appends profiles to the user's file. Names must be non-empty and unique
    /// (case-insensitively) across the built-ins and the user's existing profiles; any clash throws
    /// InvalidOperationException and nothing is written.</summary>
    void AddUserProfiles(IEnumerable<JsonObject> definitions);

    /// <summary>Makes sure handbrake-active.json matches the built-ins plus the user's profiles
    /// and returns its path. Generated deterministically in memory and compared with what is on
    /// disk - the file is only rewritten (atomically: temp file, then rename) when it is missing or
    /// different, so a normal start or encode touches nothing.</summary>
    string EnsureActiveFile();
}

public sealed class HandBrakeProfileStore : IHandBrakeProfileStore
{
    internal const string BuiltInFolderName = "Compressarr (built-in)";
    internal const string UserFolderName = "Yours";
    private const string BundledResourceName = "Compressarr.Core.Assets.compressarr-presets.json";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _gate = new();
    private readonly Lazy<(List<HandBrakeProfile> Profiles, JsonObject Header)> _bundled = new(LoadBundled);

    private (DateTime WriteUtc, long Length) _userStamp;
    private List<HandBrakeProfile>? _userCache;
    private string? _userError;

    public string? UserFileError
    {
        get
        {
            lock (_gate)
            {
                LoadUserLocked();
                return _userError;
            }
        }
    }

    public IReadOnlyList<HandBrakeProfile> GetBuiltIns() => _bundled.Value.Profiles;

    public IReadOnlyList<HandBrakeProfile> GetAll()
    {
        lock (_gate)
        {
            return _bundled.Value.Profiles.Concat(LoadUserLocked()).ToList();
        }
    }

    public HandBrakeProfile? Find(string name) =>
        GetAll().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void AddUserProfiles(IEnumerable<JsonObject> definitions)
    {
        lock (_gate)
        {
            var existing = LoadUserLocked();
            if (_userError is not null)
            {
                throw new InvalidOperationException($"Your profile file could not be read, so nothing was changed: {_userError}");
            }

            var taken = new HashSet<string>(_bundled.Value.Profiles.Concat(existing).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var added = new List<JsonObject>();
            foreach (var definition in definitions)
            {
                var name = definition["PresetName"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidOperationException("A profile needs a name.");
                }
                if (!taken.Add(name))
                {
                    throw new InvalidOperationException($"A profile named '{name}' already exists.");
                }
                added.Add((JsonObject)definition.DeepClone());
            }

            if (added.Count == 0) return;

            var all = existing.Select(p => (JsonNode)p.Definition.DeepClone()).Concat(added).ToList();
            WriteUserFileLocked(all);
            EnsureActiveFileLocked();
        }
    }

    public string EnsureActiveFile()
    {
        lock (_gate)
        {
            return EnsureActiveFileLocked();
        }
    }

    private string EnsureActiveFileLocked()
    {
        var path = AppPaths.GetHandBrakeActivePresetsFilePath();
        var desired = Encoding.UTF8.GetBytes(BuildActiveJson(LoadUserLocked()));

        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(desired))
        {
            return path;
        }

        WriteAtomically(path, desired);
        return path;
    }

    /// <summary>The generated file: a built-in folder, and a "Yours" folder when the user has any
    /// profiles. Same input always yields byte-identical output (insertion-ordered nodes, fixed
    /// formatting), which is what makes the compare-then-write check meaningful.</summary>
    private string BuildActiveJson(List<HandBrakeProfile> user)
    {
        var folders = new JsonArray { Folder(BuiltInFolderName, _bundled.Value.Profiles) };
        if (user.Count > 0) folders.Add(Folder(UserFolderName, user));

        var root = new JsonObject { ["PresetList"] = folders };
        foreach (var (key, value) in _bundled.Value.Header) root[key] = value?.DeepClone();
        return root.ToJsonString(WriteOptions);
    }

    private static JsonObject Folder(string name, IEnumerable<HandBrakeProfile> profiles) => new()
    {
        ["ChildrenArray"] = new JsonArray(profiles.Select(p => p.Definition.DeepClone()).ToArray()),
        ["Folder"] = true,
        ["PresetName"] = name,
        ["Type"] = 1
    };

    private List<HandBrakeProfile> LoadUserLocked()
    {
        var path = AppPaths.GetHandBrakeProfilesFilePath();
        if (!File.Exists(path))
        {
            _userStamp = default;
            _userError = null;
            return _userCache = new List<HandBrakeProfile>();
        }

        var info = new FileInfo(path);
        var stamp = (info.LastWriteTimeUtc, info.Length);
        if (_userCache is not null && stamp == _userStamp) return _userCache;

        _userStamp = stamp;
        if (!HandBrakePresetFile.TryRead(path, out var root, out var error))
        {
            _userError = error;
            return _userCache = new List<HandBrakeProfile>();
        }

        _userError = null;
        return _userCache = HandBrakePresetFile.GetLeaves(root)
            .Select(l => new HandBrakeProfile(l.Name, l.FileFormat, false, l.Definition))
            .ToList();
    }

    private void WriteUserFileLocked(List<JsonNode> leaves)
    {
        var root = new JsonObject { ["PresetList"] = new JsonArray(leaves.ToArray()) };
        foreach (var (key, value) in _bundled.Value.Header) root[key] = value?.DeepClone();

        WriteAtomically(AppPaths.GetHandBrakeProfilesFilePath(), Encoding.UTF8.GetBytes(root.ToJsonString(WriteOptions)));
        _userCache = null;
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);

        // The target can be open for a moment (HandBrake reading it at the start of an encode, a
        // backup zipping it); a short retry rides that out instead of failing a profile save.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    private static (List<HandBrakeProfile>, JsonObject) LoadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{BundledResourceName}' not found.");
        using var reader = new StreamReader(stream);
        var root = JsonNode.Parse(reader.ReadToEnd())!.AsObject();

        var profiles = HandBrakePresetFile.GetLeaves(root)
            .Select(l => new HandBrakeProfile(l.Name, l.FileFormat, true, (JsonObject)l.Definition.DeepClone()))
            .ToList();

        // Everything except the preset list itself (the Version* fields) is carried into every file
        // Compressarr writes, so HandBrake reads them as the same preset-format version.
        var header = new JsonObject();
        foreach (var (key, value) in root)
        {
            if (key != "PresetList") header[key] = value?.DeepClone();
        }

        return (profiles, header);
    }
}
