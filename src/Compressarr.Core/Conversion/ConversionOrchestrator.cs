using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Conversion;

/// <summary>Per-lane state that survives across non-contiguous visits to the same lane in a global,
/// cross-lane processing order - one lane's file can now be processed, then a different lane's
/// file, then this lane's next file, so the config/path snapshot a mid-run settings reload updates
/// (see ProcessOneFileAsync) needs an explicit home instead of a local variable closed over by a
/// single lane's own while loop. InputPath is deliberately init-only and never refreshed, matching
/// the original method's own asymmetry: companion-file routing math depends on the ORIGINAL Input
/// path as its relative-move cutoff root, not whatever it's reloaded to mid-run.</summary>
public sealed class LaneProcessingContext
{
    public required LaneConfig Lane { get; set; }
    public required CompressarrConfig Config { get; set; }
    public required string InputPath { get; init; }
    public required string OutputBase { get; set; }
    public required string TvShowBasePath { get; set; }
    public required string MovieBasePath { get; set; }
    public required string HbLoc { get; set; }
    public required string PresetsPath { get; set; }

    /// <summary>"File i of N" and log-filename zero-padding - a point-in-time estimate from this
    /// lane's own prep, cosmetic only (a queue-control edit mid-run can make the real count drift
    /// slightly), kept per-lane rather than switched to a global count across every lane so today's
    /// "Compressing File in Lane X, i of N" UI copy keeps meaning what it already says.</summary>
    public int FileIndex { get; set; }
    public int FileTotal { get; set; }
    public int PadSize { get; set; }

    /// <summary>This lane's own natural (recursive folder scan) order, by full path - the fallback
    /// tie-break for entries with no explicit user-set Order, used identically by both the global
    /// cross-lane picking loop (RunOrchestrator) and the Monitor page's queue display
    /// (RunEndpoints.ComputeUpNext) so the two can never disagree about "what's next" for a file
    /// nobody has ever dragged.</summary>
    public required IReadOnlyDictionary<string, int> NaturalOrderIndex { get; init; }
}

public interface IConversionOrchestrator
{
    /// <summary>Per-lane setup, run once per enabled lane before any cross-lane interleaved
    /// processing begins: validates the lane's Input/Output configuration, drops dead Pending/Error
    /// entries whose source file is gone, retries any MoveFailed entries from a prior pass, always
    /// scans Input recursively (both to seed resumeState with Pending entries when none exist yet,
    /// and to build the natural fallback order used for untouched entries either way). Returns null
    /// if this lane can't be processed at
    /// all (bad Input path, or no Output configured with "write output to same folder as input"
    /// off) - the caller should skip the lane entirely in that case, the same way the lane-scoped
    /// early-returns worked before this method existed.</summary>
    LaneProcessingContext? PrepareLane(
        LaneConfig lane,
        CompressarrConfig config,
        List<ResumeEntry> resumeState,
        string resumeFilePath);

    /// <summary>Processes exactly one already-selected file for the lane context carries - fully
    /// finished (converted, routed, arr-unmonitored, companions handled) before returning, so the
    /// resume state on disk is always accurate up to the file currently in flight, the same
    /// guarantee ProcessLaneAsync used to make for a whole lane. Mutates context in place (config
    /// reload, derived paths) so this lane's NEXT call - however many other lanes' files are
    /// processed in between - picks up mid-run settings changes exactly like before.
    ///
    /// cancellationToken (Abort) kills the in-flight HandBrakeCLI process immediately -
    /// IHandBrakeProcessRunner registers a Kill(entireProcessTree) callback directly on it. The
    /// caller is responsible for checking the separate, gentler stopToken (Stop Monitoring) BEFORE
    /// calling this method for the next file - once a file is in flight here, it always finishes
    /// completely.</summary>
    Task<ConversionResult> ProcessOneFileAsync(
        LaneProcessingContext context,
        ResumeEntry resumeEntry,
        string logFilePath,
        string timestamp,
        List<ResumeEntry> resumeState,
        string resumeFilePath,
        CancellationToken cancellationToken);

    /// <summary>Merges Order/Skipped/PresetOverride/Removed from whatever is currently on disk onto
    /// the matching entries in resumeState (by LaneId+FullName), mutating resumeState in place.
    /// Deliberately narrow: only those four user-editable fields are ever touched, never Status or
    /// EncodedFilePath, so this can never resurrect/misclassify an entry actively being driven
    /// through ProcessOneFileAsync's own state machine - it only pulls in what a queue-control web
    /// request (reorder/skip/preset-override/remove) could actually have changed. A disk entry with
    /// no in-memory match (a freshly-scanned file the user acted on before it was ever tracked) is
    /// adopted as a new entry. The caller is responsible for calling this before picking the next
    /// file to process, across every lane - see RunOrchestrator's global picking loop.</summary>
    void RefreshResumeState(List<ResumeEntry> resumeState, string resumeFilePath);
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

    public LaneProcessingContext? PrepareLane(
        LaneConfig lane,
        CompressarrConfig config,
        List<ResumeEntry> resumeState,
        string resumeFilePath)
    {
        var inputPath = _pathExpander.Expand(lane.Input);
        var outputBase = _pathExpander.Expand(lane.Output);
        var tvShowBasePath = _pathExpander.Expand(lane.TvShowBasePath);
        var movieBasePath = _pathExpander.Expand(lane.MovieBasePath);

        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(outputBase) && !config.Processing.OutSameAsIn)
        {
            _logger.Log($"Lane '{lane.DisplayName}' has no Output folder configured and 'write output to same folder as input' is off - skipping.", LogSeverity.Error);
            return null;
        }

        var hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
        var presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);

        List<FileInfo> videoFiles;

        // A Pending or Error entry whose source file is gone (e.g. removed by hand, or already
        // handled by Sonarr/Radarr, between runs) can never be resumed or retried - drop it rather
        // than let it block this lane forever. Without this, a lane with only dead pending entries
        // falls into the branch below, finds nothing to process, and never falls back to scanning
        // Input for genuinely new files. Error entries need the same treatment for a different
        // reason: unlike Pending, they're never re-checked by anything else once their source
        // disappears, so a single dead Error entry pins resumeState.Count and RunOrchestrator's
        // "stillOutstanding" check permanently - the whole resume.json (including every already-
        // Completed entry sitting alongside it) never gets cleaned up, and "Resuming previous
        // incomplete run" keeps logging that inflated count on every pass.
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

        // Always scan (recursively - a lane's files can be nested in subfolders, e.g. one per
        // movie) so the natural fallback order for entries with no explicit user-set Order matches
        // exactly what RunEndpoints.ComputeUpNext independently computes the same way for the
        // Monitor page's own queue display - both must never disagree about "what's next" for a
        // file nobody has ever dragged.
        var scanned = _scanner.FindVideoFiles(inputPath, config.Processing.VidTypes, config.Processing.MinSizeBytes, config.Processing.Limit).ToList();
        var naturalOrderIndex = scanned
            .Select((f, idx) => (f.FullName, idx))
            .ToDictionary(x => x.FullName, x => x.idx, StringComparer.OrdinalIgnoreCase);

        var pending = resumeState.Where(e => e.LaneId == lane.Id && e.Status == ResumeStatus.Pending).ToList();
        if (pending.Count > 0)
        {
            // User-set Order (drag-to-reorder on the Monitor page) drives processing order - lower
            // first; entries without one (untouched by the user) sort after, in their original
            // relative order (OrderBy is a stable sort, and int.MaxValue is the same tie-break
            // value for every one of them). A Skipped entry stays Pending (still shown in the
            // queue, still eligible to be un-skipped later) but is excluded from this count.
            videoFiles = pending
                .OrderBy(p => p.Order ?? int.MaxValue)
                .Where(p => !p.Skipped && !p.Removed)
                .Select(p => new FileInfo(p.FullName))
                .ToList();
        }
        else
        {
            videoFiles = scanned;
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
        var padSize = fileCount.ToString().Length;

        return new LaneProcessingContext
        {
            Lane = lane,
            Config = config,
            InputPath = inputPath,
            OutputBase = outputBase,
            TvShowBasePath = tvShowBasePath,
            MovieBasePath = movieBasePath,
            HbLoc = hbloc,
            PresetsPath = presetsPath,
            FileIndex = 0,
            FileTotal = fileCount,
            PadSize = padSize,
            NaturalOrderIndex = naturalOrderIndex
        };
    }

    public async Task<ConversionResult> ProcessOneFileAsync(
        LaneProcessingContext context,
        ResumeEntry resumeEntry,
        string logFilePath,
        string timestamp,
        List<ResumeEntry> resumeState,
        string resumeFilePath,
        CancellationToken cancellationToken)
    {
        context.FileIndex++;
        var i = context.FileIndex;
        var fileCount = context.FileTotal;
        var padSize = context.PadSize;
        var lane = context.Lane;
        var config = context.Config;
        var inputPath = context.InputPath;
        var outputBase = context.OutputBase;
        var tvShowBasePath = context.TvShowBasePath;
        var movieBasePath = context.MovieBasePath;
        var hbloc = context.HbLoc;
        var presetsPath = context.PresetsPath;

        var file = new FileInfo(resumeEntry.FullName);

        var isTv = ContentClassifier.IsTvFile(file.Name);
        var contentType = isTv ? "TV Show" : "Movie";
        // A per-file preset override (set from the Monitor page's queue) wins over the lane's own
        // TvPreset/MoviePreset for this one entry only - looked up before presetName is used
        // anywhere (logging, the FileStarted progress event, the "no preset" check) so all of it
        // reflects the actual preset this file will encode with.
        var presetName = !string.IsNullOrWhiteSpace(resumeEntry.PresetOverride)
            ? resumeEntry.PresetOverride
            : (isTv ? lane.TvPreset : lane.MoviePreset);

        var beginSizeGb = Math.Round(file.Length / (double)BytesPerGb, 3);
        var startTime = DateTime.Now;

        _logger.FileStart(lane.DisplayName, i, fileCount, file.Name, beginSizeGb, contentType, presetName);
        _progress.FileStarted(lane.Id, i, fileCount, file.Name, presetName);

        if (string.IsNullOrWhiteSpace(presetName))
        {
            _logger.Log($"  No {contentType} preset configured for this lane - skipping.", LogSeverity.Error);
            resumeEntry.Status = ResumeStatus.Error;
            _resumeStore.Save(resumeState, resumeFilePath);

            return new ConversionResult
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
            };
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

            // HandBrakeCLI emits a progress line roughly once a second - logging every one of them
            // would flood the recent-log window, so only a real 10% step gets written.
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

        // Settings are otherwise only read once, at the start of a manual run or for the whole
        // lifetime of the monitoring loop - a change made mid-run (delete-after-convert mode, clear
        // title metadata, a lane's base path, etc.) would otherwise have no effect until the run/
        // loop is restarted. Re-read as soon as HandBrakeCLI is done for this file, before any of
        // the post-processing below, so it takes effect on this file's own routing/cleanup and on
        // every file still to come in this lane, however many OTHER lanes' files get processed in
        // between - written back onto context so this lane's next call sees it, not just local
        // variables that would otherwise be lost between non-contiguous visits to this lane.
        config = _configStore.Load(AppPaths.GetConfigFilePath());
        lane = config.Lanes.FirstOrDefault(l => l.Id == lane.Id) ?? lane;
        outputBase = _pathExpander.Expand(lane.Output);
        tvShowBasePath = _pathExpander.Expand(lane.TvShowBasePath);
        movieBasePath = _pathExpander.Expand(lane.MovieBasePath);
        hbloc = _pathExpander.Expand(config.HandBrake.CliPath);
        presetsPath = _pathExpander.Expand(config.HandBrake.PresetsPath);
        _metadata.Enabled = config.Processing.ClearTitleMetadata;
        context.Lane = lane;
        context.Config = config;
        context.OutputBase = outputBase;
        context.TvShowBasePath = tvShowBasePath;
        context.MovieBasePath = movieBasePath;
        context.HbLoc = hbloc;
        context.PresetsPath = presetsPath;

        // Same reasoning as the config reload just above, for resume state: pull in whatever a
        // queue-control request changed on OTHER entries while this file was encoding, so the save
        // below (and whichever entry gets picked next, across any lane) doesn't clobber/ignore it.
        // Never touches resumeEntry's own Status - only RefreshResumeState's own three fields.
        RefreshResumeState(resumeState, resumeFilePath);

        if (runResult.Cancelled)
        {
            try { File.Delete(tempFileName); } catch { }
            _logger.Log($"  Conversion of '{file.Name}' aborted by user.", LogSeverity.Error);
            resumeEntry.Status = ResumeStatus.Error;
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

            // If source and final destination are the same path (in-place conversion), the rename
            // above already replaced the original with the converted result - there is nothing left
            // to separately delete.
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
                // The conversion itself already succeeded - a bad/unreachable base path (e.g. an
                // offline network drive) shouldn't take down the whole run, just leave the file
                // where HandBrake wrote it. Still flagged as an error on this file's own result (see
                // moveFailed below) so it doesn't quietly report "OK" while sitting unfiled in the
                // Output folder - the log line alone is too easy to miss.
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

            // Deliberately before companion-file/folder cleanup below: the rescan should see the
            // source folder still on disk rather than already gone.
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
            // Completed) so the retry logic in this lane's next PrepareLane call picks it back up
            // instead of a resume entry silently lying that this file is fully done.
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
        else
        {
            try { File.Delete(tempFileName); } catch { }
            resumeEntry.Status = ResumeStatus.Error;

            // HandBrakeCLI still writes its own log even on a failed encode - confirmed live
            // against a genuinely full disk that its mux error names the cause explicitly ("No
            // space left on device"), which is a much more specific signal than just "the encode
            // failed" (which covers plenty of other, unrelated causes too).
            if (File.Exists(detailLogFile) && File.ReadLines(detailLogFile).Any(l => LooksLikeDiskFull(l)))
            {
                diskFull = true;
                failureReason = "Output drive full, monitoring stopped";
            }
        }

        var duration = endTime - startTime;
        if (duration < TimeSpan.Zero) duration = duration.Negate();

        // The encode can succeed while the file still doesn't end up where it's supposed to (an
        // unreachable TV/Movie base path) - reflect that in this file's own reported status rather
        // than only in the log, so a report/History reader isn't told "OK" for a file that's
        // actually sitting unfiled in the Output folder.
        var overallSuccess = success && !moveFailed;

        _logger.FileComplete(finalFileName ?? newFileName, beginSizeGb, endSizeGb, duration, overallSuccess, detailLogFile);
        _progress.FileCompleted(lane.Id, finalFileName ?? newFileName, overallSuccess);

        _resumeStore.Save(resumeState, resumeFilePath);

        return new ConversionResult
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
        };
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

    /// <summary>Combines a new post-process warning onto any existing one for the same file - the
    /// companion-file move and the arr-unmonitor call are independent steps that can both fail for
    /// the same file, and neither should silently overwrite the other's message.</summary>
    private static string AppendWarning(string? existing, string next) =>
        existing is null ? next : $"{existing}; {next}";

    public void RefreshResumeState(List<ResumeEntry> resumeState, string resumeFilePath)
    {
        var onDisk = _resumeStore.Load(resumeFilePath);
        foreach (var diskEntry in onDisk)
        {
            var inMemory = resumeState.FirstOrDefault(e => e.LaneId == diskEntry.LaneId && e.FullName == diskEntry.FullName);
            if (inMemory is not null)
            {
                inMemory.Order = diskEntry.Order;
                inMemory.Skipped = diskEntry.Skipped;
                inMemory.PresetOverride = diskEntry.PresetOverride;
                inMemory.Removed = diskEntry.Removed;
            }
            else
            {
                resumeState.Add(diskEntry);
            }
        }
    }
}
