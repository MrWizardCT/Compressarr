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
    /// full paths - a file whose original path still exists after its group's FileBot call is
    /// "unmatched" (FileBot didn't touch it), whether because FileBot itself couldn't confidently
    /// match it, its group's Args are blank, or its group is turned off entirely. A file that was
    /// already perfectly named (nothing to rename) is indistinguishable from a genuine no-match under
    /// this heuristic - an accepted limitation for a placeholder signal, not a rigorous
    /// match-confidence readout from FileBot itself.</summary>
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
            logger.Log($"FileBot is enabled but its path '{settings.CliPath}' was not found - skipping.", LogSeverity.Error);
            return new HashSet<string>();
        }

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
        InvokeProcess(cliPath, args, logger);

        // We know exactly which paths we handed FileBot - anything still sitting at its original
        // path afterward is exactly what FileBot left alone, no need to rescan the whole folder.
        return files.Select(f => f.FullName).Where(File.Exists).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private void InvokeProcess(string cliPath, string args, IRunLogger logger)
    {
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
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (stdout) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (stderr) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit((int)ProcessTimeout.TotalMilliseconds))
            {
                logger.Log($"[FileBot] Did not exit within {ProcessTimeout.TotalMinutes:0} minutes - likely stuck open (its GUI, a license prompt, etc.) - killing it and continuing with whatever it left behind.", LogSeverity.Error);
                try { process.Kill(entireProcessTree: true); } catch { /* best effort - it may already be gone */ }
                return;
            }

            var stdoutText = stdout.ToString().Trim();
            var stderrText = stderr.ToString().Trim();
            if (stdoutText.Length > 0) logger.Log($"[FileBot] {stdoutText}");
            if (stderrText.Length > 0) logger.Log($"[FileBot] {stderrText}", LogSeverity.Error);
            if (process.ExitCode != 0)
            {
                // FileBot exits non-zero even for a completely benign "nothing to do" outcome -
                // e.g. every file already has its correct name, so it reports "already exists" and
                // "Processed 0 files" with no real problem. Confirmed live: that case's own output
                // never includes FileBot's own "Error (o_O)" failure marker (genuine failures -
                // network errors, exceptions - always do), so that marker, not the exit code alone,
                // decides whether this is worth flagging as an actual error.
                var isRealFailure = stdoutText.Contains("Error (o_O)") || stderrText.Contains("Error (o_O)");
                if (isRealFailure)
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
    }
}
