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
    // A single-segment AUMID, deliberately - this app has no Start Menu shortcut registering
    // "Compressarr.Compressarr" with a DisplayName, so Windows falls back to rendering the raw
    // AUMID as the toast's app-name header, splitting on '.'. Two identical segments
    // ("Compressarr" + "Compressarr") rendered as the literal doubled "Compressarr Compressarr"
    // that prompted this fix - a single segment has nothing to split.
    private const string AppId = "Compressarr";

    // AppContext.BaseDirectory is the running exe's own folder regardless of how it was
    // launched - matches the same reasoning App.axaml.cs uses for ContentRootPath. The PNG is
    // copied there as a loose file (see the csproj) specifically so it has a real path to hand
    // the toast, since an embedded avares:// Avalonia resource can't be used as a toast image.
    private static readonly string LogoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "CompressarrLogo.png");

    public void NotifyRunComplete(RunCompletionSummary summary, string? launchPath)
    {
        try
        {
            var savingsGb = Math.Round(summary.BeginSizeGb - summary.EndSizeGb, 3);
            var savingsPct = summary.BeginSizeGb > 0
                ? Math.Round(100 - (summary.EndSizeGb / summary.BeginSizeGb) * 100, 1)
                : 0;
            // Toast <progress value> must be 0..1 - a pathological run where output ends up
            // larger than input (a bad preset, an already-compressed source) would otherwise
            // send a negative value, which the toast would simply refuse to render.
            var progressFraction = Math.Clamp(savingsPct / 100.0, 0.0, 1.0);
            var fileWord = summary.TotalFiles == 1 ? "file" : "files";

            var xml = new XmlDocument();
            var launchAttr = launchPath is not null ? $" launch=\"{new Uri(launchPath).AbsoluteUri}\" activationType=\"protocol\"" : "";
            var logoElement = File.Exists(LogoPath)
                ? $"""<image placement="appLogoOverride" hint-crop="circle" src="{new Uri(LogoPath).AbsoluteUri}" />"""
                : "";

            var headline = Encode($"{summary.TotalFiles} {fileWord} processed");
            var detail = Encode($"{summary.BeginSizeGb:0.00} GB → {summary.EndSizeGb:0.00} GB · {summary.Duration.Hours}h {summary.Duration.Minutes}m {summary.Duration.Seconds}s");
            var progressStatus = Encode($"{savingsPct:0.#}% smaller");
            var progressValueOverride = Encode($"Saved {savingsGb:0.00} GB");

            xml.LoadXml($"""
                <toast{launchAttr}>
                  <visual>
                    <binding template="ToastGeneric">
                      {logoElement}
                      <text>Compressarr</text>
                      <text hint-style="subtitle">{headline}</text>
                      <text hint-style="captionSubtle">{detail}</text>
                      <progress value="{progressFraction.ToString(System.Globalization.CultureInfo.InvariantCulture)}" valueStringOverride="{progressValueOverride}" title="Space Saved" status="{progressStatus}" />
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

    private static string Encode(string text) => System.Net.WebUtility.HtmlEncode(text);
}
#endif
