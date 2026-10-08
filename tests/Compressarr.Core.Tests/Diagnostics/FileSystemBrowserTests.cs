using Compressarr.Core.Diagnostics;

namespace Compressarr.Core.Tests.Diagnostics;

public class FileSystemBrowserTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-browse-tests-").FullName;
    private readonly FileSystemBrowser _browser = new();

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Browse_NullPath_ReturnsRootsNotAnException()
    {
        var result = _browser.Browse(null);

        Assert.Null(result.CurrentPath);
        Assert.Null(result.ParentPath);
        Assert.NotEmpty(result.Directories);
    }

    [Fact]
    public void Browse_IncludeFiles_ListsProgramsAndStartsInAFilesFolder()
    {
        var exe = Path.Combine(_tempDir, "tool.exe");
        File.WriteAllText(exe, "x");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "x");

        var byFile = _browser.Browse(exe, includeFiles: true);
        var plain = _browser.Browse(_tempDir);

        Assert.Equal(_tempDir, byFile.CurrentPath);
        Assert.Contains(byFile.Files, f => f.Name == "tool.exe");
        if (OperatingSystem.IsWindows()) Assert.DoesNotContain(byFile.Files, f => f.Name == "readme.txt");
        Assert.Empty(plain.Files);
    }

    [Fact]
    public void Browse_IncludeFiles_WithAnExtension_ListsOnlyThatKindOfFile()
    {
        File.WriteAllText(Path.Combine(_tempDir, "presets.json"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "PRESETS2.JSON"), "{}");
        File.WriteAllText(Path.Combine(_tempDir, "tool.exe"), "x");
        File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "x");

        var withDot = _browser.Browse(_tempDir, includeFiles: true, extension: ".json");
        var withoutDot = _browser.Browse(_tempDir, includeFiles: true, extension: "json");

        Assert.Equal(new[] { "presets.json", "PRESETS2.JSON" }.OrderBy(n => n), withDot.Files.Select(f => f.Name).OrderBy(n => n));
        Assert.Equal(withDot.Files.Select(f => f.Name), withoutDot.Files.Select(f => f.Name));
    }

    [Fact]
    public void Browse_EmptyPath_ReturnsRoots()
    {
        var result = _browser.Browse("");

        Assert.Null(result.CurrentPath);
        Assert.NotEmpty(result.Directories);
    }

    [Fact]
    public void Browse_NonExistentPath_FallsBackToRoots_DoesNotThrow()
    {
        var result = _browser.Browse(Path.Combine(_tempDir, "does-not-exist"));

        Assert.Null(result.CurrentPath);
    }

    [Fact]
    public void Browse_RealDirectory_ListsSubdirectoriesSorted()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "Zebra"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "Apple"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "mango"));
        File.WriteAllText(Path.Combine(_tempDir, "not-a-folder.txt"), "x"); // must be excluded, it's a file

        var result = _browser.Browse(_tempDir);

        Assert.Equal(_tempDir, result.CurrentPath);
        Assert.Equal(3, result.Directories.Count);
        Assert.Equal(new[] { "Apple", "mango", "Zebra" }, result.Directories.Select(d => d.Name));
    }

    [Fact]
    public void Browse_RealDirectory_ParentPathIsSet()
    {
        var subDir = Path.Combine(_tempDir, "Sub");
        Directory.CreateDirectory(subDir);

        var result = _browser.Browse(subDir);

        Assert.Equal(_tempDir, result.ParentPath);
    }

    [Fact]
    public void Browse_EmptyDirectory_ReturnsEmptyListNotError()
    {
        var result = _browser.Browse(_tempDir);

        Assert.Empty(result.Directories);
    }

    [Fact]
    public void Browse_FullPathsAreAbsoluteAndUsable()
    {
        var subDir = Path.Combine(_tempDir, "Child");
        Directory.CreateDirectory(subDir);

        var result = _browser.Browse(_tempDir);

        var entry = Assert.Single(result.Directories);
        Assert.True(Path.IsPathRooted(entry.FullPath));
        Assert.True(Directory.Exists(entry.FullPath));
    }
}
