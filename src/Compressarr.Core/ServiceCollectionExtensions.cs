using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;
using Compressarr.Core.Notifications;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Reporting;
using Compressarr.Core.Routing;
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

        services.AddSingleton<IVideoFileScanner, VideoFileScanner>();
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

        services.AddSingleton<IHtmlReportGenerator, HtmlReportGenerator>();
        services.AddSingleton<IReportLauncher, ReportLauncher>();
        services.AddSingleton<INotificationService, NoOpNotificationService>();

        services.AddSingleton<IConversionOrchestrator, ConversionOrchestrator>();
        services.AddSingleton<IRunOrchestrator, RunOrchestrator>();

        return services;
    }
}
