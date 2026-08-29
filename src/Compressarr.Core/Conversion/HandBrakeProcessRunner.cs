using System.Diagnostics;

namespace Compressarr.Core.Conversion;

public sealed record HandBrakeRunResult(bool Success, string DetailLogFile);

public interface IHandBrakeProcessRunner
{
    /// <summary>Invokes HandBrakeCLI synchronously (one file fully finishes before the caller
    /// moves on) and determines success the same way v1 does: the output temp file must exist,
    /// be non-empty, AND the stderr detail log must contain a line matching "*Finished work at*"
    /// (HandBrake's own completion banner). Deliberately no process exit-code check — ported
    /// as-is for parity with v1's success-detection logic, not because it's ideal.</summary>
    HandBrakeRunResult Run(string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName, string? extraOptions, string detailLogFile);
}

public sealed class HandBrakeProcessRunner : IHandBrakeProcessRunner
{
    public HandBrakeRunResult Run(string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName, string? extraOptions, string detailLogFile)
    {
        if (File.Exists(detailLogFile))
        {
            File.Delete(detailLogFile);
        }

        var args = $"-i \"{sourcePath}\" -t 1 -o \"{tempOutputPath}\" --preset-import-file \"{presetsPath}\" --preset \"{presetName}\"";
        if (!string.IsNullOrWhiteSpace(extraOptions))
        {
            args += " " + extraOptions;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using (var process = new Process { StartInfo = startInfo })
        {
            process.Start();

            // Read both streams concurrently (not sequentially) to avoid the classic deadlock:
            // if stdout's pipe buffer fills while we're blocked reading stderr to completion (or
            // vice versa), the child process stalls writing and never exits.
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            process.WaitForExit();
            var stderr = stderrTask.GetAwaiter().GetResult();
            stdoutTask.GetAwaiter().GetResult();

            File.WriteAllText(detailLogFile, stderr);
        }

        return new HandBrakeRunResult(DetermineSuccess(tempOutputPath, detailLogFile), detailLogFile);
    }

    /// <summary>Success requires ALL of: the temp output file exists, is non-empty, AND the
    /// detail log contains a line matching "*Finished work at*" (HandBrake's own completion
    /// banner). Deliberately no process exit-code check — ported as-is for parity with v1.
    /// Extracted as an internal static method so the two conditions can be tested
    /// independently of actually invoking a process.</summary>
    internal static bool DetermineSuccess(string tempOutputPath, string detailLogFile)
    {
        if (!File.Exists(tempOutputPath)) return false;

        var hasFinishedLine = File.Exists(detailLogFile) &&
            File.ReadLines(detailLogFile).Any(l => l.Contains("Finished work at", StringComparison.Ordinal));
        var nonEmpty = new FileInfo(tempOutputPath).Length > 0;

        return hasFinishedLine && nonEmpty;
    }
}
