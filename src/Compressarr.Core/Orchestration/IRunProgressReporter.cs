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
    void LaneStarted(string laneId, string laneDisplayName);
    void FileStarted(string laneId, int index, int total, string fileName);

    /// <summary>Live progress within the file currently being encoded, parsed from HandBrakeCLI's
    /// own stdout ("Encoding: task 1 of 1, 42.10 % ..."). Fired frequently (roughly once a
    /// second) while a file is converting - implementations should treat this as a cheap
    /// state-update, not something to log every call.</summary>
    void FileProgress(string laneId, double percent, double? fps, string? eta);

    void FileCompleted(string laneId, string fileName, bool success);
    void RunCompleted(int totalFiles);
}
