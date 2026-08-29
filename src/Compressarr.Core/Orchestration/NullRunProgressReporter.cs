namespace Compressarr.Core.Orchestration;

/// <summary>Default no-op implementation, same role as NoOpNotificationService — a host that
/// doesn't need progress reporting (tests, a future CLI) gets this until something layers a real
/// implementation in on top via DI registration order.</summary>
public sealed class NullRunProgressReporter : IRunProgressReporter
{
    public void RunStarted(string timestamp) { }
    public void LaneStarted(string laneId, string laneDisplayName) { }
    public void FileStarted(string laneId, int index, int total, string fileName) { }
    public void FileCompleted(string laneId, string fileName, bool success) { }
    public void RunCompleted(int totalFiles) { }
}
