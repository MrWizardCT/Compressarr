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
    void FileCompleted(string laneId, string fileName, bool success);
    void RunCompleted(int totalFiles);
}
