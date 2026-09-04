using Compressarr.Core.Config;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Notifications;

public interface INotificationDispatcher
{
    /// <summary>Fires evt to every configured channel whose Trigger matches this run's outcome.
    /// Must never throw and must never let one channel's failure affect another - each channel is
    /// dispatched independently and any failure (including an unexpected exception from a
    /// misbehaving INotifier) is logged and swallowed, same "must never fail an otherwise-
    /// successful run" contract INotificationService already has.</summary>
    Task DispatchAsync(NotificationSettings settings, NotificationEvent evt, CancellationToken ct = default);
}

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private readonly IReadOnlyDictionary<string, INotifier> _notifiersByType;
    private readonly IRunLogger _logger;

    public NotificationDispatcher(IEnumerable<INotifier> notifiers, IRunLogger logger)
    {
        _notifiersByType = notifiers.ToDictionary(n => n.Type, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationSettings settings, NotificationEvent evt, CancellationToken ct = default)
    {
        foreach (var channel in settings.Channels)
        {
            if (!ShouldFire(channel.Trigger, evt.Outcome)) continue;

            if (!_notifiersByType.TryGetValue(channel.Type, out var notifier))
            {
                _logger.Log($"  Notification channel '{channel.DisplayName}' has unknown type '{channel.Type}' - skipped.", LogSeverity.Error);
                continue;
            }

            try
            {
                var result = await notifier.SendAsync(channel.Settings, evt, ct);
                if (!result.Success)
                {
                    _logger.Log($"  Notification to '{channel.DisplayName}' failed: {result.Message}", LogSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.Log($"  Notification to '{channel.DisplayName}' failed: {ex.Message}", LogSeverity.Error);
            }
        }
    }

    private static bool ShouldFire(NotificationTrigger trigger, NotificationOutcome outcome) => trigger switch
    {
        NotificationTrigger.Always => true,
        NotificationTrigger.OnError => outcome != NotificationOutcome.Success,
        _ => false
    };
}
