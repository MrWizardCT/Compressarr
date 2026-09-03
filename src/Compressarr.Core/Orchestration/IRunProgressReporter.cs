namespace Compressarr.Core.Orchestration;

/// <summary>
/// Structured progress reporting seam, parallel to INotificationService: Core calls into this
/// unconditionally at well-defined points in a run and never knows or cares whether anything is
/// listening. A web host (or any future host) provides a real implementation that tracks
/// in-memory state for its own clients to observe; Desktop-only/test configurations get the
/// no-op default.
/// </summary>
public interface IRunProgressReporter
{
    void RunStarted(string timestamp);

    /// <summary>isResumed reflects whether this lane already had Pending resume entries before
    /// this pass touched it at all - i.e. genuinely left over from an earlier interrupted run,
    /// not the fresh-scan bookkeeping this same pass is about to write for a lane starting
    /// clean. Callers must capture it from resume state read before ConversionOrchestrator
    /// mutates that lane's entries, or it degrades into the same false positive it's meant to
    /// avoid.</summary>
    void LaneStarted(string laneId, string laneDisplayName, bool isResumed);
    void FileStarted(string laneId, int index, int total, string fileName, string? presetName);

    /// <summary>Live progress within the file currently being encoded, parsed from HandBrakeCLI's
    /// own stdout ("Encoding: task 1 of 1, 42.10 % ..."). Fired frequently (roughly once a
    /// second) while a file is converting - implementations should treat this as a cheap
    /// state-update, not something to log every call.</summary>
    void FileProgress(string laneId, double percent, double? fps, string? eta);

    void FileCompleted(string laneId, string fileName, bool success);
    void RunCompleted(int totalFiles);
}
