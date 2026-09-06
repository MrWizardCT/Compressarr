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

public class FileBotRunnerTests
{
    [Fact]
    public void ComputeUnmatched_FileInBothBeforeAndAfter_IsUnmatched()
    {
        var before = new HashSet<string> { @"C:\Input\Show.mkv" };
        var after = new HashSet<string> { @"C:\Input\Show.mkv" };

        var result = FileBotRunner.ComputeUnmatched(before, after);

        Assert.Contains(@"C:\Input\Show.mkv", result);
    }

    [Fact]
    public void ComputeUnmatched_FileOnlyInAfter_IsNotUnmatched()
    {
        // Present after but not before doesn't happen for a rename-in-place (the old path
        // vanishes), but a file dropped into Input mid-run shouldn't be flagged either way -
        // only paths untouched across the whole before/after window count.
        var before = new HashSet<string>();
        var after = new HashSet<string> { @"C:\Input\New.mkv" };

        var result = FileBotRunner.ComputeUnmatched(before, after);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeUnmatched_FileRenamedByFileBot_IsNotUnmatched()
    {
        // The old (before-only) path is gone; the new (after-only) path is a fresh name FileBot
        // produced - neither should show up as "unmatched," since FileBot clearly acted on it.
        var before = new HashSet<string> { @"C:\Input\Show.S01E01.mkv" };
        var after = new HashSet<string> { @"C:\Input\Show - S01E01 - Title.mkv" };

        var result = FileBotRunner.ComputeUnmatched(before, after);

        Assert.Empty(result);
    }

    [Fact]
    public void ComputeUnmatched_IsCaseInsensitive()
    {
        var before = new HashSet<string> { @"C:\Input\Show.mkv" };
        var after = new HashSet<string> { @"c:\input\SHOW.MKV" };

        var result = FileBotRunner.ComputeUnmatched(before, after);

        Assert.Single(result);
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
}
