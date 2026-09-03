using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Compressarr.Core.Conversion;

public sealed record HandBrakeRunResult(bool Success, string DetailLogFile, bool Cancelled = false);

public interface IHandBrakeProcessRunner
{
    /// <summary>Invokes HandBrakeCLI (one file fully finishes before the caller moves on) and
    /// determines success the same way v1 did, plus a real exit-code check: the output temp file
    /// must exist, be non-empty, the stderr detail log must contain a line matching "*Finished
    /// work at*" (HandBrake's own completion banner), AND the process must have exited 0.
    /// The exit-code check is not optional - confirmed live against a genuinely full disk that
    /// HandBrakeCLI still writes "Finished work at" to its log even when the encode fails (mux
    /// error, exit code 4) - the log-banner check alone reports a mid-air-truncated file as a
    /// success, which without the exit code would also delete/route the source out from under it.
    ///
    /// Streams stdout line-by-line to onOutputLine as HandBrakeCLI writes it (its own progress
    /// updates, "Encoding: task 1 of 1, NN.NN % ...") so a caller can surface live progress.
    /// Cancelling cancellationToken kills the HandBrakeCLI process tree immediately and returns a
    /// result with Cancelled=true instead of throwing - the caller decides what "aborted
    /// mid-file" means for the rest of the run.</summary>
    Task<HandBrakeRunResult> RunAsync(
        string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName,
        string? extraOptions, string detailLogFile, Action<string>? onOutputLine, CancellationToken cancellationToken);
}

public sealed class HandBrakeProcessRunner : IHandBrakeProcessRunner
{
    private readonly IActiveHandBrakeProcess _activeProcess;

    public HandBrakeProcessRunner(IActiveHandBrakeProcess activeProcess)
    {
        _activeProcess = activeProcess;
    }

    public async Task<HandBrakeRunResult> RunAsync(
        string cliPath, string sourcePath, string tempOutputPath, string presetsPath, string presetName,
        string? extraOptions, string detailLogFile, Action<string>? onOutputLine, CancellationToken cancellationToken)
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

        var stderr = new StringBuilder();
        var cancelled = false;
        var exitCode = -1;

        using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
        {
            // HandBrakeCLI writes its progress updates ("Encoding: task 1 of 1, NN.NN % ...") to
            // stdout as it runs - event-driven reading (rather than the old ReadToEndAsync, which
            // only ever saw the output after the process had already exited) is what makes live
            // progress possible at all.
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null) onOutputLine?.Invoke(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null) stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _activeProcess.Register(process);

            using var killRegistration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort - process may have already exited between the check and Kill.
                }
            });

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            finally
            {
                _activeProcess.Unregister();
            }

            File.WriteAllText(detailLogFile, stderr.ToString());
        }

        if (cancelled)
        {
            return new HandBrakeRunResult(Success: false, DetailLogFile: detailLogFile, Cancelled: true);
        }

        return new HandBrakeRunResult(DetermineSuccess(tempOutputPath, detailLogFile, exitCode), detailLogFile);
    }

    /// <summary>Success requires ALL of: the temp output file exists, is non-empty, the detail
    /// log contains a line matching "*Finished work at*" (HandBrake's own completion banner), AND
    /// the process exited 0. The exit-code check matters on its own, not just as a formality -
    /// HandBrakeCLI still writes "Finished work at" even on a failed encode (confirmed against a
    /// genuinely full disk: mux error, non-zero exit, but the banner line was there anyway), so
    /// without it a failed encode reports as a success. Extracted as an internal static method so
    /// these conditions can be tested independently of actually invoking a process.</summary>
    internal static bool DetermineSuccess(string tempOutputPath, string detailLogFile, int exitCode)
    {
        if (!File.Exists(tempOutputPath)) return false;
        if (exitCode != 0) return false;

        var hasFinishedLine = File.Exists(detailLogFile) &&
            File.ReadLines(detailLogFile).Any(l => l.Contains("Finished work at", StringComparison.Ordinal));
        var nonEmpty = new FileInfo(tempOutputPath).Length > 0;

        return hasFinishedLine && nonEmpty;
    }
}

public sealed record HandBrakeProgress(double Percent, double? Fps, string? Eta);

/// <summary>
/// Parses HandBrakeCLI's own stdout progress line, e.g.:
///   "Encoding: task 1 of 1, 42.10 % (23.45 fps, avg 20.12 fps, ETA 00h05m32s)"
/// A pure static function (same pattern as HandBrakeProcessRunner.DetermineSuccess) so parsing is
/// unit-testable against real-shaped fixture strings without invoking a process.
/// </summary>
public static class HandBrakeProgressParser
{
    private static readonly Regex ProgressLine = new(
        @"^Encoding:\s*task\s*\d+\s*of\s*\d+,\s*(?<percent>[\d.]+)\s*%(?:\s*\((?:(?<fps>[\d.]+)\s*fps[^,)]*,\s*)?avg\s*[\d.]+\s*fps(?:,\s*ETA\s*(?<eta>[\dhms]+))?\))?",
        RegexOptions.Compiled);

    public static HandBrakeProgress? TryParse(string line)
    {
        var match = ProgressLine.Match(line);
        if (!match.Success) return null;

        if (!double.TryParse(match.Groups["percent"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
        {
            return null;
        }

        double? fps = match.Groups["fps"].Success &&
            double.TryParse(match.Groups["fps"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fpsValue)
                ? fpsValue
                : null;

        string? eta = match.Groups["eta"].Success ? match.Groups["eta"].Value : null;

        return new HandBrakeProgress(percent, fps, eta);
    }
}
