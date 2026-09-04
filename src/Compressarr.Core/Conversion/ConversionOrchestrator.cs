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

        // A Pending or Error entry whose source file is gone (e.g. removed by hand, or already
        // handled by Sonarr/Radarr, between runs) can never be resumed or retried - drop it
        // rather than let it block this lane forever. Without this, a lane with only dead pending
        // entries falls into the branch below, finds nothing to process, and returns without ever
        // falling back to scanning Input for genuinely new files. Error entries need the same
        // treatment for a different reason: unlike Pending, they're never re-checked by anything
        // else once their source disappears, so a single dead Error entry pins resumeState.Count
        // and RunOrchestrator's "stillOutstanding" check permanently - the whole resume.json
        // (including every already-Completed entry sitting alongside it) never gets cleaned up,
        // and "Resuming previous incomplete run" keeps logging that inflated count on every pass.
        var deadEntries = resumeState.Where(e => e.LaneId == lane.Id
            && e.Status is ResumeStatus.Pending or ResumeStatus.Error
            && !File.Exists(e.FullName)).ToList();
        if (deadEntries.Count > 0)
        {
            foreach (var dead in deadEntries)
            {
                _logger.Log($"Resume entry for '{dead.FullName}' no longer exists - removing it.", LogSeverity.Error);
                resumeState.Remove(dead);
            }
            _resumeStore.Save(resumeState, resumeFilePath);
        }

        // MoveFailed entries from a prior pass: the encode already succeeded, only the move needs
        // retrying - no re-encode, just RouteFile again against the file HandBrake already wrote.
        // Runs before the normal pending-or-scan branch below so a file that's ready to be filed
        // gets filed before this pass starts encoding anything new. A dead entry here (the encoded
        // file itself is gone - moved/deleted by hand) is dropped the same way deadEntries above
        // drops a dead Pending/Error entry, checked against EncodedFilePath rather than FullName
        // since FullName is the original Input path, which delete-after-convert may have already
        // removed even though the entry is perfectly healthy.
        var moveFailedEntries = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.MoveFailed).ToList();
        foreach (var entry in moveFailedEntries)
        {
            if (entry.EncodedFilePath is null || !File.Exists(entry.EncodedFilePath))
            {
                _logger.Log($"Resume entry for '{entry.FullName}' (awaiting move retry) no longer has its encoded file - removing it.", LogSeverity.Error);
                resumeState.Remove(entry);
                _resumeStore.Save(resumeState, resumeFilePath);
                continue;
            }

            var retryFileName = Path.GetFileName(entry.EncodedFilePath);
            var retryIsTv = ContentClassifier.IsTvFile(retryFileName);
            try
            {
                var retryDestPath = _fileRouter.RouteFile(entry.EncodedFilePath, retryIsTv, tvShowBasePath, movieBasePath, config.Processing.MoveFiles, config.Processing.OnDestinationCollision);
                entry.Status = ResumeStatus.Completed;
                entry.EncodedFilePath = null;
                _logger.Log($"  Retried move for '{retryFileName}' - succeeded.");

                if (retryDestPath is not null)
                {
                    try
                    {
                        _companionFiles.MoveCompanionFiles(entry.EncodedFilePath!, Path.GetDirectoryName(entry.EncodedFilePath)!, Path.GetDirectoryName(retryDestPath)!, config.Processing.VidTypes, config.Processing.DeleteAfterConvert, inputPath);
                    }
                    catch (Exception ex)
                    {
                        _logger.Log($"  Companion file handling skipped on move retry: {ex.Message}", LogSeverity.Error);
                    }
                }
            }
            catch (DestinationCollisionSkippedException ex)
            {
                // Configured to skip, not an error - the file just stays put; nothing left to
                // retry, so this is as resolved as it's going to get.
                entry.Status = ResumeStatus.Completed;
                entry.EncodedFilePath = null;
                _logger.Log($"  {ex.Message}");
            }
            catch (Exception ex)
            {
                // Still failing - leave it as MoveFailed, tried again on the next pass.
                _logger.Log($"  Retried move for '{retryFileName}' failed again: {ex.Message}", LogSeverity.Error);
            }

            _resumeStore.Save(resumeState, resumeFilePath);
        }

        var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
        if (pending.Count > 0)
        {
            videoFiles = pending.Select(p => new FileInfo(p.FullName)).ToList();
        }
        else
        {
            videoFiles = _scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit).ToList();
            foreach (var f in videoFiles)
            {
                // A file can reappear at a path that already has a resume entry - e.g. the same
                // source re-added after a prior run already completed it. Reuse that entry rather
                // than adding a second one, or resume.json accumulates duplicate rows for the same
                // path (a stale Completed entry sitting next to a fresh Pending one).
                var existing = resumeState.FirstOrDefault(e => e.LaneId == lane.Id && e.FullName == f.FullName);
                if (existing is not null)
                {
                    existing.Status = ResumeStatus.Pending;
                }
                else
                {
                    resumeState.Add(new ResumeEntry { LaneId = lane.Id, FullName = f.FullName, Status = ResumeStatus.Pending });
                }
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
            _progress.FileStarted(lane.Id, i, fileCount, file.Name, presetName);

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
                    FailureReason = $"No {contentType} preset configured for this lane",
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
            var moveFailed = false;
            var diskFull = false;
            string? failureReason = null;
            string? postProcessWarning = null;

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
                    routedDestPath = _fileRouter.RouteFile(newFileName, isTv, tvShowBasePath, movieBasePath, config.Processing.MoveFiles, config.Processing.OnDestinationCollision);
                }
                catch (DestinationCollisionSkippedException ex)
                {
                    // Configured to skip on collision, not an error - the encode succeeded and the
                    // result is sitting in Output exactly as configured, so this stays a warning
                    // (postProcessWarning), not moveFailed/overallSuccess=false.
                    postProcessWarning = AppendWarning(postProcessWarning, ex.Message);
                    _logger.Log($"  {ex.Message}");
                }
                catch (Exception ex)
                {
                    // The conversion itself already succeeded - a bad/unreachable base path (e.g.
                    // an offline network drive) shouldn't take down the whole run, just leave the
                    // file where HandBrake wrote it. Still flagged as an error on this file's own
                    // result (see moveFailed below) so it doesn't quietly report "OK" while sitting
                    // unfiled in the Output folder - the log line alone is too easy to miss.
                    moveFailed = true;
                    if (LooksLikeDiskFull(ex.Message))
                    {
                        diskFull = true;
                        failureReason = "Output drive full, monitoring stopped";
                    }
                    else if (LooksLikePathUnavailable(ex))
                    {
                        failureReason = "Base folder path unavailable, move skipped";
                    }
                    // else: some other move failure (permission denied, file locked, etc.) - leave
                    // failureReason null so the report shows generic "ERROR" rather than a
                    // path-unavailable message that would be actively wrong for this cause.
                    _logger.Log($"  Move skipped: {ex.Message} - file remains at '{newFileName}'.", LogSeverity.Error);
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
                    postProcessWarning = AppendWarning(postProcessWarning, $"Sonarr/Radarr unmonitor failed: {ex.Message}");
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
                        postProcessWarning = AppendWarning(postProcessWarning, $"Companion files not moved: {ex.Message}");
                    }
                }

                // moveFailed here means the real-error branch above (not the Skip case, which never
                // sets moveFailed) - the file is genuinely stuck unrouted in Output. MoveFailed (not
                // Completed) so the retry loop at the top of this lane's next pass picks it back up
                // instead of a resume entry silently lying that this file is fully done.
                if (resumeEntry is not null)
                {
                    if (moveFailed)
                    {
                        resumeEntry.Status = ResumeStatus.MoveFailed;
                        resumeEntry.EncodedFilePath = newFileName;
                    }
                    else
                    {
                        resumeEntry.Status = ResumeStatus.Completed;
                    }
                }
            }
            else
            {
                try { File.Delete(tempFileName); } catch { }
                if (resumeEntry is not null) resumeEntry.Status = ResumeStatus.Error;

                // HandBrakeCLI still writes its own log even on a failed encode - confirmed live
                // against a genuinely full disk that its mux error names the cause explicitly
                // ("No space left on device"), which is a much more specific signal than just
                // "the encode failed" (which covers plenty of other, unrelated causes too).
                if (File.Exists(detailLogFile) && File.ReadLines(detailLogFile).Any(l => LooksLikeDiskFull(l)))
                {
                    diskFull = true;
                    failureReason = "Output drive full, monitoring stopped";
                }
            }

            var duration = endTime - startTime;
            if (duration < TimeSpan.Zero) duration = duration.Negate();

            // The encode can succeed while the file still doesn't end up where it's supposed to
            // (an unreachable TV/Movie base path) - reflect that in this file's own reported
            // status rather than only in the log, so a report/History reader isn't told "OK" for
            // a file that's actually sitting unfiled in the Output folder.
            var overallSuccess = success && !moveFailed;

            _logger.FileComplete(finalFileName ?? newFileName, beginSizeGb, endSizeGb, duration, overallSuccess, detailLogFile);
            _progress.FileCompleted(lane.Id, finalFileName ?? newFileName, overallSuccess);

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
                Success = overallSuccess,
                DiskFull = diskFull,
                FailureReason = failureReason,
                DetailLogFile = detailLogFile,
                StartTime = startTime,
                EndTime = endTime,
                ArrStatus = arrStatus,
                PostProcessWarning = postProcessWarning
            });
        }

        return results;
    }

    /// <summary>Matches the specific wording HandBrakeCLI/libav (Linux/macOS-style "No space left
    /// on device", from an ENOSPC-based error) and .NET's own IOException (Windows' "There is not
    /// enough space on the disk", from ERROR_DISK_FULL) use for a genuinely full volume - a
    /// deliberately narrow match, not a general "did this fail" check, so a run only stops itself
    /// for the one failure mode where retrying on the next poll is actively pointless. A pure
    /// static function so it's testable without a real full disk.</summary>
    internal static bool LooksLikeDiskFull(string? text) =>
        text is not null && (
            text.Contains("No space left on device", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("There is not enough space on the disk", StringComparison.OrdinalIgnoreCase));

    /// <summary>True only for the specific exception shapes a missing/unreachable base path
    /// actually produces - our own "not configured" check, .NET's own "no such path" exception, or
    /// an IOException whose message names a network path/share as the problem. Deliberately narrow:
    /// a permission error or a locked file also fails the move, but blaming "path unavailable" for
    /// those would be actively wrong, not just unhelpfully vague - those fall through to the
    /// generic "ERROR" instead. A pure static function so it's testable without a real offline
    /// network drive.</summary>
    internal static bool LooksLikePathUnavailable(Exception ex) =>
        ex is InvalidOperationException or DirectoryNotFoundException ||
        (ex is IOException && (
            ex.Message.Contains("network path", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("network name", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("network location", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("cannot find the path", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("is not accessible", StringComparison.OrdinalIgnoreCase)));

    /// <summary>Combines a new post-process warning onto any existing one for the same file -
    /// the companion-file move and the arr-unmonitor call are independent steps that can both
    /// fail for the same file, and neither should silently overwrite the other's message.</summary>
    private static string AppendWarning(string? existing, string next) =>
        existing is null ? next : $"{existing}; {next}";
}
