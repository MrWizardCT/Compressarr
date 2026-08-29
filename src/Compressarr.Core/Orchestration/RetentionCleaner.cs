using Compressarr.Core.Config;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Orchestration;

/// <summary>Ported from Remove-CompressarrOldFiles. Deletes/recycles files matching the given
/// extensions in a folder that are older than retentionDays. Shared by the Logs and Reports
/// folders, both keyed off logging.retentionDays.</summary>
public static class RetentionCleaner
{
    public static void CleanUp(ITrashService trash, string path, IReadOnlyList<string> extensions, int retentionDays, string label)
    {
        if (!Directory.Exists(path)) return;

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        var extensionSet = extensions.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var oldFiles = Directory.EnumerateFiles(path)
            .Select(f => new FileInfo(f))
            .Where(f => extensionSet.Contains(f.Extension) && f.CreationTime < cutoff)
            .ToList();

        foreach (var file in oldFiles)
        {
            trash.DeleteFile(file.FullName, DeleteAfterConvertMode.Recycle);
        }
    }
}
