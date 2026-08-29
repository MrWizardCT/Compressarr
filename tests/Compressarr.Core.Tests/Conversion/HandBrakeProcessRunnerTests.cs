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
    public void DetermineSuccess_OutputExistsAndNonEmpty_AndFinishedLinePresent_IsSuccess()
    {
        var output = WriteFile("out.mp4", "some encoded bytes");
        var log = WriteFile("detail.log", "Encoding...\nFinished work at Sat Jan  1 00:00:00 2026\n");

        Assert.True(HandBrakeProcessRunner.DetermineSuccess(output, log));
    }

    [Fact]
    public void DetermineSuccess_NonEmptyFile_ButNoFinishedLine_IsFailure()
    {
        // Proves the AND is enforced: a plausible-looking output file alone isn't enough.
        var output = WriteFile("out.mp4", "some encoded bytes");
        var log = WriteFile("detail.log", "Encoding started...\nEncoding aborted.\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log));
    }

    [Fact]
    public void DetermineSuccess_FinishedLinePresent_ButOutputIsZeroLength_IsFailure()
    {
        // Proves the AND is enforced the other direction: the completion banner alone isn't enough.
        var output = WriteFile("out.mp4", "");
        var log = WriteFile("detail.log", "Finished work at Sat Jan  1 00:00:00 2026\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log));
    }

    [Fact]
    public void DetermineSuccess_OutputFileMissing_IsFailure()
    {
        var output = Path.Combine(_tempDir, "never-written.mp4");
        var log = WriteFile("detail.log", "Finished work at Sat Jan  1 00:00:00 2026\n");

        Assert.False(HandBrakeProcessRunner.DetermineSuccess(output, log));
    }
}
