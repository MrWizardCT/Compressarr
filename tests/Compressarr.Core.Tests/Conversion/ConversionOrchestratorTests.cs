using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Tests.Conversion;

file sealed class PassThroughPathExpander : IPathExpander
{
    public string Expand(string value) => value;
    public bool PathExists(string value) => Directory.Exists(value) || File.Exists(value);
}

file sealed class RealFolderScanner : IVideoFileScanner
{
    public IReadOnlyList<FileInfo> FindVideoFiles(string inputPath, IReadOnlyList<string> vidTypes, long minSizeBytes, int limit) =>
        Directory.GetFiles(inputPath).Select(f => new FileInfo(f)).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
}

file sealed class FixedExtensionPresetService : IHandBrakePresetService
{
    public IReadOnlyList<HandBrakePreset> GetPresets(string presetsPath) => Array.Empty<HandBrakePreset>();
    public IReadOnlyList<string> GetPresetNames(string presetsPath) => Array.Empty<string>();
    public bool PresetExists(string presetName, string presetsPath) => true;
    public HandBrakePreset? GetPreset(string presetName, string presetsPath) => null;
    public string GetOutputExtension(string presetName, string presetsPath, out string? warning) { warning = null; return ".mkv"; }
    public void InvalidateCache(string? presetsPath = null) { }
}

/// <summary>Simulates HandBrakeCLI by writing a real (tiny) output file, since the orchestrator
/// moves/measures it for real afterward.</summary>
file sealed class FakeProcessRunner : IHandBrakeProcessRunner
{
    public Task<HandBrakeRunResult> RunAsync(
        string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName,
        string? extraOptions, string detailLogFile, Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        File.WriteAllText(tempOutputPath, "fake encoded output");
        return Task.FromResult(new HandBrakeRunResult(Success: true, DetailLogFile: detailLogFile));
    }
}

/// <summary>Simulates the exact real failure captured live against a genuinely full disk: a
/// truncated temp output file, a failed HandBrakeRunResult, and the real HandBrakeCLI log content
/// observed (mux error naming "No space left on device", "Finished work at" printed anyway,
/// "Encode failed").</summary>
file sealed class DiskFullProcessRunner : IHandBrakeProcessRunner
{
    public Task<HandBrakeRunResult> RunAsync(
        string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName,
        string? extraOptions, string detailLogFile, Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        File.WriteAllText(tempOutputPath, "truncated, disk filled up mid-write");
        File.WriteAllText(detailLogFile,
            "ERROR: avformatMux: track 0, av_interleaved_write_frame failed with error 'No space left on device'\n" +
            "Finished work at Sat Jan  1 00:00:00 2026\n" +
            "libhb: work result = 4\n" +
            "Encode failed (error 4).\n");
        return Task.FromResult(new HandBrakeRunResult(Success: false, DetailLogFile: detailLogFile));
    }
}

/// <summary>Returns configAfterSwitch starting from its (1-based) switchOnCall'th Load() call and
/// every call after - ConversionOrchestrator.ProcessLaneAsync calls Load() exactly once per file,
/// immediately after that file's HandBrakeCLI pass finishes, so this ties the "settings changed"
/// moment precisely to a specific file's reload point rather than guessing at timing.</summary>
file sealed class SwitchingConfigStore : IConfigStore
{
    private readonly CompressarrConfig _configBeforeSwitch;
    private readonly CompressarrConfig _configAfterSwitch;
    private readonly int _switchOnCall;
    private int _callCount;

    public SwitchingConfigStore(CompressarrConfig configBeforeSwitch, CompressarrConfig configAfterSwitch, int switchOnCall)
    {
        _configBeforeSwitch = configBeforeSwitch;
        _configAfterSwitch = configAfterSwitch;
        _switchOnCall = switchOnCall;
    }

    public CompressarrConfig Load(string path)
    {
        _callCount++;
        return _callCount >= _switchOnCall ? _configAfterSwitch : _configBeforeSwitch;
    }

    public void Save(CompressarrConfig config, string path) { }
    public T Update<T>(string path, Func<CompressarrConfig, T> mutate) => mutate(_configBeforeSwitch);
}

/// <summary>Simulates a move failure whose exception isn't disk-full and isn't shaped like a
/// missing/unreachable path (e.g. a permission error) - used to prove those don't get mislabeled
/// with the path-unavailable reason.</summary>
file sealed class ThrowingFileRouter : IFileRouter
{
    private readonly Exception _exception;
    public ThrowingFileRouter(Exception exception) => _exception = exception;
    public string? RouteFile(string fileName, bool isTv, string tvShowBasePath, string movieBasePath, bool moveFiles) => throw _exception;
}

file sealed record TrashCall(string Path, DeleteAfterConvertMode Mode);

file sealed class RecordingTrashService : ITrashService
{
    public List<TrashCall> Calls { get; } = new();
    public void DeleteFile(string path, DeleteAfterConvertMode mode) => Calls.Add(new TrashCall(path, mode));
    public void DeleteFolder(string path, DeleteAfterConvertMode mode) { }
}

file sealed class NoOpRunLogger : IRunLogger
{
    public event Action<string, LogSeverity>? LineWritten { add { } remove { } }
    public string Initialize(string logFilePath, string timestamp) => Path.GetTempFileName();
    public void Log(string message, LogSeverity severity = LogSeverity.Info) { }
    public void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset) { }
    public void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile) { }
}

file sealed class NoOpProgressReporter : IRunProgressReporter
{
    public void RunStarted(string timestamp) { }
    public void LaneStarted(string laneId, string laneDisplayName) { }
    public void FileStarted(string laneId, int index, int total, string fileName) { }
    public void FileProgress(string laneId, double percent, double? fps, string? eta) { }
    public void FileCompleted(string laneId, string fileName, bool success) { }
    public void RunCompleted(int totalFiles) { }
}

file sealed class NoOpArrUnmonitorService : IArrUnmonitorService
{
    public Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv) => Task.FromResult<string?>(null);
}

file sealed class NoOpCompanionFileService : ICompanionFileService
{
    public void MoveCompanionFiles(string originalFileFullName, string originalFileDirectory, string destinationFolder, IReadOnlyList<string> vidTypes, DeleteAfterConvertMode deleteAfterConvert, string inputRoot) { }
}

file sealed class NoOpResumeStateStore : IResumeStateStore
{
    public List<ResumeEntry> Load(string path) => new();
    public void Save(List<ResumeEntry> state, string path) { }
    public void DeleteIfComplete(List<ResumeEntry> state, string path) { }
}

/// <summary>Covers the mid-run config-reload fix: settings used to be read once per run/loop
/// lifetime, so a change made while a lane was still processing files had no effect until
/// restarted. ConversionOrchestrator now reloads config immediately after each file's
/// HandBrakeCLI pass finishes, before that file's own post-processing.</summary>
public class ConversionOrchestratorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-conversion-orchestrator-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private static CompressarrConfig BuildConfig(DeleteAfterConvertMode mode) => new()
    {
        Processing = new ProcessingSettings { DeleteAfterConvert = mode, MoveFiles = false, ClearTitleMetadata = false }
    };

    [Fact]
    public async Task ProcessLaneAsync_ConfigChangedMidRun_SecondFileHonorsNewValue_FirstFileDoesNot()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        // Alphabetical order matters - RealFolderScanner sorts by name, so file1 is processed
        // before file2.
        var file1Path = Path.Combine(inputDir, "file1.mkv");
        var file2Path = Path.Combine(inputDir, "file2.mkv");
        File.WriteAllText(file1Path, "source 1");
        File.WriteAllText(file2Path, "source 2");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset"
        };

        var initialConfig = BuildConfig(DeleteAfterConvertMode.Maintain);
        initialConfig.Lanes.Add(lane);
        var configAfterSwitch = BuildConfig(DeleteAfterConvertMode.Delete);
        configAfterSwitch.Lanes.Add(lane);

        // Call #1 = file1's reload (still Maintain), call #2 = file2's reload (now Delete).
        var configStore = new SwitchingConfigStore(initialConfig, configAfterSwitch, switchOnCall: 2);
        var trash = new RecordingTrashService();

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            trash,
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, initialConfig, _tempDir, "20260101_000000", new List<ResumeEntry>(), Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Success));

        // file1's post-processing ran while DeleteAfterConvert was still Maintain (the switch
        // only happens once file1's own HandBrakeCLI pass finishes, inside the reload point) -
        // Maintain means _trash.DeleteFile is never called for it at all.
        Assert.DoesNotContain(trash.Calls, c => c.Path == file1Path);

        // file2's post-processing ran after the reload picked up the switched-to Delete mode.
        var file2Call = Assert.Single(trash.Calls);
        Assert.Equal(file2Path, file2Call.Path);
        Assert.Equal(DeleteAfterConvertMode.Delete, file2Call.Mode);
    }

    [Fact]
    public async Task ProcessLaneAsync_MoveDestinationUnreachable_ReportsErrorButKeepsTheFile()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        var sourcePath = Path.Combine(inputDir, "Caddyshack (1980).mkv");
        File.WriteAllText(sourcePath, "source");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset",
            // Simulates an offline/unreachable network drive.
            MovieBasePath = @"Z:\Unavailable\Movies"
        };

        var config = new CompressarrConfig
        {
            Processing = new ProcessingSettings { DeleteAfterConvert = DeleteAfterConvertMode.Maintain, MoveFiles = true, ClearTitleMetadata = false }
        };
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", new List<ResumeEntry>(), Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        var result = Assert.Single(results);
        // The encode itself succeeded - only the move failed - but this must be reported as an
        // error, not silently as "OK", or a reader of the report/History would never know the
        // file didn't end up where it was supposed to.
        Assert.False(result.Success);
        // The report shows this specific reason in place of a bare "ERROR" - not disk-full, since
        // the unreachable path here is a nonexistent drive letter, not a full one.
        Assert.False(result.DiskFull);
        Assert.Equal("Base folder path unavailable, move skipped", result.FailureReason);
        // The file is not lost - it's exactly where HandBrake wrote it, in the lane's Output
        // folder, since routing never got to move it anywhere else.
        var outputPath = Path.Combine(outputDir, "Caddyshack (1980).mkv");
        Assert.True(File.Exists(outputPath));
        Assert.Equal(outputPath, result.NewFileName);
    }

    [Fact]
    public async Task ProcessLaneAsync_EncodeFailsFromDiskFull_ResultFlagsDiskFull()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        var sourcePath = Path.Combine(inputDir, "Caddyshack (1980).mkv");
        File.WriteAllText(sourcePath, "source");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset"
        };

        var config = new CompressarrConfig
        {
            Processing = new ProcessingSettings { DeleteAfterConvert = DeleteAfterConvertMode.Maintain, MoveFiles = false, ClearTitleMetadata = false }
        };
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new DiskFullProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", new List<ResumeEntry>(), Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.True(result.DiskFull);
        Assert.Equal("Output drive full, monitoring stopped", result.FailureReason);
        // A failed encode's truncated temp file is cleaned up - it never becomes the "output".
        Assert.False(File.Exists(Path.Combine(outputDir, "Caddyshack (1980).mkv")));
        // The real source is never touched by a failed encode either way.
        Assert.True(File.Exists(sourcePath));
    }

    [Fact]
    public async Task ProcessLaneAsync_PendingResumeFileMissing_RemovedAndFallsBackToScan()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        // Only a brand-new file sits in Input - the previously pending file was removed by hand
        // (e.g. between test runs) before monitoring started back up.
        var newFilePath = Path.Combine(inputDir, "New Movie (2024).mkv");
        File.WriteAllText(newFilePath, "source");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset"
        };

        var config = BuildConfig(DeleteAfterConvertMode.Maintain);
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        var resumeState = new List<ResumeEntry>
        {
            new() { LaneId = "lane1", FullName = Path.Combine(inputDir, "Deleted Movie (1999).mkv"), Status = ResumeStatus.Pending }
        };

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", resumeState, Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        // The dead pending entry pointing at the removed file is gone from resume state...
        Assert.DoesNotContain(resumeState, e => e.FullName.Contains("Deleted Movie"));
        // ...and the lane fell back to scanning Input instead of silently processing nothing,
        // picking up the genuinely new file that was actually sitting there.
        var result = Assert.Single(results);
        Assert.Equal("New Movie (2024).mkv", result.FileName);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ProcessLaneAsync_FileReappearsAfterCompleting_ReusesResumeEntryInsteadOfDuplicating()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        var filePath = Path.Combine(inputDir, "Private School (1983).mkv");
        File.WriteAllText(filePath, "source, re-added after already completing once");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset"
        };

        var config = BuildConfig(DeleteAfterConvertMode.Maintain);
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        // Simulates the exact state pulled from a real resume.json: this file already completed
        // in an earlier run, but the same path has since reappeared in Input.
        var resumeState = new List<ResumeEntry>
        {
            new() { LaneId = "lane1", FullName = filePath, Status = ResumeStatus.Completed }
        };

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", resumeState, Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        Assert.Single(results);
        // Exactly one resume entry for this path, not two - the pre-existing Completed entry was
        // reused (and is Completed again, since this run finished it) rather than left in place
        // alongside a second, newly-added entry.
        var entry = Assert.Single(resumeState, e => e.FullName == filePath);
        Assert.Equal(ResumeStatus.Completed, entry.Status);
    }

    [Fact]
    public async Task ProcessLaneAsync_NoPresetConfigured_ReportsSpecificFailureReason()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        var sourcePath = Path.Combine(inputDir, "Caddyshack (1980).mkv");
        File.WriteAllText(sourcePath, "source");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir
            // MoviePreset intentionally left unset - this is what's under test.
        };

        var config = BuildConfig(DeleteAfterConvertMode.Maintain);
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new FileRouter(),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", new List<ResumeEntry>(), Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.Equal("No Movie preset configured for this lane", result.FailureReason);
    }

    [Fact]
    public async Task ProcessLaneAsync_MoveFailsForUnrelatedReason_FallsBackToGenericError()
    {
        var inputDir = Path.Combine(_tempDir, "Input");
        var outputDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);

        var sourcePath = Path.Combine(inputDir, "Caddyshack (1980).mkv");
        File.WriteAllText(sourcePath, "source");

        var lane = new LaneConfig
        {
            Id = "lane1",
            DisplayName = "Test Lane",
            Enabled = true,
            Input = inputDir,
            Output = outputDir,
            MoviePreset = "Any Preset",
            MovieBasePath = @"D:\Movies"
        };

        var config = new CompressarrConfig
        {
            Processing = new ProcessingSettings { DeleteAfterConvert = DeleteAfterConvertMode.Maintain, MoveFiles = true, ClearTitleMetadata = false }
        };
        config.Lanes.Add(lane);
        var configStore = new SwitchingConfigStore(config, config, switchOnCall: int.MaxValue);

        var orchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(),
            new RealFolderScanner(),
            new FixedExtensionPresetService(),
            new MetadataService(),
            new FakeProcessRunner(),
            new ThrowingFileRouter(new UnauthorizedAccessException(@"Access to the path 'D:\Movies' is denied.")),
            new NoOpCompanionFileService(),
            new NoOpArrUnmonitorService(),
            new RecordingTrashService(),
            new NoOpRunLogger(),
            new NoOpResumeStateStore(),
            new NoOpProgressReporter(),
            configStore);

        var results = await orchestrator.ProcessLaneAsync(
            lane, config, _tempDir, "20260101_000000", new List<ResumeEntry>(), Path.Combine(_tempDir, "resume.json"), CancellationToken.None);

        var result = Assert.Single(results);
        Assert.False(result.Success);
        Assert.False(result.DiskFull);
        // A permission error isn't a path-availability problem - mislabeling it that way would be
        // actively wrong, not just unhelpfully vague, so this falls back to the generic reason.
        Assert.Null(result.FailureReason);
        // The file is not lost - it's exactly where HandBrake wrote it, since routing threw before
        // it could move it anywhere.
        Assert.True(File.Exists(Path.Combine(outputDir, "Caddyshack (1980).mkv")));
    }
}
