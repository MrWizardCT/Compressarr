using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.FileBot;
using Compressarr.Core.Logging;

namespace Compressarr.Core.Tests.FileBot;

file sealed class RecordingRunLogger : IRunLogger
{
    public event Action<string, LogSeverity>? LineWritten;
    public List<(string Message, LogSeverity Severity)> Logs { get; } = new();

    public string Initialize(string logFilePath, string timestamp) => "";
    public void Log(string message, LogSeverity severity = LogSeverity.Info) => Logs.Add((message, severity));
    public void FileStart(string laneDisplayName, int index, int total, string fileName, double sizeGb, string contentType, string preset) { }
    public void FileComplete(string fileName, double beginSizeGb, double endSizeGb, TimeSpan duration, bool success, string? detailLogFile) { }
}

file sealed class EmptyScanner : IVideoFileScanner
{
    public IReadOnlyList<FileInfo> FindVideoFiles(string inputPath, IReadOnlyList<string> vidTypes, long minSizeBytes, int limit) =>
        Array.Empty<FileInfo>();
}

file sealed class RealFolderScanner : IVideoFileScanner
{
    public IReadOnlyList<FileInfo> FindVideoFiles(string inputPath, IReadOnlyList<string> vidTypes, long minSizeBytes, int limit) =>
        Directory.GetFiles(inputPath).Select(f => new FileInfo(f)).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
}

public class FileBotRunnerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-filebot-tests-").FullName;
    private static readonly string CmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void Run_Disabled_ReturnsEmptySetAndNeverLogs()
    {
        var runner = new FileBotRunner(new EmptyScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = false, CliPath = @"C:\does\not\exist.exe" };

        var result = runner.Run(settings, @"C:\Input", new List<string> { "mkv" }, logger);

        Assert.Empty(result);
        Assert.Empty(logger.Logs);
    }

    [Fact]
    public void Run_EnabledButCliPathMissing_ReturnsEmptySetAndLogsError()
    {
        var runner = new FileBotRunner(new EmptyScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = @"C:\does\not\exist.exe" };

        var result = runner.Run(settings, @"C:\Input", new List<string> { "mkv" }, logger);

        Assert.Empty(result);
        Assert.Contains(logger.Logs, l => l.Severity == LogSeverity.Error);
    }

    [Fact]
    public void Run_TvFilesWithBlankTvArgs_ReturnsThemUnmatchedAndLogsWarningWithoutStartingAProcess()
    {
        // Regression: a bare "filebot.exe" launch with no arguments at all only opens FileBot's
        // own GUI - confirmed live. Blank Args for a group that has files must be treated as "not
        // configured," never actually invoked.
        var tvPath = CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = "   " };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.Contains(tvPath, result);
        Assert.Contains(logger.Logs, l => l.Severity == LogSeverity.Error && l.Message.Contains("TV"));
        Assert.True(File.Exists(tvPath));
    }

    [Fact]
    public void Run_MovieFilesWithBlankMovieArgs_ReturnsThemUnmatchedAndLogsWarning()
    {
        var moviePath = CreateFile("Some Movie (2020).mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, MovieArgs = "" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.Contains(moviePath, result);
        Assert.Contains(logger.Logs, l => l.Severity == LogSeverity.Error && l.Message.Contains("movie"));
    }

    [Fact]
    public void Run_ClassifiesTvAndMovieFilesIntoSeparateGroups()
    {
        // Both groups left unconfigured (blank Args) so nothing actually invokes a process - the
        // per-group "N file(s) left as-is" counts are what prove classification routed each file
        // to the right bucket, not a shared/miscounted one.
        CreateFile("Show.S01E01.mkv");
        CreateFile("Some Movie (2020).mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = "", MovieArgs = "" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.Equal(2, result.Count);
        Assert.Contains(logger.Logs, l => l.Message.Contains("1 TV file"));
        Assert.Contains(logger.Logs, l => l.Message.Contains("1 movie file"));
    }

    [Fact]
    public void Run_TvDisabled_SkipsTvFilesSilentlyEvenWithArgsConfigured()
    {
        var tvPath = CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvEnabled = false, TvArgs = "/c exit 0 {files}" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.DoesNotContain(tvPath, result);
        Assert.Empty(logger.Logs);
        Assert.True(File.Exists(tvPath));
    }

    [Fact]
    public void Run_ExternalToolRemovesFile_IsNotFlaggedUnmatched()
    {
        // A real (trivial) external process standing in for FileBot actually renaming/moving a
        // file - proves the new "still exists at its original path" unmatched check for real,
        // not just against synthetic path sets.
        var tvPath = CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = $"/c del \"{tvPath}\"" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.DoesNotContain(tvPath, result);
        Assert.False(File.Exists(tvPath));
    }

    [Fact]
    public void Run_ExternalToolLeavesFileAlone_IsFlaggedUnmatched()
    {
        var tvPath = CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = "/c exit 0" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.Contains(tvPath, result);
    }

    [Fact]
    public void Run_ExternalToolMentionsFileButLeavesItInPlace_IsNotFlaggedUnmatched()
    {
        // Regression: FileBot confirms an already-correctly-named file via "[MOVE] Skipped [X]
        // because [X] already exists" - the path never changes on disk, but FileBot DID engage
        // with it, so this must not be flagged unmatched just because nothing moved. Found live:
        // every file Compressarr had already correctly renamed on an earlier pass showed
        // "Unmatched" on every subsequent re-scan, even though FileBot was working correctly.
        var tvPath = CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = $"/c echo Skipped \"{tvPath}\" because already exists" };

        var result = runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.DoesNotContain(tvPath, result);
        Assert.True(File.Exists(tvPath));
    }

    [Fact]
    public void Run_NonZeroExitWithoutErrorMarker_DoesNotLogAsError()
    {
        // Regression: FileBot exits non-zero even for a completely benign "nothing to do" outcome
        // (every file already correctly named) - confirmed live that case's own output never
        // includes FileBot's own "Error (o_O)" failure marker, unlike a genuine failure. Logging
        // every non-zero exit as an Error made a totally fine outcome look like something broke.
        CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = "/c \"echo Processed 0 files & exit /b 1\"" };

        runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.DoesNotContain(logger.Logs, l => l.Severity == LogSeverity.Error && l.Message.Contains("Exited with code"));
    }

    [Fact]
    public void Run_NonZeroExitWithErrorMarker_LogsAsError()
    {
        CreateFile("Show.S01E01.mkv");
        var runner = new FileBotRunner(new RealFolderScanner());
        var logger = new RecordingRunLogger();
        var settings = new FileBotSettings { Enabled = true, CliPath = CmdExe, TvArgs = "/c \"echo Error (o_O) & exit /b 1\"" };

        runner.Run(settings, _tempDir, new List<string> { "mkv" }, logger);

        Assert.Contains(logger.Logs, l => l.Severity == LogSeverity.Error && l.Message.Contains("Exited with code"));
    }
}
