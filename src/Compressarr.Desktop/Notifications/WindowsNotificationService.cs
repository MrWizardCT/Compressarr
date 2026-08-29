#if WINDOWS
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Compressarr.Core.Notifications;

namespace Compressarr.Desktop.Notifications;

/// <summary>Real Windows toast implementation using Windows.UI.Notifications directly - this
/// needs a Windows-flavored TFM to reference the raw .winmd metadata, which Compressarr.Core
/// can't afford (it must stay buildable on every platform). Compressarr.Desktop can afford it
/// now that it's a thin per-platform tray host rather than a single cross-platform UI project -
/// this file only compiles into the net10.0-windows10.0.19041.0 build (see the csproj's
/// Compile Remove under plain net10.0). Registered to override Core's NoOpNotificationService
/// in App.axaml.cs's composition root.</summary>
public sealed class WindowsNotificationService : INotificationService
{
    private const string AppId = "Compressarr.Compressarr";

    public void Notify(string title, string message, string? launchPath)
    {
        try
        {
            var xml = new XmlDocument();
            var launchAttr = launchPath is not null ? $" launch=\"{new Uri(launchPath).AbsoluteUri}\" activationType=\"protocol\"" : "";
            xml.LoadXml($"""
                <toast{launchAttr}>
                  <visual>
                    <binding template="ToastGeneric">
                      <text>{System.Net.WebUtility.HtmlEncode(title)}</text>
                      <text>{System.Net.WebUtility.HtmlEncode(message)}</text>
                    </binding>
                  </visual>
                </toast>
                """);

            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
        }
        catch
        {
            // Best-effort only - a broken/unavailable toast subsystem must never fail a run.
        }
    }
}
#endif
