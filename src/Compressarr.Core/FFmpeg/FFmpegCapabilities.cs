using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Compressarr.Core.FFmpeg;

/// <summary>What the installed ffmpeg can do, found by asking it once: its version, the encoders it
/// was built with, and - for NVENC - whether a GPU session really opens (a build can list
/// hevc_nvenc on a machine with no NVIDIA GPU).</summary>
public sealed record FFmpegCapabilities(string? Version, IReadOnlySet<string> Encoders, bool? NvencSessionOk, string? BuildNote)
{
    public bool Has(string encoder) => Encoders.Contains(encoder);

    /// <summary>The hardware encoders a profile can only use if this returns true.</summary>
    public bool CanUse(string encoder) => encoder.EndsWith("_nvenc", StringComparison.Ordinal)
        ? Has(encoder) && NvencSessionOk == true
        : Has(encoder);
}

public static partial class FFmpegCapabilityParser
{
    [GeneratedRegex(@"ffmpeg version (?<v>\S+)")]
    private static partial Regex VersionLine();

    // " V....D libx265             libx265 H.265 / HEVC (codec hevc)" - flags, then the encoder's name
    [GeneratedRegex(@"^\s*[VAS][\w.]{5}\s+(?<name>\S+)\s", RegexOptions.Multiline)]
    private static partial Regex EncoderLine();

    public static string? ParseVersion(string versionOutput)
    {
        var m = VersionLine().Match(versionOutput);
        return m.Success ? m.Groups["v"].Value : null;
    }

    public static IReadOnlySet<string> ParseEncoders(string encodersOutput)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Everything before the "------" separator is the legend, not an encoder.
        var start = encodersOutput.IndexOf("------", StringComparison.Ordinal);
        var body = start >= 0 ? encodersOutput[(start + 6)..] : encodersOutput;
        foreach (Match m in EncoderLine().Matches(body)) set.Add(m.Groups["name"].Value);
        return set;
    }

    /// <summary>"GPL build, from BtbN" style note from the configuration line, when it can be told.</summary>
    public static string? ParseBuildNote(string versionOutput)
    {
        var gpl = versionOutput.Contains("--enable-gpl", StringComparison.Ordinal);
        var nonfree = versionOutput.Contains("--enable-nonfree", StringComparison.Ordinal);
        if (!gpl && !nonfree) return null;
        return nonfree ? "non-free build" : "GPL build";
    }
}

/// <summary>The encoders a profile needs that an ffmpeg build doesn't have - how a profile is flagged
/// on the Lanes and Profiles pages instead of failing in the middle of a run.</summary>
public static class FFmpegProfileRequirements
{
    public static IReadOnlyList<string> Missing(FFmpegProfile profile, FFmpegCapabilities caps)
    {
        var missing = new List<string>();
        if (!caps.CanUse(profile.VideoCodec))
        {
            missing.Add(profile.VideoCodec.EndsWith("_nvenc", StringComparison.Ordinal) && caps.Has(profile.VideoCodec)
                ? $"{profile.VideoCodec} (no NVIDIA GPU session could be opened)"
                : profile.VideoCodec);
        }

        // The audio encoder is needed whenever audio can be encoded (always in "encode" mode; as the
        // fallback for tracks that can't be copied in pass-through mode).
        var audio = FFmpegCommandBuilder.AudioEncoderName(profile.AudioCodec);
        if (!caps.Has(audio)) missing.Add(audio);

        return missing;
    }
}

public interface IFFmpegCapabilityProbe
{
    /// <summary>Asks ffmpeg what it can do. Null when ffmpeg can't be run at all.</summary>
    Task<FFmpegCapabilities?> DetectAsync(string ffmpegPath, CancellationToken cancellationToken);

    /// <summary>Just the version line and build note - one quick call, for places (the About page) that don't need the
    /// encoder list or a GPU test. Null when ffmpeg can't be run or isn't ffmpeg.</summary>
    Task<(string? Version, string? BuildNote)?> ReadVersionAsync(string ffmpegPath, CancellationToken cancellationToken);
}

public sealed class FFmpegCapabilityProbe : IFFmpegCapabilityProbe
{
    public async Task<(string? Version, string? BuildNote)?> ReadVersionAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(ffmpegPath)) return null;
        var version = await RunAsync(ffmpegPath, new[] { "-hide_banner", "-version" }, cancellationToken, TimeSpan.FromSeconds(8));
        if (version is null || FFmpegCapabilityParser.ParseVersion(version.Output) is null) return null;
        return (FFmpegCapabilityParser.ParseVersion(version.Output), FFmpegCapabilityParser.ParseBuildNote(version.Output));
    }

    public async Task<FFmpegCapabilities?> DetectAsync(string ffmpegPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(ffmpegPath)) return null;

        // Short timeout: a real ffmpeg answers at once, and a program that isn't ffmpeg (a GUI app, say) would
        // otherwise sit there for the full 30 seconds.
        var version = await RunAsync(ffmpegPath, new[] { "-hide_banner", "-version" }, cancellationToken, TimeSpan.FromSeconds(8));
        if (version is null || FFmpegCapabilityParser.ParseVersion(version.Output) is null) return null;
        var encoders = await RunAsync(ffmpegPath, new[] { "-hide_banner", "-encoders" }, cancellationToken);
        if (encoders is null) return null;

        var set = FFmpegCapabilityParser.ParseEncoders(encoders.Output);

        bool? nvenc = null;
        if (set.Contains("hevc_nvenc"))
        {
            // A one-frame throwaway encode: it only works when a GPU session actually opens.
            var test = await RunAsync(ffmpegPath, new[]
            {
                "-hide_banner", "-nostdin", "-f", "lavfi", "-i", "nullsrc=s=256x256:d=0.2",
                "-c:v", "hevc_nvenc", "-f", "null", "-"
            }, cancellationToken);
            nvenc = test is { ExitCode: 0 };
        }

        return new FFmpegCapabilities(
            FFmpegCapabilityParser.ParseVersion(version.Output),
            set,
            nvenc,
            FFmpegCapabilityParser.ParseBuildNote(version.Output));
    }

    private sealed record Result(int ExitCode, string Output);

    private static async Task<Result?> RunAsync(string path, IEnumerable<string> args, CancellationToken ct, TimeSpan? limit = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var a in args) startInfo.ArgumentList.Add(a);

            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(limit ?? TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }

            return new Result(process.ExitCode, (await stdout) + (await stderr));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
