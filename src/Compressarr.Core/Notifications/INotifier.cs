namespace Compressarr.Core.Notifications;

/// <summary>Describes one configurable field a channel type needs (a webhook URL, a bot token, a
/// chat ID, etc.) - the web UI fetches these via GET /api/notifications/types and renders whatever
/// each registered INotifier declares, so adding a new channel type never needs new frontend
/// code. InputType is "text"/"password"/"textarea"/"select" - Options is only meaningful (and
/// required) for "select".</summary>
public sealed record NotifierField(string Key, string Label, string InputType, bool Required, bool Secret = false, IReadOnlyList<string>? Options = null);

public sealed record NotifyResult(bool Success, string Message);

public enum NotificationOutcome
{
    Success,
    Warning,
    Error
}

/// <summary>What actually happened this run, in a form every notifier's SendAsync can lay out
/// however suits its own channel - deliberately structured rather than a pre-formatted string,
/// same reasoning INotificationService.RunCompletionSummary already uses.</summary>
public sealed record NotificationEvent(
    NotificationOutcome Outcome,
    string Title,
    string Body,
    int TotalFiles,
    double SavedGb,
    TimeSpan Duration,
    string? ReportPath);

/// <summary>One pluggable notification channel type (Discord, Slack, a generic webhook, etc).
/// Each implementation is stateless and registered as a singleton in DI, resolved as
/// IEnumerable&lt;INotifier&gt; by NotificationDispatcher and indexed by Type against whichever
/// channels the user has configured.</summary>
public interface INotifier
{
    /// <summary>Stable id matching NotificationChannel.Type in config - "webhook", "discord", etc.
    /// Never shown to the user directly; DisplayName is.</summary>
    string Type { get; }

    string DisplayName { get; }

    /// <summary>The config fields this channel type needs, driving both the "Add Channel" field
    /// picker and each existing channel's own edit form.</summary>
    IReadOnlyList<NotifierField> Fields { get; }

    /// <summary>Sends a real notification for a completed run. Must never throw - a failed/
    /// misconfigured channel must never fail an otherwise-successful run or block another
    /// channel; NotificationDispatcher treats any thrown exception as a failed NotifyResult
    /// anyway, but implementations should still catch and report a real message where possible.</summary>
    Task<NotifyResult> SendAsync(IReadOnlyDictionary<string, string> settings, NotificationEvent evt, CancellationToken ct);

    /// <summary>Sends a short test message using whatever's currently typed into the channel's
    /// fields, even if not yet saved - same "test what's on screen, not what's on disk" behavior
    /// /api/arr/test already has for Sonarr/Radarr.</summary>
    Task<NotifyResult> TestAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct);
}
