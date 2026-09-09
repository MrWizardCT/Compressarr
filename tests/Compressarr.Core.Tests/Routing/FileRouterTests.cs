using Compressarr.Core.Config;
using Compressarr.Core.Routing;

namespace Compressarr.Core.Tests.Routing;

public class FileRouterTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-router-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string CreateSourceFile(string name)
    {
        var path = Path.Combine(_tempDir, "source", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "data");
        return path;
    }

    [Fact]
    public void MoveTvFile_RoutesToShowSeasonFolder()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("MASH.S04E09.mkv");
        var outputBase = Path.Combine(_tempDir, "TV");

        var dest = router.MoveTvFile(source, "MASH.S04E09.mkv", outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "MASH", "Season 04", "MASH.S04E09.mkv"), dest);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void MoveTvFile_SourcePathDiffersFromDesiredFileName_ClassifiesByDesiredNameOnly()
    {
        // Pins the v2.1.3 code review's finding #4 fix: the physical file being moved (sourcePath)
        // can carry a collision-safe staging name unrelated to the show/episode it actually is -
        // classification and the destination leaf name must come from desiredFileName alone, never
        // from sourcePath's own name.
        var router = new FileRouter();
        var source = CreateSourceFile("MASH.S04E09.compressarr-a1b2c3d4.mkv");
        var outputBase = Path.Combine(_tempDir, "TV");

        var dest = router.MoveTvFile(source, "MASH.S04E09.mkv", outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "MASH", "Season 04", "MASH.S04E09.mkv"), dest);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void MoveTvFile_BasePathUnavailable_ThrowsAndLeavesSourceFileInPlace()
    {
        // Simulates an offline/unreachable network drive - a drive letter that doesn't exist
        // fails Directory.CreateDirectory the same way a mapped drive being offline does.
        var router = new FileRouter();
        var source = CreateSourceFile("MASH.S04E09.mkv");

        var ex = Assert.ThrowsAny<Exception>(() => router.MoveTvFile(source, "MASH.S04E09.mkv", @"Z:\Unavailable\TV"));

        // ConversionOrchestrator relies on this being catchable and the source file being
        // untouched afterward - RouteFile itself has no try/catch, so both matter here.
        Assert.IsNotType<InvalidOperationException>(ex); // that's reserved for "not configured"
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveMovieFile_BasePathUnavailable_ThrowsAndLeavesSourceFileInPlace()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");

        var ex = Assert.ThrowsAny<Exception>(() => router.MoveMovieFile(source, "Caddyshack (1980).mkv", @"Z:\Unavailable\Movies"));

        Assert.IsNotType<InvalidOperationException>(ex);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveTvFile_NoSeasonEpisode_ReturnsNull()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Not A TV Episode.mkv");

        var dest = router.MoveTvFile(source, "Not A TV Episode.mkv", Path.Combine(_tempDir, "TV"));

        Assert.Null(dest);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveMovieFile_NoExistingMovieFolders_UsesOutputBaseDirectly()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "Caddyshack (1980)", "Caddyshack (1980).mkv"), dest);
    }

    [Fact]
    public void MoveMovieFile_ExistingUnrelatedMovieFolderPresent_StillUsesOutputBaseDirectly()
    {
        // Regression test: MoveMovieFile used to auto-detect a single folder anywhere under
        // outputBase whose name merely contained "movie" and treat it as a bucket, nesting
        // every subsequent movie inside it. "Scary Movie (2026)" matched that filter purely by
        // having "Movie" in its own title, silently misrouting real files in production. Movies
        // must always land directly under outputBase in their own folder, regardless of what
        // other movie folders already exist there.
        var router = new FileRouter();
        var source = CreateSourceFile("The Runner (2026).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");
        Directory.CreateDirectory(Path.Combine(outputBase, "Scary Movie (2026)"));

        var dest = router.MoveMovieFile(source, "The Runner (2026).mkv", outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "The Runner (2026)", "The Runner (2026).mkv"), dest);
    }

    [Fact]
    public void MoveMovieFile_DestinationAlreadyExists_OverwritesWithoutError()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        File.WriteAllText(source, "NEW CONTENT");
        var outputBase = Path.Combine(_tempDir, "Movies");

        // A prior conversion already placed a file at the exact spot this one is about to land -
        // e.g. the same movie converted a second time.
        var existingDestFolder = Path.Combine(outputBase, "Caddyshack (1980)");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "Caddyshack (1980).mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase)!;

        Assert.Equal(existingDestPath, dest);
        Assert.False(File.Exists(source));
        Assert.Equal("NEW CONTENT", File.ReadAllText(dest));
    }

    [Fact]
    public void MoveTvFile_DestinationAlreadyExists_OverwritesWithoutError()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("MASH.S04E09.mkv");
        File.WriteAllText(source, "NEW CONTENT");
        var outputBase = Path.Combine(_tempDir, "TV");

        var existingDestFolder = Path.Combine(outputBase, "MASH", "Season 04");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "MASH.S04E09.mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        var dest = router.MoveTvFile(source, "MASH.S04E09.mkv", outputBase)!;

        Assert.Equal(existingDestPath, dest);
        Assert.False(File.Exists(source));
        Assert.Equal("NEW CONTENT", File.ReadAllText(dest));
    }

    [Fact]
    public void RouteFile_MoveFilesFalse_ReturnsNullAndLeavesFileInPlace()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Show.S01E01.mkv");

        var result = router.RouteFile(source, "Show.S01E01.mkv", isTv: true, Path.Combine(_tempDir, "TV"), Path.Combine(_tempDir, "Movies"), moveFiles: false);

        Assert.Null(result);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveMovieFile_CollisionModeSkip_ThrowsAndLeavesBothFilesUntouched()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        File.WriteAllText(source, "NEW CONTENT");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var existingDestFolder = Path.Combine(outputBase, "Caddyshack (1980)");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "Caddyshack (1980).mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        var ex = Assert.Throws<DestinationCollisionSkippedException>(
            () => router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase, DestinationCollisionMode.Skip));

        Assert.Contains(existingDestPath, ex.Message);
        Assert.True(File.Exists(source));
        Assert.Equal("NEW CONTENT", File.ReadAllText(source));
        Assert.Equal("OLD CONTENT", File.ReadAllText(existingDestPath));
    }

    [Fact]
    public void MoveTvFile_CollisionModeSkip_ThrowsAndLeavesBothFilesUntouched()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("MASH.S04E09.mkv");
        var outputBase = Path.Combine(_tempDir, "TV");
        var existingDestFolder = Path.Combine(outputBase, "MASH", "Season 04");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "MASH.S04E09.mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        Assert.Throws<DestinationCollisionSkippedException>(
            () => router.MoveTvFile(source, "MASH.S04E09.mkv", outputBase, DestinationCollisionMode.Skip));

        Assert.True(File.Exists(source));
        Assert.True(File.Exists(existingDestPath));
        Assert.Equal("OLD CONTENT", File.ReadAllText(existingDestPath));
    }

    [Fact]
    public void MoveMovieFile_CollisionModeRename_UsesNextAvailableSuffix()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        File.WriteAllText(source, "NEW CONTENT");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var existingDestFolder = Path.Combine(outputBase, "Caddyshack (1980)");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "Caddyshack (1980).mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase, DestinationCollisionMode.Rename)!;

        Assert.Equal(Path.Combine(existingDestFolder, "Caddyshack (1980) (2).mkv"), dest);
        Assert.False(File.Exists(source));
        Assert.Equal("NEW CONTENT", File.ReadAllText(dest));
        Assert.Equal("OLD CONTENT", File.ReadAllText(existingDestPath)); // original untouched
    }

    [Fact]
    public void MoveMovieFile_CollisionModeRename_SkipsAlreadyTakenSuffixes()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var existingDestFolder = Path.Combine(outputBase, "Caddyshack (1980)");
        Directory.CreateDirectory(existingDestFolder);
        File.WriteAllText(Path.Combine(existingDestFolder, "Caddyshack (1980).mkv"), "1");
        File.WriteAllText(Path.Combine(existingDestFolder, "Caddyshack (1980) (2).mkv"), "2");
        File.WriteAllText(Path.Combine(existingDestFolder, "Caddyshack (1980) (3).mkv"), "3");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase, DestinationCollisionMode.Rename)!;

        Assert.Equal(Path.Combine(existingDestFolder, "Caddyshack (1980) (4).mkv"), dest);
    }

    [Fact]
    public void MoveMovieFile_CollisionModeOverwrite_MatchesDefaultBehavior()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        File.WriteAllText(source, "NEW CONTENT");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var existingDestFolder = Path.Combine(outputBase, "Caddyshack (1980)");
        Directory.CreateDirectory(existingDestFolder);
        var existingDestPath = Path.Combine(existingDestFolder, "Caddyshack (1980).mkv");
        File.WriteAllText(existingDestPath, "OLD CONTENT");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase, DestinationCollisionMode.Overwrite)!;

        Assert.Equal(existingDestPath, dest);
        Assert.Equal("NEW CONTENT", File.ReadAllText(dest));
    }

    [Fact]
    public void MoveMovieFile_NoCollision_RenameModeUsesPlainPath()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");

        var dest = router.MoveMovieFile(source, "Caddyshack (1980).mkv", outputBase, DestinationCollisionMode.Rename)!;

        Assert.Equal(Path.Combine(outputBase, "Caddyshack (1980)", "Caddyshack (1980).mkv"), dest);
    }
}
