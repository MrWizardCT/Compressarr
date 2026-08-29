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

        var dest = router.MoveTvFile(source, outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "MASH", "Season 04", "MASH.S04E09.mkv"), dest);
        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void MoveTvFile_NoSeasonEpisode_ReturnsNull()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Not A TV Episode.mkv");

        var dest = router.MoveTvFile(source, Path.Combine(_tempDir, "TV"));

        Assert.Null(dest);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveMovieFile_NoExistingMovieFolders_UsesOutputBaseDirectly()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");

        var dest = router.MoveMovieFile(source, outputBase)!;

        Assert.Equal(Path.Combine(outputBase, "Caddyshack (1980)", "Caddyshack (1980).mkv"), dest);
    }

    [Fact]
    public void MoveMovieFile_SingleMovieFolder_UsesItUnconditionally()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var movieFolder = Path.Combine(outputBase, "01. Movies");
        Directory.CreateDirectory(movieFolder);

        var dest = router.MoveMovieFile(source, outputBase)!;

        Assert.StartsWith(movieFolder, dest);
    }

    [Fact]
    public void MoveMovieFile_MultipleYearRangeFolders_PicksMatchingRange()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Caddyshack (1980).mkv");
        var outputBase = Path.Combine(_tempDir, "Movies");
        var oldRange = Path.Combine(outputBase, "01. Movies 1920-1979");
        var matchingRange = Path.Combine(outputBase, "02. Movies 1980-1999");
        Directory.CreateDirectory(oldRange);
        Directory.CreateDirectory(matchingRange);

        var dest = router.MoveMovieFile(source, outputBase)!;

        Assert.StartsWith(matchingRange, dest);
    }

    [Fact]
    public void RouteFile_MoveFilesFalse_ReturnsNullAndLeavesFileInPlace()
    {
        var router = new FileRouter();
        var source = CreateSourceFile("Show.S01E01.mkv");

        var result = router.RouteFile(source, isTv: true, Path.Combine(_tempDir, "TV"), Path.Combine(_tempDir, "Movies"), moveFiles: false);

        Assert.Null(result);
        Assert.True(File.Exists(source));
    }
}
