using Compressarr.Core.Arr;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;
using Compressarr.Core.Notifications;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Presets;
using Compressarr.Core.Reporting;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Tests.Orchestration;

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

/// <summary>Simulates HandBrakeCLI by writing a real (tiny) output file, and records every source
/// path it was asked to encode, in call order - the only way to observe the TRUE global processing
/// order across lanes, since RunResult.Report.Lanes groups results back by lane afterward and can't
/// tell you the real interleaved timeline on its own.</summary>
file sealed class RecordingProcessRunner : IHandBrakeProcessRunner
{
    public List<string> ProcessedInOrder { get; } = new();
    private readonly Action<string>? _onEachRun;

    public RecordingProcessRunner(Action<string>? onEachRun = null) => _onEachRun = onEachRun;

    public Task<HandBrakeRunResult> RunAsync(
        string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName,
        string? extraOptions, string detailLogFile, Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        ProcessedInOrder.Add(Path.GetFileName(sourcePath));
        _onEachRun?.Invoke(sourcePath);
        File.WriteAllText(tempOutputPath, "fake encoded output");
        return Task.FromResult(new HandBrakeRunResult(Success: true, DetailLogFile: detailLogFile));
    }
}

file sealed class NoOpCompanionFileService : ICompanionFileService
{
    public void MoveCompanionFiles(string originalFileFullName, string originalFileDirectory, string destinationFolder, IReadOnlyList<string> vidTypes, DeleteAfterConvertMode deleteAfterConvert, string inputRoot) { }
}

file sealed class NoOpArrUnmonitorService : IArrUnmonitorService
{
    public Task<string?> UnmonitorAsync(CompressarrConfig config, string fileName, bool isTv) => Task.FromResult<string?>(null);
}

file sealed class NoOpTrashService : ITrashService
{
    public void DeleteFile(string path, DeleteAfterConvertMode mode) { }
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
    public void LaneStarted(string laneId, string laneDisplayName, bool isResumed) { }
    public void FileStarted(string laneId, int index, int total, string fileName, string? presetName) { }
    public void FileProgress(string laneId, double percent, double? fps, string? eta) { }
    public void FileCompleted(string laneId, string fileName, bool success) { }
    public void RunCompleted(int totalFiles) { }
}

file sealed class NoOpHistoryStore : IRunHistoryStore
{
    public void AppendRun(string logFilePath, RunHistoryRecord record) { }
    public IReadOnlyList<RunHistoryRecord> GetHistory(string logFilePath) => Array.Empty<RunHistoryRecord>();
    public int GetRunCount(string runCountPath) => 0;
    public void IncrementRunCount(string runCountPath) { }
}

file sealed class NoOpHistoryRollupCalculator : IHistoryRollupCalculator
{
    public (HistoryRollup Today, HistoryRollup ThisMonth, HistoryRollup ThisYear) Calculate(string logFilePath) =>
        (new HistoryRollup(0, 0, 0), new HistoryRollup(0, 0, 0), new HistoryRollup(0, 0, 0));
}

file sealed class NoOpHtmlReportGenerator : IHtmlReportGenerator
{
    public string Generate(ReportModel model) => "<html></html>";
}

file sealed class NoOpReportLauncher : IReportLauncher
{
    public void Open(string reportFilePath) { }
}

file sealed class NoOpNotificationDispatcher : INotificationDispatcher
{
    public Task DispatchAsync(NotificationSettings settings, NotificationEvent evt, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>IConfigStore is only used by ConversionOrchestrator's own mid-file reload (not by these
/// tests, which never change config mid-run) - always returning the same fixed config passed in at
/// construction is enough, and matches RunOnceAsync's own real config as the same reference these
/// tests already build and pass to it.</summary>
file sealed class StaticConfigStore : IConfigStore
{
    private readonly CompressarrConfig _config;
    public StaticConfigStore(CompressarrConfig config) => _config = config;
    public CompressarrConfig Load(string path) => _config;
    public void Save(CompressarrConfig config, string path) { }
    public T Update<T>(string path, Func<CompressarrConfig, T> mutate) => mutate(_config);
}

/// <summary>Covers RunOrchestrator's global, cross-lane processing loop - added alongside the
/// cross-lane priority queue feature, since this exact loop (previously "drain lane 1 fully, then
/// drain lane 2") had zero dedicated regression coverage before this: ConversionOrchestratorTests
/// only ever drives a single lane, and RunLoopControllerTests mocks IRunOrchestrator entirely.
/// Uses the real ConversionOrchestrator (not mocked) so these tests exercise the actual
/// PrepareLane/ProcessOneFileAsync split RunOrchestrator drives in production.</summary>
public class RunOrchestratorTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-run-orchestrator-tests-").FullName;

    public RunOrchestratorTests()
    {
        // RunOrchestrator.RunOnceAsync reads/writes resume.json (and would read settings.json/
        // runcount.json too, if these tests ever triggered that code) via AppPaths.* internally -
        // they are NOT parameters it accepts, unlike ConversionOrchestrator's own methods. Without
        // this override, a real test run here would read and write THIS MACHINE's actual
        // production Compressarr data - confirmed the hard way once already (a prior draft of this
        // file polluted the real resume.json with temp-path test entries before this override was
        // added; cleaned up by hand afterward).
        AppPaths.TestOverrideAppDataDirectory = _tempDir;
    }

    public void Dispose()
    {
        AppPaths.TestOverrideAppDataDirectory = null;
        Directory.Delete(_tempDir, recursive: true);
    }

    // Returns (RunOrchestrator, string ResumeFilePath) only - a file-local type like
    // RecordingProcessRunner can't appear in a member signature of this non-file-local test class,
    // even a private one, so the caller constructs it and passes it in instead of getting it back.
    private (RunOrchestrator Orchestrator, string ResumeFilePath) BuildOrchestrator(
        CompressarrConfig config, IHandBrakeProcessRunner processRunner, IResumeStateStore? resumeStore = null)
    {
        // HandBrakeCLI/presets "paths" just need to exist on disk for PathExists to pass -
        // PassThroughPathExpander does no real expansion, and FixedExtensionPresetService never
        // actually reads presetsPath's contents. Tests are expected to have already set
        // config.HandBrake.CliPath/PresetsPath to these same real-on-disk paths.
        File.WriteAllText(config.HandBrake.CliPath, "fake");
        File.WriteAllText(config.HandBrake.PresetsPath, "{}");

        // Must match what RunOrchestrator.RunOnceAsync itself resolves internally
        // (AppPaths.GetResumeFilePath(), now rooted at _tempDir via the override above) - not an
        // independently-chosen path - so the ConversionOrchestrator side (which takes this as an
        // explicit parameter) and the RunOrchestrator side (which doesn't) agree on the same file.
        var resumeFilePath = AppPaths.GetResumeFilePath();
        var effectiveResumeStore = resumeStore ?? new JsonResumeStateStore();

        var conversionOrchestrator = new ConversionOrchestrator(
            new PassThroughPathExpander(), new RealFolderScanner(), new FixedExtensionPresetService(), new MetadataService(),
            processRunner, new FileRouter(), new NoOpCompanionFileService(), new NoOpArrUnmonitorService(),
            new NoOpTrashService(), new NoOpRunLogger(), effectiveResumeStore, new NoOpProgressReporter(),
            new StaticConfigStore(config));

        var runOrchestrator = new RunOrchestrator(
            new PassThroughPathExpander(), new FixedExtensionPresetService(), conversionOrchestrator, new MetadataService(),
            effectiveResumeStore, new NoOpRunLogger(), new NoOpHistoryStore(), new NoOpHistoryRollupCalculator(),
            new NoOpHtmlReportGenerator(), new NoOpReportLauncher(), new NoOpNotificationService(), new NoOpNotificationDispatcher(),
            new NoOpTrashService(), new NoOpProgressReporter(), new ActiveRunController());

        return (runOrchestrator, resumeFilePath);
    }

    private static LaneConfig MakeLane(string id, string displayName, string inputDir, string outputDir) => new()
    {
        Id = id,
        DisplayName = displayName,
        Enabled = true,
        Input = inputDir,
        Output = outputDir,
        MoviePreset = "Any Preset"
    };

    [Fact]
    public async Task RunOnceAsync_CrossLaneOrder_InterleavesAcrossLanes_NotLaneByLane()
    {
        var laneAInput = Path.Combine(_tempDir, "LaneA_Input");
        var laneAOutput = Path.Combine(_tempDir, "LaneA_Output");
        var laneBInput = Path.Combine(_tempDir, "LaneB_Input");
        var laneBOutput = Path.Combine(_tempDir, "LaneB_Output");
        Directory.CreateDirectory(laneAInput);
        Directory.CreateDirectory(laneAOutput);
        Directory.CreateDirectory(laneBInput);
        Directory.CreateDirectory(laneBOutput);

        var aFile1 = Path.Combine(laneAInput, "a-file1.mkv");
        var aFile2 = Path.Combine(laneAInput, "a-file2.mkv");
        var bFile1 = Path.Combine(laneBInput, "b-file1.mkv");
        var bFile2 = Path.Combine(laneBInput, "b-file2.mkv");
        File.WriteAllText(aFile1, "1");
        File.WriteAllText(aFile2, "2");
        File.WriteAllText(bFile1, "3");
        File.WriteAllText(bFile2, "4");

        var laneA = MakeLane("laneA", "Lane A", laneAInput, laneAOutput);
        var laneB = MakeLane("laneB", "Lane B", laneBInput, laneBOutput);

        var config = new CompressarrConfig { Processing = new ProcessingSettings { MoveFiles = false, ClearTitleMetadata = false } };
        config.HandBrake.CliPath = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        config.HandBrake.PresetsPath = Path.Combine(_tempDir, "presets.json");
        // Defaults are literal "%CompressarrAppData%\..." tokens - PassThroughPathExpander doesn't
        // resolve them, so left unset these would create a stray relative folder (with a literal %
        // in its name) in the test runner's own working directory instead of nowhere real, but
        // still outside this test's controlled temp dir.
        config.Logging.LogFilePath = Path.Combine(_tempDir, "Logs");
        config.Report.ReportPath = Path.Combine(_tempDir, "Reports");
        config.Lanes.Add(laneA);
        config.Lanes.Add(laneB);

        var processRunner = new RecordingProcessRunner();
        var (orchestrator, resumeFilePath) = BuildOrchestrator(config, processRunner);

        // Explicit global interleave: B1 (0), A1 (1), B2 (2), A2 (3) - deliberately NOT grouped by
        // lane, so lane-by-lane processing (the old behavior) would produce a different observed
        // order than this.
        var resumeState = new List<ResumeEntry>
        {
            new() { LaneId = "laneA", FullName = aFile1, Status = ResumeStatus.Pending, Order = 1 },
            new() { LaneId = "laneA", FullName = aFile2, Status = ResumeStatus.Pending, Order = 3 },
            new() { LaneId = "laneB", FullName = bFile1, Status = ResumeStatus.Pending, Order = 0 },
            new() { LaneId = "laneB", FullName = bFile2, Status = ResumeStatus.Pending, Order = 2 }
        };
        new JsonResumeStateStore().Save(resumeState, resumeFilePath);

        var result = await orchestrator.RunOnceAsync(config);

        Assert.NotNull(result);
        Assert.Equal(4, result!.TotalFiles);
        Assert.Equal(new[] { "b-file1.mkv", "a-file1.mkv", "b-file2.mkv", "a-file2.mkv" }, processRunner.ProcessedInOrder);
    }

    [Fact]
    public async Task RunOnceAsync_OneLaneHasNoEligibleFiles_OtherLaneStillProcessesFully()
    {
        var emptyLaneInput = Path.Combine(_tempDir, "Empty_Input");
        var emptyLaneOutput = Path.Combine(_tempDir, "Empty_Output");
        var busyLaneInput = Path.Combine(_tempDir, "Busy_Input");
        var busyLaneOutput = Path.Combine(_tempDir, "Busy_Output");
        Directory.CreateDirectory(emptyLaneInput);
        Directory.CreateDirectory(emptyLaneOutput);
        Directory.CreateDirectory(busyLaneInput);
        Directory.CreateDirectory(busyLaneOutput);

        var file1 = Path.Combine(busyLaneInput, "file1.mkv");
        var file2 = Path.Combine(busyLaneInput, "file2.mkv");
        File.WriteAllText(file1, "1");
        File.WriteAllText(file2, "2");

        var emptyLane = MakeLane("empty", "Empty Lane", emptyLaneInput, emptyLaneOutput);
        var busyLane = MakeLane("busy", "Busy Lane", busyLaneInput, busyLaneOutput);

        var config = new CompressarrConfig { Processing = new ProcessingSettings { MoveFiles = false, ClearTitleMetadata = false } };
        config.HandBrake.CliPath = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        config.HandBrake.PresetsPath = Path.Combine(_tempDir, "presets.json");
        // Defaults are literal "%CompressarrAppData%\..." tokens - PassThroughPathExpander doesn't
        // resolve them, so left unset these would create a stray relative folder (with a literal %
        // in its name) in the test runner's own working directory instead of nowhere real, but
        // still outside this test's controlled temp dir.
        config.Logging.LogFilePath = Path.Combine(_tempDir, "Logs");
        config.Report.ReportPath = Path.Combine(_tempDir, "Reports");
        config.Lanes.Add(emptyLane);
        config.Lanes.Add(busyLane);

        var processRunner = new RecordingProcessRunner();
        var (orchestrator, _) = BuildOrchestrator(config, processRunner);

        var result = await orchestrator.RunOnceAsync(config);

        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalFiles);
        Assert.Equal(new[] { "file1.mkv", "file2.mkv" }, processRunner.ProcessedInOrder);
    }

    [Fact]
    public async Task RunOnceAsync_NonContiguousLaneVisits_ReportGroupsResultsCorrectlyPerLane()
    {
        var laneAInput = Path.Combine(_tempDir, "LaneA_Input");
        var laneAOutput = Path.Combine(_tempDir, "LaneA_Output");
        var laneBInput = Path.Combine(_tempDir, "LaneB_Input");
        var laneBOutput = Path.Combine(_tempDir, "LaneB_Output");
        Directory.CreateDirectory(laneAInput);
        Directory.CreateDirectory(laneAOutput);
        Directory.CreateDirectory(laneBInput);
        Directory.CreateDirectory(laneBOutput);

        var aFile1 = Path.Combine(laneAInput, "a-file1.mkv");
        var aFile2 = Path.Combine(laneAInput, "a-file2.mkv");
        var bFile1 = Path.Combine(laneBInput, "b-file1.mkv");
        File.WriteAllText(aFile1, "1");
        File.WriteAllText(aFile2, "2");
        File.WriteAllText(bFile1, "3");

        var laneA = MakeLane("laneA", "Lane A", laneAInput, laneAOutput);
        var laneB = MakeLane("laneB", "Lane B", laneBInput, laneBOutput);

        var config = new CompressarrConfig { Processing = new ProcessingSettings { MoveFiles = false, ClearTitleMetadata = false } };
        config.HandBrake.CliPath = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        config.HandBrake.PresetsPath = Path.Combine(_tempDir, "presets.json");
        // Defaults are literal "%CompressarrAppData%\..." tokens - PassThroughPathExpander doesn't
        // resolve them, so left unset these would create a stray relative folder (with a literal %
        // in its name) in the test runner's own working directory instead of nowhere real, but
        // still outside this test's controlled temp dir.
        config.Logging.LogFilePath = Path.Combine(_tempDir, "Logs");
        config.Report.ReportPath = Path.Combine(_tempDir, "Reports");
        config.Lanes.Add(laneA);
        config.Lanes.Add(laneB);

        var (orchestrator, resumeFilePath) = BuildOrchestrator(config, new RecordingProcessRunner());

        // Lane B's one file sandwiched between lane A's two - lane A is visited, then lane B, then
        // lane A again (non-contiguous).
        var resumeState = new List<ResumeEntry>
        {
            new() { LaneId = "laneA", FullName = aFile1, Status = ResumeStatus.Pending, Order = 0 },
            new() { LaneId = "laneB", FullName = bFile1, Status = ResumeStatus.Pending, Order = 1 },
            new() { LaneId = "laneA", FullName = aFile2, Status = ResumeStatus.Pending, Order = 2 }
        };
        new JsonResumeStateStore().Save(resumeState, resumeFilePath);

        var result = await orchestrator.RunOnceAsync(config);

        Assert.NotNull(result);
        var laneASection = result!.Report.Lanes.Single(l => l.LaneDisplayName == "Lane A");
        var laneBSection = result.Report.Lanes.Single(l => l.LaneDisplayName == "Lane B");
        Assert.Equal(new[] { "a-file1.mkv", "a-file2.mkv" }, laneASection.Results.Select(r => r.FileName));
        Assert.Equal(new[] { "b-file1.mkv" }, laneBSection.Results.Select(r => r.FileName));
    }

    [Fact]
    public async Task RunOnceAsync_CrossLaneReorderDuringEncode_IsHonoredNotLost()
    {
        // The most safety-critical scenario for this whole feature: a reorder landing while a file
        // is mid-encode must move a DIFFERENT lane's file ahead of one still queued in the
        // currently-encoding file's own lane - not just be honored within one lane (already covered
        // by ConversionOrchestratorTests' own single-lane version of this test).
        var laneAInput = Path.Combine(_tempDir, "LaneA_Input");
        var laneAOutput = Path.Combine(_tempDir, "LaneA_Output");
        var laneBInput = Path.Combine(_tempDir, "LaneB_Input");
        var laneBOutput = Path.Combine(_tempDir, "LaneB_Output");
        Directory.CreateDirectory(laneAInput);
        Directory.CreateDirectory(laneAOutput);
        Directory.CreateDirectory(laneBInput);
        Directory.CreateDirectory(laneBOutput);

        var episode1 = Path.Combine(laneAInput, "episode1.mkv");
        var episode2 = Path.Combine(laneAInput, "episode2.mkv");
        var movieA = Path.Combine(laneBInput, "movieA.mkv");
        File.WriteAllText(episode1, "1");
        File.WriteAllText(episode2, "2");
        File.WriteAllText(movieA, "3");

        var laneA = MakeLane("laneA", "Lane A", laneAInput, laneAOutput);
        var laneB = MakeLane("laneB", "Lane B", laneBInput, laneBOutput);

        var config = new CompressarrConfig { Processing = new ProcessingSettings { MoveFiles = false, ClearTitleMetadata = false } };
        config.HandBrake.CliPath = Path.Combine(_tempDir, "HandBrakeCLI.exe");
        config.HandBrake.PresetsPath = Path.Combine(_tempDir, "presets.json");
        // Defaults are literal "%CompressarrAppData%\..." tokens - PassThroughPathExpander doesn't
        // resolve them, so left unset these would create a stray relative folder (with a literal %
        // in its name) in the test runner's own working directory instead of nowhere real, but
        // still outside this test's controlled temp dir.
        config.Logging.LogFilePath = Path.Combine(_tempDir, "Logs");
        config.Report.ReportPath = Path.Combine(_tempDir, "Reports");
        config.Lanes.Add(laneA);
        config.Lanes.Add(laneB);

        // Must match AppPaths.GetResumeFilePath()'s real result (rooted at _tempDir via the
        // constructor's override) - a separately-computed path here was a real bug caught while
        // writing this test: it silently wrote the concurrent edit to a file RunOrchestrator never
        // actually reads from, making it look like the edit was never honored at all.
        var resumeFilePath = AppPaths.GetResumeFilePath();
        var realResumeStore = new JsonResumeStateStore();

        var initialState = new List<ResumeEntry>
        {
            new() { LaneId = "laneA", FullName = episode1, Status = ResumeStatus.Pending, Order = 0 },
            new() { LaneId = "laneA", FullName = episode2, Status = ResumeStatus.Pending, Order = 1 },
            new() { LaneId = "laneB", FullName = movieA, Status = ResumeStatus.Pending, Order = 2 }
        };
        realResumeStore.Save(initialState, resumeFilePath);

        // Fires while episode1 is still "encoding" - simulates POST /api/run/queue/reorder moving
        // lane B's movie to the very front, ahead of lane A's own still-queued episode2.
        var processRunner = new RecordingProcessRunner(sourcePath =>
        {
            if (Path.GetFileName(sourcePath) != "episode1.mkv") return;
            realResumeStore.Update(resumeFilePath, state =>
            {
                state.Single(e => e.FullName == movieA).Order = 0;
                state.Single(e => e.FullName == episode2).Order = 1;
                return true;
            });
        });
        var (orchestrator, _) = BuildOrchestrator(config, processRunner, realResumeStore);

        var result = await orchestrator.RunOnceAsync(config);

        Assert.NotNull(result);
        Assert.Equal(3, result!.TotalFiles);
        Assert.Equal(new[] { "episode1.mkv", "movieA.mkv", "episode2.mkv" }, processRunner.ProcessedInOrder);

        var finalState = realResumeStore.Load(resumeFilePath);
        Assert.All(finalState, e => Assert.Equal(ResumeStatus.Completed, e.Status));
    }
}
