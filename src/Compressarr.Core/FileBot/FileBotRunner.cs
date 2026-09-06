using System.Diagnostics;
using Compressarr.Core.Config;
using Compressarr.Core.Conversion;
using Compressarr.Core.Logging;

namespace Compressarr.Core.FileBot;

public interface IFileBotRunner
{
    /// <summary>Runs FileBot against inputPath if settings.Enabled and CliPath resolves to a real
    /// file; no-ops (returns an empty set, no process started) otherwise. Returns the set of
    /// video-file full paths that were present both BEFORE and AFTER the run - i.e. FileBot didn't
    /// touch/rename them - treated as "FileBot didn't/couldn't confidently match this one." A file
    /// that was already perfectly named (nothing to rename) is indistinguishable from a genuine
    /// no-match under this heuristic - an accepted limitation for a placeholder signal, not a
    /// rigorous match-confidence readout from FileBot itself.</summary>
    HashSet<string> Run(FileBotSettings settings, string inputPath, IReadOnlyList<string> vidTypes, IRunLogger logger);
}

/// <summary>Optional pre-processing pass, invoked once per lane right before Compressarr's own
/// Input scan (see ConversionOrchestrator.PrepareLane) - synchronous, one-shot, no live progress
/// to surface, so this deliberately mirrors RunOrchestrator's simple PostExec invocation rather
/// than IHandBrakeProcessRunner's async/cancellable/streaming machinery.</summary>
public sealed class FileBotRunner : IFileBotRunner
{
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

        var before = SnapshotVideoPaths(inputPath, vidTypes);
        var args = (settings.Args ?? "").Replace("{input}", inputPath);

        logger.Log($"[FileBot] Running: \"{settings.CliPath}\" {args}");

        try
        {
            var startInfo = new ProcessStartInfo(settings.CliPath, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                logger.Log("[FileBot] Failed to start the process.", LogSeverity.Error);
                return new HashSet<string>();
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrWhiteSpace(stdout)) logger.Log($"[FileBot] {stdout.Trim()}");
            if (!string.IsNullOrWhiteSpace(stderr)) logger.Log($"[FileBot] {stderr.Trim()}", LogSeverity.Error);
            if (process.ExitCode != 0)
            {
                logger.Log($"[FileBot] Exited with code {process.ExitCode} - continuing with whatever it left behind.", LogSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            // Never let an optional cleanup step block the lane's actual conversion work.
            logger.Log($"[FileBot] Failed to run: {ex.Message}", LogSeverity.Error);
            return new HashSet<string>();
        }

        var after = SnapshotVideoPaths(inputPath, vidTypes);
        return ComputeUnmatched(before, after);
    }

    private HashSet<string> SnapshotVideoPaths(string inputPath, IReadOnlyList<string> vidTypes) =>
        _scanner.FindVideoFiles(inputPath, vidTypes, minSizeBytes: 0, limit: 0)
            .Select(f => f.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pure diff logic, split out for direct unit testing without a real process
    /// invocation - the intersection of before/after is whatever FileBot left completely
    /// untouched.</summary>
    internal static HashSet<string> ComputeUnmatched(HashSet<string> before, HashSet<string> after)
    {
        var unmatched = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
        unmatched.IntersectWith(before);
        return unmatched;
    }
}
