using Compressarr.Core.Config;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Tests.Routing;

/// <summary>Deletes for real (both Delete and Recycle modes) so tests can assert on actual
/// filesystem state without depending on OS recycle-bin behavior — CompanionFileService's own
/// routing/guard logic is what's under test here, not the trash backend.</summary>
file sealed class FakeTrashService : ITrashService
{
    public void DeleteFile(string path, DeleteAfterConvertMode mode)
    {
        if (mode == DeleteAfterConvertMode.Maintain) return;
        if (File.Exists(path)) File.Delete(path);
    }

    public void DeleteFolder(string path, DeleteAfterConvertMode mode)
    {
        if (mode == DeleteAfterConvertMode.Maintain) return;
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}

public class CompanionFileServiceTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-companion-tests-").FullName;
    private readonly CompanionFileService _service = new(new FakeTrashService());
    private static readonly string[] VidTypes = { "mkv", "mp4" };

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void MoveCompanionFiles_OtherVideoStillInFolder_LeavesEverythingAlone()
    {
        var sourceDir = Path.Combine(_tempDir, "Input", "Show");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "episode1.mkv");
        var otherVideo = Path.Combine(sourceDir, "episode2.mkv");
        var subtitle = Path.Combine(sourceDir, "episode1.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(otherVideo, "x");
        File.WriteAllText(subtitle, "x");

        var destDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, Path.Combine(_tempDir, "Input"));

        // Shared/flat folder - nothing touched except the file already handled elsewhere.
        Assert.True(File.Exists(otherVideo));
        Assert.True(File.Exists(subtitle));
        Assert.False(File.Exists(Path.Combine(destDir, "episode1.srt")));
    }

    [Fact]
    public void MoveCompanionFiles_OtherVideoIsHeldBack_MovesSiblingsAnyway()
    {
        // Mirrors the real production layout: a whole season's episodes and subtitles sitting
        // flat in one shared lane Input folder (no per-episode subfolders) - a permanently
        // Skipped/Removed sibling video must never block this file's own subtitle from moving,
        // the way an "other video still here, must not be done yet" guard alone would assume.
        var sourceDir = Path.Combine(_tempDir, "Input");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "episode1.mkv");
        var heldBack = Path.Combine(sourceDir, "episode2.mkv");
        var subtitle = Path.Combine(sourceDir, "episode1.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(heldBack, "x");
        File.WriteAllText(subtitle, "x");

        var destDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, sourceDir,
            new HashSet<string> { heldBack });

        Assert.True(File.Exists(Path.Combine(destDir, "episode1.srt")), "subtitle must move despite the held-back sibling");
        Assert.True(File.Exists(heldBack), "the held-back sibling video must never itself be moved or deleted");
        Assert.True(Directory.Exists(sourceDir), "the shared Input folder itself must survive");
    }

    [Fact]
    public void MoveCompanionFiles_HeldBackVideosOwnSubtitle_IsNeverSweptEitherEvenWhenAnotherSweepFires()
    {
        // Confirmed live on the real running app: the first version of this fix protected the
        // held-back VIDEO from being swept, but not ITS OWN subtitle - "episode2.eng.srt" got
        // moved to destination right alongside the real siblings the moment the guard passed,
        // orphaning a subtitle for a video that was never actually converted.
        var sourceDir = Path.Combine(_tempDir, "Input");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "episode1.mkv");
        var convertedSubtitle = Path.Combine(sourceDir, "episode1.eng.srt");
        var heldBack = Path.Combine(sourceDir, "episode2.mkv");
        var heldBackSubtitle = Path.Combine(sourceDir, "episode2.eng.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(convertedSubtitle, "x");
        File.WriteAllText(heldBack, "x");
        File.WriteAllText(heldBackSubtitle, "x");

        var destDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, sourceDir,
            new HashSet<string> { heldBack });

        Assert.True(File.Exists(Path.Combine(destDir, "episode1.eng.srt")), "the processed file's own subtitle must still move");
        Assert.True(File.Exists(heldBack), "the held-back video must survive");
        Assert.True(File.Exists(heldBackSubtitle), "the held-back video's OWN subtitle must survive too, not just the video itself");
        Assert.False(File.Exists(Path.Combine(destDir, "episode2.eng.srt")), "the held-back subtitle must not be moved to destination");
    }

    [Fact]
    public void MoveCompanionFiles_HeldBackSiblingInSubfolder_SurvivesAndFolderIsNotDeleted()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var seasonDir = Path.Combine(inputRoot, "Show", "Season 01");
        Directory.CreateDirectory(seasonDir);
        var converted = Path.Combine(seasonDir, "episode1.mkv");
        var heldBack = Path.Combine(seasonDir, "episode2.mkv");
        File.WriteAllText(converted, "x");
        File.WriteAllText(heldBack, "x");
        File.Delete(converted); // simulates the file already having been moved out by FileRouter

        var destDir = Path.Combine(_tempDir, "Output", "Show", "Season 01");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot,
            new HashSet<string> { heldBack });

        Assert.True(File.Exists(heldBack), "the held-back sibling video must never itself be moved or deleted");
        Assert.True(Directory.Exists(seasonDir), "a folder still holding a held-back file is not really empty");
    }

    [Fact]
    public void MoveCompanionFiles_MaintainMode_CopiesSiblingsAndLeavesSourceUntouched()
    {
        var sourceDir = Path.Combine(_tempDir, "Input", "Movie");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "movie.mkv");
        var subtitle = Path.Combine(sourceDir, "movie.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(subtitle, "x");

        var destDir = Path.Combine(_tempDir, "Output", "Movie");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Maintain, Path.Combine(_tempDir, "Input"));

        Assert.True(File.Exists(subtitle), "source sibling must survive Maintain mode");
        Assert.True(File.Exists(Path.Combine(destDir, "movie.srt")), "sibling must be copied to destination");
        Assert.True(Directory.Exists(sourceDir), "source folder must survive Maintain mode");
    }

    [Fact]
    public void MoveCompanionFiles_DeleteMode_MovesSiblingsAndRemovesEmptySourceFolder()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var sourceDir = Path.Combine(inputRoot, "Movie");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "movie.mkv");
        var subtitle = Path.Combine(sourceDir, "movie.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(subtitle, "x");
        File.Delete(converted); // simulates the file already having been moved out by FileRouter

        var destDir = Path.Combine(_tempDir, "Output", "Movie");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot);

        Assert.True(File.Exists(Path.Combine(destDir, "movie.srt")));
        Assert.False(Directory.Exists(sourceDir), "now-empty source folder must be removed");
        Assert.True(Directory.Exists(inputRoot), "the lane's input root itself must never be removed");
    }

    [Fact]
    public void MoveCompanionFiles_DeleteMode_CascadesUpwardButStopsAtInputRoot()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var showDir = Path.Combine(inputRoot, "Show");
        var seasonDir = Path.Combine(showDir, "Season 01");
        Directory.CreateDirectory(seasonDir);
        var converted = Path.Combine(seasonDir, "episode1.mkv");
        File.WriteAllText(converted, "x");
        File.Delete(converted);

        var destDir = Path.Combine(_tempDir, "Output", "Show", "Season 01");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Recycle, inputRoot);

        Assert.False(Directory.Exists(seasonDir));
        Assert.False(Directory.Exists(showDir), "empty parent (Show\\) must cascade-remove once Season 01 was its only content");
        Assert.True(Directory.Exists(inputRoot), "cascade must stop at the input root");
    }

    [Fact]
    public void MoveCompanionFiles_DeleteMode_CascadeStopsAtFirstNonEmptyAncestor()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var showDir = Path.Combine(inputRoot, "Show");
        var seasonDir = Path.Combine(showDir, "Season 01");
        Directory.CreateDirectory(seasonDir);
        var converted = Path.Combine(seasonDir, "episode1.mkv");
        File.WriteAllText(converted, "x");
        File.Delete(converted);

        // Show\ has other unrelated content (e.g. Season 02), so the cascade must stop there.
        Directory.CreateDirectory(Path.Combine(showDir, "Season 02"));

        var destDir = Path.Combine(_tempDir, "Output", "Show", "Season 01");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot);

        Assert.False(Directory.Exists(seasonDir));
        Assert.True(Directory.Exists(showDir), "must not remove a parent that still has other content");
    }
}
