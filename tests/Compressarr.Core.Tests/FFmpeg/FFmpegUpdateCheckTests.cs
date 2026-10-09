using Compressarr.Core.Dependencies;
using Compressarr.Core.FFmpeg;

namespace Compressarr.Core.Tests.FFmpeg;

public class FFmpegUpdateCheckTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("compressarr-ffupdate-tests-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FFmpegReleaseInfo Release(string name, string? sha) =>
        new(name, "ffmpeg-master-latest-win64-gpl.zip", "https://example.invalid/ffmpeg.zip", 1, "https://example.invalid/release", sha);

    private static FFmpegInstallMarker Marker(string name, string? sha) => new(name, "ffmpeg-master-latest-win64-gpl.zip", sha, DateTime.UtcNow);

    [Fact]
    public void NoWorkingFfmpeg_IsNotInstalled()
    {
        Assert.Equal(FFmpegUpdateStatus.NotInstalled, FFmpegUpdateCheck.Compare(false, null, Release("b", "AA")));
    }

    [Fact]
    public void AnFfmpegCompressarrDidNotInstall_CannotBeCompared()
    {
        Assert.Equal(FFmpegUpdateStatus.Unknown, FFmpegUpdateCheck.Compare(true, null, Release("b", "AA")));
    }

    [Fact]
    public void TheRecordedBuild_IsComparedByItsPublishedChecksum_IgnoringCase()
    {
        Assert.Equal(FFmpegUpdateStatus.UpToDate, FFmpegUpdateCheck.Compare(true, Marker("Latest Auto-Build (1)", "abc123"), Release("Latest Auto-Build (1)", "ABC123")));
        Assert.Equal(FFmpegUpdateStatus.Newer, FFmpegUpdateCheck.Compare(true, Marker("Latest Auto-Build (1)", "abc123"), Release("Latest Auto-Build (2)", "def456")));
    }

    [Fact]
    public void WithoutAChecksum_TheReleaseNameDecides()
    {
        Assert.Equal(FFmpegUpdateStatus.UpToDate, FFmpegUpdateCheck.Compare(true, Marker("Build 7", null), Release("Build 7", null)));
        Assert.Equal(FFmpegUpdateStatus.Newer, FFmpegUpdateCheck.Compare(true, Marker("Build 7", null), Release("Build 8", null)));
    }

    [Fact]
    public void TheMarker_IsWrittenBesideFfmpeg_AndReadBackFromThere()
    {
        FFmpegInstallMarkerFile.Write(_dir, Release("Latest Auto-Build (3)", "abc"));

        var marker = FFmpegInstallMarkerFile.Read(Path.Combine(_dir, "ffmpeg.exe"));

        Assert.NotNull(marker);
        Assert.Equal("Latest Auto-Build (3)", marker!.Name);
        Assert.Equal("abc", marker.Sha256);
    }

    [Fact]
    public void AMissingOrDamagedMarker_IsSimplyNone()
    {
        Assert.Null(FFmpegInstallMarkerFile.Read(Path.Combine(_dir, "ffmpeg.exe")));
        Assert.Null(FFmpegInstallMarkerFile.Read(null));

        File.WriteAllText(Path.Combine(_dir, FFmpegInstallMarkerFile.FileName), "{ not json");
        Assert.Null(FFmpegInstallMarkerFile.Read(Path.Combine(_dir, "ffmpeg.exe")));
    }

    [Fact]
    public void HandBrakeInstall_LeavesALicenseNoteBesideTheProgram()
    {
        HandBrakeInstaller.WriteLicenseNotice(_dir);

        var note = File.ReadAllText(Path.Combine(_dir, "LICENSE-NOTICE.txt"));
        Assert.Contains("GNU General Public License", note);
        Assert.Contains("https://github.com/HandBrake/HandBrake", note);
    }
}
