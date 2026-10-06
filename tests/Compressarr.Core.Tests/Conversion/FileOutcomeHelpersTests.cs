using Compressarr.Core.Conversion;

namespace Compressarr.Core.Tests.Conversion;

/// <summary>The small pure helpers that classify what happened to a file. The disk-full one decides whether
/// monitoring stops itself, so a false positive is expensive - these pin exactly what counts.</summary>
public class FileOutcomeHelpersTests
{
    [Theory]
    [InlineData("ERROR: avformatMux: av_interleaved_write_frame failed with error 'No space left on device'", true)]
    [InlineData("no space left on device", true)]
    [InlineData("There is not enough space on the disk.", true)]
    [InlineData("Encode failed (error 4).", false)]
    [InlineData("Access to the path is denied.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeDiskFull_MatchesOnlyTheGenuineFullVolumeWordings(string? text, bool expected) =>
        Assert.Equal(expected, FileOutcomeHelpers.LooksLikeDiskFull(text));

    [Fact]
    public void LooksLikePathUnavailable_RecognisesAMissingOrUnreachableBasePath()
    {
        Assert.True(FileOutcomeHelpers.LooksLikePathUnavailable(new InvalidOperationException("Movie base path is not configured")));
        Assert.True(FileOutcomeHelpers.LooksLikePathUnavailable(new DirectoryNotFoundException("no such directory")));
        Assert.True(FileOutcomeHelpers.LooksLikePathUnavailable(new IOException("The network path was not found.")));
        Assert.True(FileOutcomeHelpers.LooksLikePathUnavailable(new IOException("The specified network name is no longer available.")));
        Assert.True(FileOutcomeHelpers.LooksLikePathUnavailable(new IOException("The system cannot find the path specified.")));
    }

    [Fact]
    public void LooksLikePathUnavailable_DoesNotBlameThePathForAPermissionOrLockProblem()
    {
        Assert.False(FileOutcomeHelpers.LooksLikePathUnavailable(new UnauthorizedAccessException("Access denied")));
        Assert.False(FileOutcomeHelpers.LooksLikePathUnavailable(new IOException("The process cannot access the file because it is being used by another process.")));
    }

    [Fact]
    public void AppendWarning_JoinsIndependentWarnings_WithoutLosingEither()
    {
        Assert.Equal("first", FileOutcomeHelpers.AppendWarning(null, "first"));
        Assert.Equal("first; second", FileOutcomeHelpers.AppendWarning("first", "second"));
    }
}
