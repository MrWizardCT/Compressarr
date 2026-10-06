using Compressarr.Core.Config;
using Compressarr.Core.Conversion;

namespace Compressarr.Core.Queue;

/// <summary>The user chose a different lane for this file to land in, and that lane has since been
/// deleted. Thrown at routing time so the file takes the same path as any other failed move: the
/// finished encode stays safe in Output and is retried on every pass until the destination is fixed.
/// It must never fall back to the file's own lane's library - that is the very library the user was
/// avoiding.</summary>
public sealed class DestinationLaneMissingException : Exception
{
    public string LaneId { get; }

    public DestinationLaneMissingException(string laneId)
        : base($"the destination lane this file was assigned to no longer exists (id {laneId}) - the file stays in Output until the lane is restored or another destination is chosen")
    {
        LaneId = laneId;
    }
}

/// <summary>Where a finished file lands: its own lane's library, or the library of the lane the user
/// assigned it to. The one place that decides this, used only at the routing step - everything else
/// about a file (preset, Output folder, cleanup boundary, *arr handoff) always comes from its own lane.</summary>
public static class LaneDestination
{
    /// <summary>The effective TV/Movie library base paths for this entry, plus the lane it was
    /// redirected to (null when it simply lands in its own lane's library). Throws
    /// <see cref="DestinationLaneMissingException"/> when the assigned lane no longer exists.
    /// homeTv/homeMovie are the entry's own lane's already-expanded paths.</summary>
    public static (string TvShowBasePath, string MovieBasePath, LaneConfig? RedirectedTo) Resolve(
        CompressarrConfig config, IPathExpander pathExpander, ResumeEntry entry, string homeTv, string homeMovie)
    {
        if (string.IsNullOrWhiteSpace(entry.DestinationLaneId) || entry.DestinationLaneId == entry.LaneId)
        {
            return (homeTv, homeMovie, null);
        }

        var destination = config.Lanes.FirstOrDefault(l => l.Id == entry.DestinationLaneId)
            ?? throw new DestinationLaneMissingException(entry.DestinationLaneId);
        return (pathExpander.Expand(destination.TvShowBasePath), pathExpander.Expand(destination.MovieBasePath), destination);
    }
}
