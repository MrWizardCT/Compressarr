using System.Text.Json;

namespace Compressarr.Core.Conversion;

public enum ResumeStatus
{
    Pending,
    Completed,
    Error
}

public sealed class ResumeEntry
{
    public required string LaneId { get; set; }
    public required string FullName { get; set; }
    public ResumeStatus Status { get; set; }
}

public interface IResumeStateStore
{
    List<ResumeEntry> Load(string path);
    void Save(List<ResumeEntry> state, string path);
    void DeleteIfComplete(List<ResumeEntry> state, string path);
}

/// <summary>Ported from Import-CompressarrResumeState/Export-CompressarrResumeState. Written to
/// disk after every single file processed (not batched), so it's always accurate to the file
/// currently in flight — an interrupted run resumes mid-lane instead of rescanning already
/// attempted files.</summary>
public sealed class JsonResumeStateStore : IResumeStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public List<ResumeEntry> Load(string path)
    {
        if (!File.Exists(path)) return new List<ResumeEntry>();

        var raw = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<ResumeEntry>>(raw, Options) ?? new List<ResumeEntry>();
    }

    public void Save(List<ResumeEntry> state, string path)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(state, Options));
    }

    public void DeleteIfComplete(List<ResumeEntry> state, string path)
    {
        var hasOutstanding = state.Any(e => e.Status is ResumeStatus.Pending or ResumeStatus.Error);
        if (!hasOutstanding && File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
