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

    /// <summary>Fires a best-effort OS notification summarizing a digest period (a day or a week
    /// of runs) rather than one run - a separate method, not an overload of NotifyRunComplete,
    /// since a digest has no single Duration and its header should read "Daily Digest"/"Weekly
    /// Digest" rather than the generic per-run title. periodLabel is exactly that header text.
    /// launchPath mirrors NotifyRunComplete's own - clicking the toast opens it (the web UI's
    /// History page, for a digest). NOT OPTIONAL IN PRACTICE despite the nullable type: confirmed
    /// live 2026-09-07 that a toast with no launch target (making it non-activatable) never
    /// appears at all on this unpackaged Win32 app's AUMID - not as a banner, not in Action
    /// Center, no exception either. Every real per-run toast the user has ever seen work always
    /// carried a real launchPath; the digest toast didn't originally, and was invisible until this
    /// was added. Must never throw, same contract as NotifyRunComplete.</summary>
    void NotifyDigestComplete(DigestSummary summary, string periodLabel, string? launchPath);
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
    public void NotifyDigestComplete(DigestSummary summary, string periodLabel, string? launchPath) { }
}
