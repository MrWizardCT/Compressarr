using Compressarr.Core.Config;

namespace Compressarr.Core.Conversion;

/// <summary>How two Input folders relate. Lanes scan their Input folder including its subfolders, so a folder
/// that contains another lane's folder is as much a collision as the same folder twice.</summary>
public enum LaneFolderRelation
{
    None,
    /// <summary>The very same folder.</summary>
    Same,
    /// <summary>This lane's folder is inside the other lane's folder.</summary>
    InsideOther,
    /// <summary>The other lane's folder is inside this lane's folder.</summary>
    ContainsOther
}

/// <summary>Another enabled lane whose Input folder collides with this one's.</summary>
public sealed record LaneFolderConflict(LaneConfig Other, LaneFolderRelation Relation)
{
    /// <summary>The sentence shown on the lane's Input field.</summary>
    public string Describe() => Relation switch
    {
        LaneFolderRelation.Same =>
            $"The lane \"{Other.DisplayName}\" is also enabled and watches this same folder. Only one enabled lane can watch a folder - turn one of them off, or choose a different Input folder.",
        LaneFolderRelation.InsideOther =>
            $"This folder is inside the folder the lane \"{Other.DisplayName}\" watches. Lanes scan their Input folder including subfolders, so both would pick up the same files - turn one lane off, or choose a different Input folder.",
        _ =>
            $"The folder the lane \"{Other.DisplayName}\" watches is inside this one. Lanes scan their Input folder including subfolders, so both would pick up the same files - turn one lane off, or choose a different Input folder."
    };
}

public static class LaneFolders
{
    /// <summary>The first other ENABLED lane whose Input folder is the same as, inside, or around this lane's. Only
    /// meaningful for an enabled lane (a disabled one never runs, so it can't collide). Compared on whole folder names
    /// after expanding tokens - "D:\Media Download" and "D:\Media Download Kids" are two different folders, not one
    /// inside the other. A folder that can't be read as a path never collides.</summary>
    public static LaneFolderConflict? FindConflict(LaneConfig lane, IEnumerable<LaneConfig> allLanes, IPathExpander pathExpander)
    {
        if (!lane.Enabled) return null;
        var mine = Normalize(pathExpander.Expand(lane.Input));
        if (mine is null) return null;

        foreach (var other in allLanes)
        {
            if (other.Id == lane.Id || !other.Enabled) continue;
            var theirs = Normalize(pathExpander.Expand(other.Input));
            if (theirs is null) continue;

            var relation = Compare(mine, theirs);
            if (relation != LaneFolderRelation.None) return new LaneFolderConflict(other, relation);
        }

        return null;
    }

    /// <summary>Whether two Input folder settings name the same folder (after expanding tokens, ignoring a trailing
    /// separator and, on Windows, capitalisation).</summary>
    public static bool SameFolder(IPathExpander pathExpander, string a, string b)
    {
        var left = Normalize(pathExpander.Expand(a));
        var right = Normalize(pathExpander.Expand(b));
        return left is not null && right is not null && Compare(left, right) == LaneFolderRelation.Same;
    }

    internal static LaneFolderRelation Compare(string mine, string theirs)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(mine, theirs, comparison)) return LaneFolderRelation.Same;

        var separator = Path.DirectorySeparatorChar.ToString();
        if (mine.StartsWith(theirs + separator, comparison)) return LaneFolderRelation.InsideOther;
        if (theirs.StartsWith(mine + separator, comparison)) return LaneFolderRelation.ContainsOther;
        return LaneFolderRelation.None;
    }

    /// <summary>A full path without a trailing separator, or null for an empty or unusable path.</summary>
    internal static string? Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var full = Path.GetFullPath(path.Trim());
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? full : trimmed; // "/" stays "/"
        }
        catch (Exception)
        {
            return null;
        }
    }
}
