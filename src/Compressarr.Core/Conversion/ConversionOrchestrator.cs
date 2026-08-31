using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Conversion;

public interface IConversionOrchestrator
{
    /// <summary>Processes every pending file for one lane, strictly sequentially: each file is
    /// fully finished (converted, routed, arr-unmonitored, companions handled) before the next
    /// one starts — no concurrent HandBrakeCLI processes, no job-list polling. This also means
    /// the resume state on disk is always accurate up to the file currently in flight. Ported
    /// from Invoke-CompressarrLaneConversion.</summary>
    Task<IReadOnlyList<ConversionResult>> ProcessLaneAsync(
        LaneConfig lane,
        CompressarrConfig config,
        string logFilePath,
        string timestamp,
        List<ResumeEntry> resumeState,
        string resumeFilePath,
        CancellationToken cancellationToken);
}

public sealed class ConversionOrchestrator : IConversionOrchestrator
{
    private const long BytesPerGb = 1024L * 1024L * 1024L;

    private readonly IPathExpander _pathExpander;
    private readonly IVideoFileScanner _scanner;
    private readonly IHandBrakePresetService _presets;
    private readonly IMetadataService _metadata;
    private readonly IHandBrakeProcessRunner _processRunner;
    private readonly IFileRouter _fileRouter;
    private readonly ICompanionFileService _companionFiles;
    private readonly IArrUnmonitorService _arrUnmonitor;
    private readonly ITrashService _trash;
    private readonly IRunLogger _logger;
    private readonly IResumeStateStore _resumeStore;
    private readonly IRunProgressReporter _progress;
    private readonly IConfigStore _configStore;

    public ConversionOrchestrator(
        IPathExpander pathExpander,
        IVideoFileScanner scanner,
        IHandBrakePresetService presets,
        IMetadataService metadata,
        IHandBrakeProcessRunner processRunner,
        IFileRouter fileRouter,
        ICompanionFileService companionFiles,
        IArrUnmonitorService arrUnmonitor,
        ITrashService trash,
        IRunLogger logger,
        IResumeStateStore resumeStore,
        IRunProgressReporter progress,
        IConfigStore configStore)
    {
        _pathExpander = pathExpander;
        _scanner = scanner;
        _presets = presets;
        _metadata = metadata;
        _processRunner = processRunner;
        _fileRouter = fileRouter;
        _companionFiles = companionFiles;
        _arrUnmonitor = arrUnmonitor;
        _trash = trash;
        _logger = logger;
        _resumeStore = resumeStore;
        _progress = progress;
        _configStore = configStore;
    }

    public async Task<IReadOnlyList<ConversionResult>> ProcessLaneAsync(
        LaneConfig lane,
        CompressarrConfig config,
        string logFilePath,
        string timestamp,
        List<ResumeEntry> resumeState,
        string resumeFilePath,
        CancellationToken cancellationToken)
    {
        var results = new List<ConversionResult>();

        var inputPath = _pathExpander.Expand(lane.Input);
        var outputBase = _pathExpander.Expand(lane.Output);
        var tvShowBasePath = _pathExpander.Expand(lane.TvShowBasePath);
        var movieBasePath = _pathExpander.Expand(lane.MovieBasePath);

        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
        {
            return results;
        }
        if (string.IsNullOrWhiteSpace(outputBase) && !config.Processing.OutSameAsIn)
        {
            _logger.Log($"Lane '{lane.DisplayName}' has no Output folder configured and 'write output to same folder as input' is off - skipping.", LogSeverity.Error);
            return results;
        }

        var hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
        var presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);

        List<FileInfo> videoFiles;
        var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
        if (pending.Count > 0)
        {
            videoFiles = pending.Select(p => new FileInfo(p.FullName)).Where(f => f.Exists).ToList();
        }
        else
        {
            videoFiles = _scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit).ToList();
            foreach (var f in videoFiles)
            {
                resumeState.Add(new ResumeEntry { LaneId = lane.Id, FullName = f.FullName, Status = ResumeStatus.Pending });
            }
            _resumeStore.Save(resumeState, resumeFilePath);
        }

        var fileCount = videoFiles.Count;
        if (fileCount == 0) return results;

        var padSize = fileCount.ToString().Length;

        var i = 0;
        foreach (var file in videoFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            i++;

            var isTv = ContentClassifier.IsTvFile(file.Name);
            var contentType = isTv ? "TV Show" : "Movie";
            var presetName = isTv ? lane.TvPreset : lane.MoviePreset;

            var beginSizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
            var startTime = DateTime.Now;

            _logger.FileStart(lane.DisplayName, i, fileCount, file.Name, beginSizeGb, contentType, presetName);
            _progress.FileStarted(lane.Id, i, fileCount, file.Name);

            var resumeEntry = resumeState.FirstOrDefault(e => e.LaneId == lane.Id && e.FullName == file.FullName);

            if (string.IsNullOrWhiteSpace(presetName))
            {
                _logger.Log($"  No {contentType} preset configured for this lane - skipping.", LogSeverity.Error);
                if (resumeEntry is not null) resumeEntry.Status = ResumeStatus.Error;
                _resumeStore.Save(resumeState, resumeFilePath);

                results.Add(new ConversionResult
                {
                    LaneId = lane.Id,
                    FileName = file.Name,
                    FullName = file.FullName,
                    ContentType = contentType,
                    PresetName = presetName,
                    BeginSizeGb = beginSizeGb,
                    EndSizeGb = 0,
                    Success = false,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                });
                continue;
            }

            var extension = _presets.GetOutputExtension(presetName, presetsPath, out var extensionWarning);
            if (extensionWarning is not null) _logger.Log(extensionWarning, LogSeverity.Error);

            _metadata.ClearTitle(file.FullName);

            var destFolder = config.Processing.OutSameAsIn ? file.DirectoryName! : outputBase;
            Directory.CreateDirectory(destFolder);

            var baseName = Path.GetFileNameWithoutExtension(file.Name);
            var newFileName = Path.Combine(destFolder, baseName + extension);
            var tempFileName = Path.Combine(destFolder, baseName + ".compressarr-" + Guid.NewGuid().ToString("N")[..8] + extension);

            var logName = $"{baseName}_{timestamp}_{i.ToString().PadLeft(padSize, '0')}_HBdetails.txt";
            var detailLogFile = Path.Combine(logFilePath, logName);

            var lastLoggedPercent = -10.0;
            void OnOutputLine(string line)
            {
                var progress = HandBrakeProgressParser.TryParse(line);
                if (progress is null) return;

                _progress.FileProgress(lane.Id, progress.Percent, progress.Fps, progress.Eta);

                // HandBrakeCLI emits a progress line roughly once a second - logging every one of
                // them would flood the recent-log window, so only a real 10% step gets written.
                if (progress.Percent - lastLoggedPercent >= 10.0)
                {
                    lastLoggedPercent = progress.Percent;
                    var etaSuffix = progress.Eta is not null ? $", ETA {progress.Eta}" : "";
                    var fpsSuffix = progress.Fps is not null ? $", {progress.Fps:0.0} fps" : "";
                    _logger.Log($"  {progress.Percent:0.0}%{fpsSuffix}{etaSuffix}");
                }
            }

            var runResult = await _processRunner.RunAsync(hbloc, file.FullName, tempFileName, presetsPath, presetName, config.HandBrake.Options, detailLogFile, OnOutputLine, cancellationToken);
            var endTime = DateTime.Now;

            // Settings are otherwise only read once, at the start of a manual run or for the
            // whole lifetime of the monitoring loop - a change made mid-run (delete-after-convert
            // mode, clear title metadata, a lane's base path, etc.) would otherwise have no effect
            // until the run/loop is restarted. Re-read as soon as HandBrakeCLI is done for this
            // file, before any of the post-processing below, so it takes effect on this file's
            // own routing/cleanup and on every file still to come in this lane. The derived paths
            // computed once at the top of this method must be refreshed too, or the reload above
            // would be a no-op for anything that reads them instead of lane/config directly.
            config = _configStore.Load(AppPaths.GetConfigFilePath());
            lane = config.Lanes.FirstOrDefault(l => l.Id == lane.Id) ?? lane;
            outputBase = _pathExpander.Expand(lane.Output);
            tvShowBasePath = _pathExpander.Expand(lane.TvShowBasePath);
            movieBasePath = _pathExpander.Expand(lane.MovieBasePath);
            hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
            presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
            _metadata.Enabled = config.Processing.ClearTitleMetadata;

            if (runResult.Cancelled)
            {
                try { File.Delete(tempFileName); } catch { }
                _logger.Log($"  Conversion of '{file.Name}' aborted by user.", LogSeverity.Error);
                if (resumeEntry is not null) resumeEntry.Status = ResumeStatus.Error;
                _resumeStore.Save(resumeState, resumeFilePath);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var success = runResult.Success;

            double endSizeGb = 0;
            string? arrStatus = null;
            string? finalFileName = newFileName;

            if (success)
            {
                _metadata.ClearTitle(tempFileName);
                File.Move(tempFileName, newFileName, overwrite: true);
                endSizeGb = Math.Round(new FileInfo(newFileName).Length / (double)BytesPerGb, 3);

                // If source and final destination are the same path (in-place conversion), the
                // rename above already replaced the original with the converted result - there
                // is nothing left to separately delete.
                var sameAsSource = string.Equals(file.FullName, newFileName, StringComparison.OrdinalIgnoreCase);
                if (!sameAsSource && config.Processing.DeleteAfterConvert != DeleteAfterConvertMode.Maintain)
                {
                    if (File.Exists(file.FullName))
                    {
                        var attrs = File.GetAttributes(file.FullName);
                        if (attrs.HasFlag(FileAttributes.ReadOnly))
                        {
                            File.SetAttributes(file.FullName, attrs & ~FileAttributes.ReadOnly);
                        }
                    }
                    _trash.DeleteFile(file.FullName, config.Processing.DeleteAfterConvert);
                }

                string? routedDestPath = null;
                try
                {
                    routedDestPath = _fileRouter.RouteFile(newFileName, isTv, tvShowBasePath, movieBasePath, config.Processing.MoveFiles);
                }
                catch (Exception ex)
                {
                    // The conversion itself already succeeded - a bad/blank base path shouldn't
                    // take down the whole run, just leave the file where HandBrake wrote it.
                    _logger.Log($"  Move skipped: {ex.Message}", LogSeverity.Error);
                }

                // Deliberately before companion-file/folder cleanup below: the rescan should see
                // the source folder still on disk rather than already gone.
                try
                {
                    var arrResult = await _arrUnmonitor.UnmonitorAsync(config, file.Name, isTv);
                    if (arrResult is not null)
                    {
                        _logger.Log($"  {arrResult}");
                        arrStatus = arrResult;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log($"  Arr unmonitor skipped: {ex.Message}", LogSeverity.Error);
                    arrStatus = $"Failed: {ex.Message}";
                }

                if (routedDestPath is not null)
                {
                    finalFileName = routedDestPath;
                    try
                    {
                        var routedDestFolder = Path.GetDirectoryName(routedDestPath)!;
                        _companionFiles.MoveCompanionFiles(file.FullName, file.DirectoryName!, routedDestFolder, config.Processing.VidTypes, config.Processing.DeleteAfterConvert, inputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"  Companion file handling skipped: {ex.Message}", LogSeverity.Error);
                    }
                }

                if (resumeEntry is not null) resumeEntry.Status = ResumeStatus.Completed;
            }
            else
            {
                try { File.Delete(tempFileName); } catch { }
                if (resumeEntry is not null) resumeEntry.Status = ResumeStatus.Error;
            }

            var duration = endTime - startTime;
            if (duration < TimeSpan.Zero) duration = duration.Negate();

            _logger.FileComplete(finalFileName ?? newFileName, beginSizeGb, endSizeGb, duration, success, detailLogFile);
            _progress.FileCompleted(lane.Id, finalFileName ?? newFileName, success);

            _resumeStore.Save(resumeState, resumeFilePath);

            results.Add(new ConversionResult
            {
                LaneId = lane.Id,
                FileName = file.Name,
                FullName = file.FullName,
                NewFileName = finalFileName,
                ContentType = contentType,
                PresetName = presetName,
                BeginSizeGb = beginSizeGb,
                EndSizeGb = endSizeGb,
                Success = success,
                DetailLogFile = detailLogFile,
                StartTime = startTime,
                EndTime = endTime,
                ArrStatus = arrStatus
            });
        }

        return results;
    }
}
