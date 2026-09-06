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
    private static readonly string[] CompanionExtensions = { "srt" };

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void MoveCompanionFiles_OtherVideoStillInFolder_StillMovesThisFilesOwnCompanion()
    {
        // Mirrors the real production layout: a whole season's episodes and subtitles sitting
        // flat in one shared lane Input folder (no per-episode subfolders). Reported live: waiting
        // for "this is the only video left" before moving anything meant a file's own subtitle sat
        // stranded until the WHOLE batch finished - or never, if a sibling was skipped/removed, or
        // even just because Stop Monitoring landed right after this one file finished. Companions
        // now move per-file, by name match, regardless of what else is still in the folder.
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

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, Path.Combine(_tempDir, "Input"), CompanionExtensions, DeleteAfterConvertMode.Recycle);

        Assert.True(File.Exists(Path.Combine(destDir, "episode1.srt")), "this file's own subtitle must move even while another video is still here");
        Assert.True(File.Exists(otherVideo), "the other, still-untouched video must survive");
    }

    [Fact]
    public void MoveCompanionFiles_AnotherVideosOwnCompanion_IsNeverTouchedByThisFilesMove()
    {
        // Stem-based matching must stay scoped to THIS file only - confirmed live, an earlier
        // version of this logic swept a sibling's own subtitle right along with the real one the
        // moment some "is it safe to sweep now" guard passed.
        var sourceDir = Path.Combine(_tempDir, "Input");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "episode1.mkv");
        var convertedSubtitle = Path.Combine(sourceDir, "episode1.eng.srt");
        var otherVideo = Path.Combine(sourceDir, "episode2.mkv");
        var otherSubtitle = Path.Combine(sourceDir, "episode2.eng.srt");
        File.WriteAllText(converted, "x");
        File.WriteAllText(convertedSubtitle, "x");
        File.WriteAllText(otherVideo, "x");
        File.WriteAllText(otherSubtitle, "x");

        var destDir = Path.Combine(_tempDir, "Output");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, sourceDir, CompanionExtensions, DeleteAfterConvertMode.Recycle);

        Assert.True(File.Exists(Path.Combine(destDir, "episode1.eng.srt")), "the processed file's own subtitle must move");
        Assert.True(File.Exists(otherVideo), "the other video must survive");
        Assert.True(File.Exists(otherSubtitle), "the other video's OWN subtitle must survive too - not this file's to touch");
        Assert.False(File.Exists(Path.Combine(destDir, "episode2.eng.srt")), "the other subtitle must not be moved to destination");
    }

    [Fact]
    public void MoveCompanionFiles_OtherVideoStillInFolder_FolderIsNotDeleted()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var seasonDir = Path.Combine(inputRoot, "Show", "Season 01");
        Directory.CreateDirectory(seasonDir);
        var converted = Path.Combine(seasonDir, "episode1.mkv");
        var otherVideo = Path.Combine(seasonDir, "episode2.mkv");
        File.WriteAllText(converted, "x");
        File.WriteAllText(otherVideo, "x");
        File.Delete(converted); // simulates the file already having been moved out by FileRouter

        var destDir = Path.Combine(_tempDir, "Output", "Show", "Season 01");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Recycle);

        Assert.True(File.Exists(otherVideo), "the other, still-untouched video must survive");
        Assert.True(Directory.Exists(seasonDir), "a folder still holding another video is not really empty");
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

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Maintain, Path.Combine(_tempDir, "Input"), CompanionExtensions, DeleteAfterConvertMode.Recycle);

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

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Recycle);

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

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Recycle, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Recycle);

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

        _service.MoveCompanionFiles(converted, seasonDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Recycle);

        Assert.False(Directory.Exists(seasonDir));
        Assert.True(Directory.Exists(showDir), "must not remove a parent that still has other content");
    }

    [Fact]
    public void MoveCompanionFiles_StemMatchedFileNotOnCompanionList_IsRemovedNotMoved()
    {
        // The bug this whole feature exists to fix: a stem-matched file that isn't a recognized
        // companion type must never be blindly moved (or, in the old design, blindly deleted
        // along with genuinely unrelated content) - it's disposed of per unmatchedCompanionAction
        // instead, independent of what companion types are actually configured.
        var inputRoot = Path.Combine(_tempDir, "Input");
        var sourceDir = Path.Combine(inputRoot, "Movie");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "movie.mkv");
        var subtitle = Path.Combine(sourceDir, "movie.srt");
        var unwantedStemMatch = Path.Combine(sourceDir, "movie.bak");
        File.WriteAllText(converted, "x");
        File.WriteAllText(subtitle, "x");
        File.WriteAllText(unwantedStemMatch, "x");

        var destDir = Path.Combine(_tempDir, "Output", "Movie");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Recycle);

        Assert.True(File.Exists(Path.Combine(destDir, "movie.srt")), "a recognized companion type must still move");
        Assert.False(File.Exists(unwantedStemMatch), "a stem-matched file not on the companion list must be removed per unmatchedCompanionAction");
        Assert.False(File.Exists(Path.Combine(destDir, "movie.bak")), "an unrecognized file type must never be moved to the destination");
    }

    [Fact]
    public void MoveCompanionFiles_UnmatchedActionMaintain_LeavesUnrecognizedFileAndFolderInPlace()
    {
        var inputRoot = Path.Combine(_tempDir, "Input");
        var sourceDir = Path.Combine(inputRoot, "Movie");
        Directory.CreateDirectory(sourceDir);
        var converted = Path.Combine(sourceDir, "movie.mkv");
        File.WriteAllText(converted, "x");
        File.Delete(converted); // simulates the file already having been moved out by FileRouter

        // Genuinely unrelated content the user placed here on purpose - not a companion of any
        // video at all.
        var unrelated = Path.Combine(sourceDir, "notes.txt");
        File.WriteAllText(unrelated, "personal notes, not a companion file");

        var destDir = Path.Combine(_tempDir, "Output", "Movie");
        Directory.CreateDirectory(destDir);

        _service.MoveCompanionFiles(converted, sourceDir, destDir, VidTypes, DeleteAfterConvertMode.Delete, inputRoot, CompanionExtensions, DeleteAfterConvertMode.Maintain);

        Assert.True(File.Exists(unrelated), "unmatchedCompanionAction=Maintain must leave unrecognized content untouched");
        Assert.True(Directory.Exists(sourceDir), "the folder must not be deleted while it still holds content left in place on purpose");
    }
}
