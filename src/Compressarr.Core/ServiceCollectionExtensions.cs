using Compressarr.Core.Arr;
using Compressarr.Core.Backup;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Dependencies;
using Compressarr.Core.Diagnostics;
using Compressarr.Core.Logging;
using Compressarr.Core.Notifications;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Reporting;
using Compressarr.Core.Routing;
using Compressarr.Core.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace Compressarr.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers every Compressarr.Core service behind its interface. Any host
    /// (Desktop today; a web server or CLI in a later phase) wires this up the same way -
    /// nothing here is Avalonia- or platform-specific.</summary>
    public static IServiceCollection AddCompressarrCore(this IServiceCollection services)
    {
        services.AddHttpClient();

        services.AddSingleton<IConfigStore, JsonConfigStore>();
        services.AddSingleton<IPathExpander, PathExpander>();
        services.AddSingleton<IHandBrakePresetService, HandBrakePresetService>();
        services.AddSingleton<IPresetInstaller, PresetInstaller>();
        services.AddSingleton<IHandBrakeInstaller, HandBrakeInstaller>();

        services.AddSingleton<IVideoFileScanner, VideoFileScanner>();
        services.AddSingleton<IActiveHandBrakeProcess, ActiveHandBrakeProcess>();
        services.AddSingleton<IHandBrakeProcessRunner, HandBrakeProcessRunner>();
        services.AddSingleton<IMetadataService, MetadataService>();
        services.AddSingleton<IResumeStateStore, JsonResumeStateStore>();
        services.AddSingleton<IFileRouter, FileRouter>();
        services.AddSingleton<ICompanionFileService, CompanionFileService>();
        services.AddSingleton<ITrashService>(_ => TrashServiceFactory.CreateForCurrentPlatform());

        services.AddSingleton<IArrClient, ArrClient>();
        services.AddSingleton<IArrUnmonitorService, ArrUnmonitorService>();

        services.AddSingleton<IRunLogger, FileRunLogger>();
        services.AddSingleton<IRunHistoryStore, CsvRunHistoryStore>();
        services.AddSingleton<IHistoryRollupCalculator, HistoryRollupCalculator>();
        services.AddSingleton<IWebHistoryRollupCalculator, WebHistoryRollupCalculator>();
        services.AddSingleton<ICpuUsageSampler>(_ => CpuUsageSamplerFactory.CreateForCurrentPlatform());
        services.AddSingleton<IFileSystemBrowser, FileSystemBrowser>();

        services.AddSingleton<IHtmlReportGenerator, HtmlReportGenerator>();
        services.AddSingleton<IReportLauncher, ReportLauncher>();
        services.AddSingleton<INotificationService, NoOpNotificationService>();
        services.AddSingleton<IRunProgressReporter, NullRunProgressReporter>();

        services.AddSingleton<IWebhookSender, WebhookSender>();
        services.AddSingleton<INotifier, WebhookNotifier>();
        services.AddSingleton<INotifier, TelegramNotifier>();
        services.AddSingleton<INotifier, PushoverNotifier>();
        services.AddSingleton<INotifier, NtfyNotifier>();
        services.AddSingleton<INotifier, GotifyNotifier>();
        services.AddSingleton<INotifier, NotifiarrNotifier>();
        services.AddSingleton<INotifier, IftttNotifier>();
        services.AddSingleton<INotificationDispatcher, NotificationDispatcher>();

        services.AddSingleton<IActiveRunController, ActiveRunController>();
        services.AddSingleton<IConversionOrchestrator, ConversionOrchestrator>();
        services.AddSingleton<IRunOrchestrator, RunOrchestrator>();
        services.AddSingleton<IRunLoopController, RunLoopController>();
        services.AddSingleton<IStartupRegistrationService>(_ => StartupRegistrationServiceFactory.CreateForCurrentPlatform());

        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IBackupScheduler, BackupScheduler>();

        return services;
    }
}
