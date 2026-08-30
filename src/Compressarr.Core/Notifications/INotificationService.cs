namespace Compressarr.Core.Notifications;

public sealed record RunCompletionSummary(int TotalFiles, double BeginSizeGb, double EndSizeGb, TimeSpan Duration);

public interface INotificationService
{
    /// <summary>Fires a best-effort OS notification summarizing a completed run. Implementations
    /// must never throw — a failed/unsupported notification must never fail an otherwise-
    /// successful run, matching v1's toast call being wrapped in try/catch and only fired when
    /// files were processed. Structured (rather than a pre-formatted string) so each
    /// implementation can lay the numbers out however suits its platform's notification
    /// system.</summary>
    void NotifyRunComplete(RunCompletionSummary summary, string? launchPath);
}

/// <summary>Default fallback everywhere for Phase 1. A native Windows toast (Windows.UI.Notifications)
/// was attempted here but requires referencing raw .winmd metadata, which .NET 5+ only supports
/// under a Windows-flavored TargetFramework (net10.0-windows10.0.xxxx) — incompatible with
/// Compressarr.Core's requirement to stay buildable on every platform without a TFM split. True
/// notifications (native Windows toast via a windows-flavored Desktop build, libnotify/D-Bus on
/// Linux, UNUserNotificationCenter/osascript on macOS) are deferred past Phase 1 — this interface
/// seam is what lets any of them land later without touching callers.</summary>
public sealed class NoOpNotificationService : INotificationService
{
    public void NotifyRunComplete(RunCompletionSummary summary, string? launchPath) { }
}
