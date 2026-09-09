using System.Diagnostics;
using System.Text;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;

namespace Compressarr.Core.FileBot;

public interface IFileBotRunner
{
    /// <summary>Runs FileBot against inputPath if settings.Enabled and CliPath resolves to a real
    /// file; no-ops (returns an empty set, no process started) otherwise. Splits the folder's video
    /// files into TV/movie groups (via ContentClassifier.IsTvFile, the same classifier Compressarr's
    /// own routing already trusts) and invokes FileBot once per group that's enabled and has files,
    /// using that group's own Args - never both groups in one call, so each group's own --db/--format
    /// choice can never affect the other content type. Returns the union of both groups' "unmatched"
    /// full paths - a file is "unmatched" only if its original path still exists AND its path never
    /// appears anywhere in FileBot's own output, meaning FileBot genuinely never engaged with it
    /// (couldn't confidently match it, its group's Args are blank, or its group is turned off
    /// entirely). A file FileBot confirmed was already correctly named - logged as "[MOVE] Skipped
    /// [X] because [X] already exists," its path unchanged but still mentioned - is NOT unmatched,
    /// since FileBot did successfully identify it.</summary>
    HashSet<string> Run(FileBotSettings settings, string inputPath, IReadOnlyList<string> vidTypes, IRunLogger logger);
}

/// <summary>Optional pre-processing pass, invoked once per lane right before Compressarr's own
/// Input scan (see ConversionOrchestrator.PrepareLane) - synchronous, one-shot, no live progress
/// to surface, so this deliberately mirrors RunOrchestrator's simple PostExec invocation rather
/// than IHandBrakeProcessRunner's async/cancellable/streaming machinery.
///
/// Confirmed live: a bare "filebot.exe" launch with no arguments at all just opens FileBot's own
/// GUI (expected behavior for a GUI app given zero CLI args, not a Compressarr bug) - it then sits
/// there until a person closes it, once per enabled lane. Guards against that (blank Args for a
/// group with files is treated the same as "not configured," never invoked) and against ANY other
/// way FileBot could end up blocking indefinitely (a license-activation prompt, a crash, etc.) via
/// ProcessTimeout - past that, the process (and its tree, since FileBot's own launcher spawns a
/// separate java process) is killed and the pass continues with whatever FileBot left behind,
/// rather than wedging the whole run with no way out short of a person manually intervening.</summary>
public sealed class FileBotRunner : IFileBotRunner
{
    // Real TheTVDB/TMDB lookups over a whole season can legitimately take a couple of minutes;
    // nothing FileBot does here should ever need anywhere close to this long, so it's a generous
    // upper bound whose only real job is guaranteeing the pass can't hang forever.
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(10);

    private readonly IVideoFileScanner _scanner;

    public FileBotRunner(IVideoFileScanner scanner)
    {
        _scanner = scanner;
    }

    public HashSet<string> Run(FileBotSettings settings, string inputPath, IReadOnlyList<string> vidTypes, IRunLogger logger)
    {
        if (!settings.Enabled) return new HashSet<string>();

        if (string.IsNullOrWhiteSpace(settings.CliPath) || !File.Exists(settings.CliPath))
        {
            // Keyed by inputPath (stable per lane) rather than a lane id - this method never
            // receives one. Runs unconditionally on every pass FileBot is enabled for, regardless
            // of whether there are new files, so a broken path would otherwise log Error on every
            // single poll for as long as it stays broken - the same standing-condition
            // file-proliferation problem RunOrchestrator's own config checks have.
            logger.LogProblem($"filebot-path-missing:{inputPath}", $"FileBot is enabled but its path '{settings.CliPath}' was not found - skipping.");
            return new HashSet<string>();
        }
        logger.ClearProblem($"filebot-path-missing:{inputPath}");

        var allFiles = _scanner.FindVideoFiles(inputPath, vidTypes, minSizeBytes: 0, limit: 0);
        var tvFiles = allFiles.Where(f => ContentClassifier.IsTvFile(f.Name)).ToList();
        var movieFiles = allFiles.Except(tvFiles).ToList();

        var unmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        unmatched.UnionWith(RunGroup(settings.CliPath, settings.TvArgs, tvFiles, settings.TvEnabled, "TV", logger));
        unmatched.UnionWith(RunGroup(settings.CliPath, settings.MovieArgs, movieFiles, settings.MovieEnabled, "movie", logger));
        return unmatched;
    }

    private HashSet<string> RunGroup(string cliPath, string? argsTemplate, List<FileInfo> files, bool typeEnabled, string label, IRunLogger logger)
    {
        if (files.Count == 0) return new HashSet<string>();

        // A deliberate per-type opt-out is quiet - nothing to warn about, the user asked for this.
        if (!typeEnabled) return new HashSet<string>();

        var args = (argsTemplate ?? "").Trim();
        if (args.Length == 0)
        {
            logger.Log($"FileBot is enabled but no {label} arguments are configured - {files.Count} {label} file(s) left as-is.", LogSeverity.Error);
            return files.Select(f => f.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        var fileList = string.Join(" ", files.Select(f => $"\"{f.FullName}\""));
        args = args.Replace("{files}", fileList);

        logger.Log($"[FileBot] Running ({label}): \"{cliPath}\" {args}");
        var output = InvokeProcess(cliPath, args, logger);

        // A file whose original path is gone was renamed/moved away - matched. A file whose path
        // is untouched could still be a genuine match: FileBot logs "[MOVE] Skipped [X] because [X]
        // already exists" for a file that was ALREADY correctly named (confirmed live - a file
        // Compressarr already routed correctly on an earlier pass triggers this every time it's
        // re-scanned). Only a path that never appears anywhere in FileBot's own output at all - it
        // genuinely never engaged with that file - counts as unmatched.
        return files
            .Select(f => f.FullName)
            .Where(path => File.Exists(path) && !output.Contains(path, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private string InvokeProcess(string cliPath, string args, IRunLogger logger)
    {
        var output = new StringBuilder();
        try
        {
            var startInfo = new ProcessStartInfo(cliPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = startInfo };
            var stateLock = new object();
            var sawErrorMarker = false;

            // Logged line-by-line as FileBot actually prints it, not batched until the whole
            // process exits - a real TheTVDB/TMDB lookup can take real wall-clock time, and the
            // Monitor page's Recent Log otherwise shows nothing at all for however long that takes.
            // Also accumulated (see RunGroup above) so a per-file match can be confirmed afterward
            // even when nothing about that file's path actually changed on disk.
            void HandleLine(string data, LogSeverity severity)
            {
                logger.Log($"[FileBot] {data}", severity);
                lock (stateLock)
                {
                    output.AppendLine(data);
                    if (data.Contains("Error (o_O)")) sawErrorMarker = true;
                }
            }
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data, LogSeverity.Info); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) HandleLine(e.Data, LogSeverity.Error); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                logger.Log($"[FileBot] Did not exit within {ProcessTimeout.TotalMinutes:0} minutes - likely stuck open (its GUI, a license prompt, etc.) - killing it and continuing with whatever it left behind.", LogSeverity.Error);
                try { process.Kill(entireProcessTree: true); } catch { /* best effort - it may already be gone */ }
                return output.ToString();
            }

            // The timeout-based overload above can return true before the async
            // OutputDataReceived/ErrorDataReceived callbacks for a fast-exiting process's last
            // lines have actually fired - a well-known .NET race, confirmed flaky here live. The
            // parameterless overload blocks until the redirected streams are fully drained too, so
            // sawErrorMarker/output below always reflect everything the process actually printed.
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                // FileBot exits non-zero even for a completely benign "nothing to do" outcome -
                // e.g. every file already has its correct name, so it reports "already exists" and
                // "Processed 0 files" with no real problem. Confirmed live: that case's own output
                // never includes FileBot's own "Error (o_O)" failure marker (genuine failures -
                // network errors, exceptions - always do), so that marker, not the exit code alone,
                // decides whether this is worth flagging as an actual error.
                if (sawErrorMarker)
                {
                    logger.Log($"[FileBot] Exited with code {process.ExitCode} - continuing with whatever it left behind.", LogSeverity.Error);
                }
                else
                {
                    logger.Log($"[FileBot] Exited with code {process.ExitCode} (nothing left to rename) - continuing.");
                }
            }
        }
        catch (Exception ex)
        {
            // Never let an optional cleanup step block the lane's actual conversion work.
            logger.Log($"[FileBot] Failed to run: {ex.Message}", LogSeverity.Error);
        }

        return output.ToString();
    }
}
