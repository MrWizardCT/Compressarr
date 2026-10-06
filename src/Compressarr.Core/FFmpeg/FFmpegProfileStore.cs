using System.Reflection;
using System.Text;
using System.Text.Json;
using Compressarr.Core.Config;

namespace Compressarr.Core.FFmpeg;

/// <summary>
/// Compressarr's ffmpeg profile catalog: the built-ins that ship inside the app (locked) plus the
/// user's own, kept in %AppData%\Compressarr\Profiles\ffmpeg-profiles.json (included in backups and
/// in Export/Import config). Names are unique within ffmpeg's own catalog - a HandBrake profile may
/// share a name (the built-ins deliberately do), since a lane only ever picks from its own
/// encoder's list.
/// </summary>
public interface IFFmpegProfileStore
{
    /// <summary>Built-ins first, then the user's profiles in stored order. Copies - changing one does nothing.</summary>
    IReadOnlyList<FFmpegProfile> GetAll();

    IReadOnlyList<FFmpegProfile> GetBuiltIns();

    FFmpegProfile? Find(string name);

    /// <summary>Set when the user's file exists but could not be read - only the built-ins are then
    /// offered, and nothing will overwrite that file.</summary>
    string? UserFileError { get; }

    /// <summary>Appends profiles to the user's file. Names must be non-empty and unique
    /// (case-insensitively) among everything; any clash throws InvalidOperationException and nothing is written.</summary>
    void AddUserProfiles(IEnumerable<FFmpegProfile> profiles);

    /// <summary>Replaces one of the user's profiles (the new one may carry a new name), keeping its place.</summary>
    void ReplaceUserProfile(string currentName, FFmpegProfile profile);

    void RemoveUserProfile(string name);
}

public sealed class FFmpegProfileStore : IFFmpegProfileStore
{
    private const string BundledResourceName = "Compressarr.Core.Assets.ffmpeg-presets.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private sealed class FileShape
    {
        public List<FFmpegProfile> Profiles { get; set; } = new();
    }

    private readonly object _gate = new();
    private readonly Lazy<List<FFmpegProfile>> _bundled = new(LoadBundled);

    private (DateTime WriteUtc, long Length) _stamp;
    private List<FFmpegProfile>? _userCache;
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

    public IReadOnlyList<FFmpegProfile> GetBuiltIns() => _bundled.Value.Select(p => p.Clone()).ToList();

    public IReadOnlyList<FFmpegProfile> GetAll()
    {
        lock (_gate)
        {
            return _bundled.Value.Concat(LoadUserLocked()).Select(p => p.Clone()).ToList();
        }
    }

    public FFmpegProfile? Find(string name) =>
        GetAll().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public void AddUserProfiles(IEnumerable<FFmpegProfile> profiles)
    {
        lock (_gate)
        {
            var existing = LoadUserLocked();
            EnsureWritableLocked();

            var taken = new HashSet<string>(_bundled.Value.Concat(existing).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            var added = new List<FFmpegProfile>();
            foreach (var profile in profiles)
            {
                var name = profile.Name?.Trim();
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("A profile needs a name.");
                if (!taken.Add(name)) throw new InvalidOperationException($"A profile named '{name}' already exists.");

                var copy = profile.Clone();
                copy.Name = name;
                copy.IsBuiltIn = false;
                added.Add(copy);
            }

            if (added.Count == 0) return;
            WriteLocked(existing.Concat(added).ToList());
        }
    }

    public void ReplaceUserProfile(string currentName, FFmpegProfile profile)
    {
        lock (_gate)
        {
            var existing = LoadUserLocked();
            EnsureWritableLocked();

            var index = existing.FindIndex(p => string.Equals(p.Name, currentName, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw MissingOrLocked(currentName, "changed");

            var newName = profile.Name?.Trim();
            if (string.IsNullOrWhiteSpace(newName)) throw new InvalidOperationException("A profile needs a name.");
            var clash = _bundled.Value.Concat(existing.Where((_, i) => i != index))
                .Any(p => string.Equals(p.Name, newName, StringComparison.OrdinalIgnoreCase));
            if (clash) throw new InvalidOperationException($"A profile named '{newName}' already exists.");

            var copy = profile.Clone();
            copy.Name = newName;
            copy.IsBuiltIn = false;
            var all = existing.ToList();
            all[index] = copy;
            WriteLocked(all);
        }
    }

    public void RemoveUserProfile(string name)
    {
        lock (_gate)
        {
            var existing = LoadUserLocked();
            EnsureWritableLocked();

            var index = existing.FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw MissingOrLocked(name, "deleted");

            WriteLocked(existing.Where((_, i) => i != index).ToList());
        }
    }

    private InvalidOperationException MissingOrLocked(string name, string verb) =>
        _bundled.Value.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            ? new InvalidOperationException($"'{name}' is a built-in profile and can't be {verb}. Duplicate it to make your own.")
            : new InvalidOperationException($"There is no profile named '{name}'.");

    private void EnsureWritableLocked()
    {
        if (_userError is not null)
        {
            throw new InvalidOperationException($"Your ffmpeg profile file could not be read, so nothing was changed: {_userError}");
        }
    }

    private List<FFmpegProfile> LoadUserLocked()
    {
        var path = AppPaths.GetFFmpegProfilesFilePath();
        if (!File.Exists(path))
        {
            _stamp = default;
            _userError = null;
            return _userCache = new List<FFmpegProfile>();
        }

        var info = new FileInfo(path);
        var stamp = (info.LastWriteTimeUtc, info.Length);
        if (_userCache is not null && stamp == _stamp) return _userCache;

        _stamp = stamp;
        try
        {
            var shape = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(path), Options)
                ?? throw new JsonException("The file is empty.");
            _userError = null;
            return _userCache = shape.Profiles.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _userError = $"Could not read '{path}': {ex.Message}";
            return _userCache = new List<FFmpegProfile>();
        }
    }

    private void WriteLocked(List<FFmpegProfile> profiles)
    {
        var path = AppPaths.GetFFmpegProfilesFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new FileShape { Profiles = profiles }, Options));

        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }

        _userCache = null;
    }

    private static List<FFmpegProfile> LoadBundled()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{BundledResourceName}' not found.");
        var shape = JsonSerializer.Deserialize<FileShape>(stream, Options)!;
        foreach (var profile in shape.Profiles) profile.IsBuiltIn = true;
        return shape.Profiles;
    }
}
