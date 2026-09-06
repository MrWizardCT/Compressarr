using Compressarr.Core.Config;
using Compressarr.Core.Orchestration;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Tests.Orchestration;

file sealed class FakeTrashService : ITrashService
{
    public void DeleteFile(string path, DeleteAfterConvertMode mode)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteFolder(string path, DeleteAfterConvertMode mode)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}

public class RetentionCleanerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-retention-tests-").FullName;
    private readonly ITrashService _trash = new FakeTrashService();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateOldFile(string name, int daysOld)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "x");
        File.SetCreationTime(path, DateTime.Now.AddDays(-daysOld));
        return path;
    }

    [Fact]
    public void CleanUp_ZeroRetentionDays_KeepsEverything()
    {
        // 0 must mean "keep forever," not "delete everything right now" - a plain
        // DateTime.Now.AddDays(0) cutoff would otherwise catch every existing file, since
        // virtually all of them have CreationTime < now.
        var oldFile = CreateOldFile("old.log", daysOld: 365);
        var newFile = CreateOldFile("new.log", daysOld: 0);

        RetentionCleaner.CleanUp(_trash, _tempDir, new[] { ".log" }, retentionDays: 0, "log");

        Assert.True(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void CleanUp_NegativeRetentionDays_KeepsEverything()
    {
        var oldFile = CreateOldFile("old.log", daysOld: 365);

        RetentionCleaner.CleanUp(_trash, _tempDir, new[] { ".log" }, retentionDays: -1, "log");

        Assert.True(File.Exists(oldFile));
    }

    [Fact]
    public void CleanUp_PositiveRetentionDays_RemovesOnlyFilesOlderThanCutoff()
    {
        var oldFile = CreateOldFile("old.log", daysOld: 10);
        var newFile = CreateOldFile("new.log", daysOld: 1);

        RetentionCleaner.CleanUp(_trash, _tempDir, new[] { ".log" }, retentionDays: 5, "log");

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
    }

    [Fact]
    public void CleanUp_PositiveRetentionDays_IgnoresNonMatchingExtensions()
    {
        var oldOther = CreateOldFile("old.txt", daysOld: 10);

        RetentionCleaner.CleanUp(_trash, _tempDir, new[] { ".log" }, retentionDays: 5, "log");

        Assert.True(File.Exists(oldOther), "an old file with a non-matching extension must be left alone");
    }
}
