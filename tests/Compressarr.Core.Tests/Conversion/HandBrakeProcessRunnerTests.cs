using Compressarr.Core.Conversion;

namespace Compressarr.Core.Tests.Conversion;

public class HandBrakeProcessRunnerTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("compressarr-hb-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string WriteFile(string name, string? content)
    {
        var path = Path.Combine(_tempDir, name);
        if (content is null)
        {
            // Represents "file does not exist" - caller just gets the path back unwritten.
        }
        else
        {
            File.WriteAllText(path, content);
        }
        return path;
    }

    [Fact]
    public void DetermineSuccess_OutputExistsAndNonEmpty_FinishedLinePresent_ExitCodeZero_IsSuccess()
    {
        var output = WriteFile("out.mp4", "some encoded bytes");
        var log = WriteFile("detail.log", "Encoding...\nFinished work at Sat Jan  1 00:00:00 2026\n");

        Assert.True(HandBrakeProcessRunner.DetermineSuccess(output, log, exitCode: 0));
    }

    [Fact]
    public void DetermineSuccess_NonEmptyFile_ButNoFinishedLine_IsFailure()
    {
        // Proves the AND is enforced: a plausible-looking output file alone isn't enough.
        var output = WriteFile("out.mp4", "some encoded bytes");
        var log = WriteFile("detail.log", "Encoding started...\nEncoding aborted.\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log, exitCode: 0));
    }

    [Fact]
    public void DetermineSuccess_FinishedLinePresent_ButOutputIsZeroLength_IsFailure()
    {
        // Proves the AND is enforced the other direction: the completion banner alone isn't enough.
        var output = WriteFile("out.mp4", "");
        var log = WriteFile("detail.log", "Finished work at Sat Jan  1 00:00:00 2026\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log, exitCode: 0));
    }

    [Fact]
    public void DetermineSuccess_OutputFileMissing_IsFailure()
    {
        var output = Path.Combine(_tempDir, "never-written.mp4");
        var log = WriteFile("detail.log", "Finished work at Sat Jan  1 00:00:00 2026\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log, exitCode: 0));
    }

    [Fact]
    public void DetermineSuccess_NonZeroExitCode_IsFailure_EvenWithFinishedLineAndNonEmptyOutput()
    {
        // Confirmed live against a genuinely full disk: HandBrakeCLI still writes "Finished work
        // at" to its own log even when the encode actually failed (mux error, exit code 4,
        // "libhb: work result = 4") - a truncated/corrupt output file with the banner line
        // present is exactly what a disk-full failure looks like. Without checking the exit code,
        // this reports as a success, which would then move the corrupt file into place and
        // potentially delete/recycle the real source out from under it.
        var output = WriteFile("out.mp4", "partial encoded bytes, disk filled up mid-write");
        var log = WriteFile("detail.log",
            "ERROR: avformatMux: track 0, av_interleaved_write_frame failed with error 'No space left on device'\n" +
            "Finished work at Sat Jan  1 00:00:00 2026\n" +
            "libhb: work result = 4\n" +
            "Encode failed (error 4).\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log, exitCode: 4));
    }
}
